#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""编译门禁纯函数库：归属解析、新鲜度校验、日志/xml 解析、报告文本生成。

判定规则（M1-S01-fix3）：以物理证据为主——程序集新鲜度 + 测试结果。
日志 error CS 行降为诊断项：Unity 增量编译可能对已删除文件报 CS2001，
随后同一次运行内自愈重编译成功（程序集全部重建、测试全绿），属噪音。
"""

import json
import os
import re
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path

EXCLUDE_DIRS = {"Library", "Temp", "obj", "bin"}
STALE_TOLERANCE_SEC = 2.0
ERROR_CS_RE = re.compile(r"error CS\d+")


def fmt_ts(ts: float) -> str:
    return datetime.fromtimestamp(ts).strftime("%Y-%m-%d %H:%M:%S")


def is_project_assembly(name: str) -> bool:
    """只校验工程自有程序集：Assembly-CSharp* 与 IronCurtain.*。"""
    if name in ("Assembly-CSharp.dll", "Assembly-CSharp-Editor.dll"):
        return True
    return name.startswith("IronCurtain.")


def scan_asmdefs(project: Path) -> list[tuple[str, Path]]:
    """扫描 Assets/ 下所有 .asmdef，返回 (程序集名, asmdef 所在目录)。"""
    result = []
    assets = project / "Assets"
    if not assets.is_dir():
        return result
    for root, dirs, files in os.walk(assets):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        for f in files:
            if not f.endswith(".asmdef"):
                continue
            p = Path(root) / f
            try:
                data = json.loads(p.read_text(encoding="utf-8", errors="replace"))
            except (OSError, ValueError):
                continue  # 解析失败的 asmdef 不参与归属，文件回退到 Assembly-CSharp
            name = data.get("name")
            if isinstance(name, str) and name:
                result.append((name, p.parent))
    return result


def attribute_cs_files(project: Path, asmdefs: list[tuple[str, Path]]
                       ) -> dict[str, tuple[float, str]]:
    """把每个 .cs 归属到程序集，返回 {程序集名: (最大 mtime, 对应 .cs 路径)}。

    归属规则：最深的祖先 asmdef 目录；无 asmdef 归属 → Assembly-CSharp，
    其中路径中含 Editor 目录段的 → Assembly-CSharp-Editor。
    """
    max_map: dict[str, tuple[float, str]] = {}
    for root, dirs, files in os.walk(project):
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]
        rel_root = Path(root).relative_to(project)
        fallback = "Assembly-CSharp-Editor" if "Editor" in rel_root.parts else "Assembly-CSharp"
        for f in files:
            if not f.endswith(".cs"):
                continue
            p = Path(root) / f
            try:
                m = p.stat().st_mtime
            except OSError:
                continue  # 扫描期间文件被删（编辑器自动清理）属正常竞态，跳过即可
            best_name, best_len = None, -1
            for name, adir in asmdefs:
                try:
                    rel = p.relative_to(adir)
                except ValueError:
                    continue
                depth = len(rel.parts)
                if depth > best_len:
                    best_name, best_len = name, depth
            owner = best_name if best_name is not None else fallback
            cur = max_map.get(owner)
            if cur is None or m > cur[0]:
                max_map[owner] = (m, str(p))
    return max_map


def check_stale_assemblies(project: Path, src_map: dict[str, tuple[float, str]]
                           ) -> tuple[bool, list[str]]:
    """防伪绿核心：逐工程程序集断言 dll_mtime + 2s >= 其归属源码的最大 mtime。"""
    sa_dir = project / "Library" / "ScriptAssemblies"
    bad, ok_count, lines = False, 0, []
    if not sa_dir.is_dir():
        return False, ["- Library/ScriptAssemblies 不存在（Unity 从未编译过？）"]
    for asm in sorted(src_map):
        if not is_project_assembly(asm + ".dll"):
            continue
        src_max, src_file = src_map[asm]
        dll = sa_dir / (asm + ".dll")
        if not dll.is_file():
            bad = True
            lines.append(f"- ❌ `{asm}.dll` 不存在，但其源码最新修改于 "
                         f"{fmt_ts(src_max)}（`{src_file}`）")
            continue
        try:
            dll_m = dll.stat().st_mtime
        except OSError:
            continue  # 竞态下被删，本轮跳过（Unity 重建后下轮会重新校验）
        delta = dll_m - src_max
        if dll_m + STALE_TOLERANCE_SEC < src_max:
            bad = True
            lines.append(f"- ❌ `{asm}.dll` 陈旧 {delta:+.1f}s"
                         f"（dll={fmt_ts(dll_m)} 源码最新={fmt_ts(src_max)}，"
                         f"由 `{src_file}` 导致）")
        else:
            ok_count += 1
            lines.append(f"- ✅ `{asm}.dll` dll={fmt_ts(dll_m)} "
                         f"(源码最新 {fmt_ts(src_max)}, {delta:+.1f}s)")
    if not lines:
        lines.append("- 未发现工程自有程序集对应的源码（工程为空？）")
        bad = True
    lines.insert(0, f"- 新鲜工程程序集 {ok_count} 个")
    return (not bad), lines


def parse_log(log_path: Path) -> tuple[int, list[str], bool]:
    """统计唯一 error CS 行；重复错误只算一次，避免虚高。"""
    try:
        text = log_path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return -1, ["- 无法读取 Unity 日志"], True
    uniq = sorted({ln.strip() for ln in text.splitlines() if ERROR_CS_RE.search(ln)})
    has_marker = "Scripts have compiler errors" in text
    return len(uniq), [f"- {u}" for u in uniq[:20]], has_marker


def parse_test_xml(xml_path: Path) -> dict | None:
    try:
        root = ET.parse(xml_path).getroot()
    except (OSError, ET.ParseError):
        return None  # 测试被要求跑但 xml 缺失/损坏，本身就是失败证据，交由上层判定
    r = root if root.tag == "test-run" else root.find(".//test-run")
    if r is None:
        return None
    return {k: int(r.get(k, 0)) for k in
            ("testcasecount", "passed", "failed", "skipped", "inconclusive")}


def log_error_section(err_lines: list[str], primary_pass: bool) -> list[str]:
    """编译日志错误行（诊断）小节：主证据全过时标注自愈噪音。"""
    out = ["## 编译日志错误行（诊断）"]
    if not err_lines:
        out.append("- 无 error CS 行")
        return out
    out += err_lines
    if primary_pass:
        out.append("⚠️ 自愈噪音（程序集已重建、测试全绿）：不影响结论与退出码")
    else:
        out.append("（主证据已失败，上述错误行作为失败证据一并列入）")
    return out

