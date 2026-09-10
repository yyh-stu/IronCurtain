#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""编译门禁：Unity 编译后校验工程程序集与源码一致性，防"测试全绿但实际是旧 DLL"的伪绿。

判定以物理证据为主（M1-S01-fix3）：程序集新鲜度 + 测试结果两项全过 ⇒ 通过；
日志 error CS 行降为诊断项——Unity 增量编译对已删除文件报 CS2001 后会
同一次运行内自愈重编译（程序集重建、测试全绿），属噪音不判失败。
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import gate_checks as gc

DEFAULT_PROJECT = "D:/DevTools/IronCurtain"
DEFAULT_UNITY = "D:/Unity/Editor/Unity.exe"
DEFAULT_TIMEOUT = 900
DEFAULT_MIN_CASES = 294
HANDLE_WAIT_SEC = 2.0
CLEANUP_RETRIES = 5
CLEANUP_RETRY_INTERVAL = 1.0


def kill_unity_tree(unity: Path) -> None:
    """Windows 下强杀 Unity 进程树；其他平台用 pkill 兜底。失败静默（尽力而为）。"""
    try:
        if os.name == "nt":
            subprocess.run(["taskkill", "/F", "/T", "/IM", unity.name],
                           capture_output=True)
        else:
            subprocess.run(["pkill", "-9", "-f", unity.name], capture_output=True)
    except OSError:
        pass


def run_unity(unity: Path, project: Path, log_path: Path, xml_path: Path | None,
              timeout: int) -> tuple[int, bool]:
    cmd = [str(unity), "-batchmode", "-projectPath", str(project),
           "-logFile", str(log_path)]
    if xml_path is not None:
        cmd += ["-runTests", "-testPlatform", "EditMode", "-testResults", str(xml_path)]
    try:
        # 文本模式读日志：Unity 日志可能含 GBK 中文，errors='replace' 防解码崩溃
        proc = subprocess.run(cmd, timeout=timeout, capture_output=True, text=True,
                              errors="replace")
        return proc.returncode, False
    except subprocess.TimeoutExpired:
        # 挂死必须显式杀掉，否则 CI 卡到平台层超时，错误信息更难定位
        kill_unity_tree(unity)
        return -1, True


def release_wait(log_path: Path) -> None:
    """等待 Unity 释放日志句柄（Windows WinError 32 的主要来源）。"""
    deadline = time.time() + HANDLE_WAIT_SEC
    while time.time() < deadline:
        try:
            with open(log_path, "ab"):
                pass
            return
        except OSError:
            time.sleep(0.2)


def cleanup_temp_dir(td: str) -> None:
    """容错清理：多次重试，任何失败只告警不影响调用方语义。"""
    for i in range(CLEANUP_RETRIES):
        try:
            shutil.rmtree(td, ignore_errors=False,
                          onerror=lambda *a, **k: None)
            return
        except OSError:
            if i < CLEANUP_RETRIES - 1:
                time.sleep(CLEANUP_RETRY_INTERVAL)
    shutil.rmtree(td, ignore_errors=True)  # 最终兜底：尽力删，删不掉就算了
    print(f"[warn] 临时目录未能完全清理: {td}", file=sys.stderr)


def main() -> int:
    ap = argparse.ArgumentParser(description="IronCurtain 编译门禁（防伪绿）")
    ap.add_argument("--project", default=DEFAULT_PROJECT)
    ap.add_argument("--unity", default=DEFAULT_UNITY)
    ap.add_argument("--timeout", type=int, default=DEFAULT_TIMEOUT)
    ap.add_argument("--min-cases", type=int, default=DEFAULT_MIN_CASES)
    ap.add_argument("--test", action="store_true", help="编译后追加 EditMode 测试")
    ap.add_argument("--force-rebuild", action="store_true",
                    help="跑前清空 Library/ScriptAssemblies（只删文件不删目录），彻底排除增量缓存干扰")
    ap.add_argument("-o", "--out", default=None, help="结果 markdown 输出路径")
    args = ap.parse_args()

    project = Path(args.project)
    md: list[str] = ["# 编译门禁报告"]
    failures: list[str] = []
    advice: list[str] = []

    if not project.is_dir():
        print(f"工程不存在: {project}")
        return 1

    if args.force_rebuild:
        sa_dir = project / "Library" / "ScriptAssemblies"
        md.append(f"- force-rebuild: 清空 {sa_dir}")
        if sa_dir.is_dir():
            for f in sa_dir.iterdir():
                if f.is_file():
                    try:
                        f.unlink()
                    except OSError as e:
                        failures.append(f"force-rebuild 清理失败: {f} ({e})")

    # 先在源码扫描前完成清理动作，清理本身不会引入新的源码 mtime
    asmdefs = gc.scan_asmdefs(project)
    src_map = gc.attribute_cs_files(project, asmdefs)
    cs_max = max((v[0] for v in src_map.values()), default=0.0)
    cs_file = max(src_map.items(), key=lambda kv: kv[1][0],
                  default=("", (0.0, "")))[1][1]
    md.append(f"- t0: {gc.fmt_ts(time.time())}")
    md.append(f"- 最新 .cs: `{cs_file}` mtime={gc.fmt_ts(cs_max)}")
    md.append(f"- 模式: {'编译+测试' if args.test else '仅编译'}"
              + ("（force-rebuild）" if args.force_rebuild else ""))

    # 临时目录必须在工程之外：Unity 启动时会清空工程 Temp/，写进去的 results.xml 会被删掉
    # （2026-09-10 实测：xml 恒缺失、测试白跑）。文件锁由 kill_unity_tree + release_wait 处理。
    td = tempfile.mkdtemp(prefix="ic-gate-")

    test_missing = False
    if True:
        log_path = Path(td) / "unity.log"
        xml_path = Path(td) / "results.xml" if args.test else None
        rc, timed_out = run_unity(Path(args.unity), project, log_path, xml_path,
                                  args.timeout)
        md.append(f"- Unity 退出码: {rc}" + ("（超时被杀）" if timed_out else ""))
        if timed_out:
            failures.append("Unity 进程超时被强制终止")
            advice.append("检查 Unity 是否弹窗/锁库（删除 Library/locks 后重试），或加大 --timeout")
        release_wait(log_path)  # 等 Unity 松开日志句柄，缓解 WinError 32
        err_count, err_lines, has_marker = gc.parse_log(log_path)
        md.append(f"- error CS 唯一行数: {err_count}（Scripts have compiler errors: {has_marker}）")
        # --- 主证据 1：测试结果 ---
        stats = None
        if xml_path is not None:
            stats = gc.parse_test_xml(xml_path)
            if stats is None:
                test_missing = True
                failures.append("测试结果 xml 缺失或损坏（编译失败时 Unity 不产出）")
            else:
                md.append(f"- 用例: total={stats['testcasecount']} passed={stats['passed']} "
                          f"failed={stats['failed']} skipped={stats['skipped']} "
                          f"inconclusive={stats['inconclusive']}")
                if stats["failed"] > 0:
                    failures.append(f"测试失败 {stats['failed']} 个")
                    advice.append("查看测试 xml 中 failed 用例的日志")
                if stats["testcasecount"] < args.min_cases:
                    failures.append(f"用例数 {stats['testcasecount']} < 阈值 {args.min_cases}")
                    advice.append("用例丢失通常是伪绿前兆：先确认 dll 校验，再查测试发现逻辑")
        # --- 主证据 2：程序集新鲜度 ---
        fresh, dll_lines = gc.check_stale_assemblies(project, src_map)
        md += ["", "## DLL 新鲜度校验（防伪绿核心，仅工程程序集）"] + dll_lines
        if not fresh:
            failures.append("陈旧程序集：存在 dll 早于其归属源码最新 mtime，本次结果不可信（伪绿）")
            advice.append("用 --force-rebuild 清空 Library/ScriptAssemblies 后重跑，或重启 Unity 强制重编译")
        # --- 主证据判定（失败条件任一即败）---
        primary_pass = (not failures)
        # --- 诊断项：日志 error CS 行 ---
        md += [""] + gc.log_error_section(err_lines, primary_pass)
        if not primary_pass and err_count != 0:
            failures.append(f"存在编译错误（error CS={err_count}，已列入失败证据）")
            advice.append("按报告错误行清单修复 .cs 后重跑")

    md += ["", "## 结论", "✅ 通过：程序集新鲜度与测试结果均满足" if not failures else
           "❌ 失败：\n" + "\n".join(f"- {f}" for f in failures)]
    if advice:
        md += ["", "## 下一步建议"] + [f"- {a}" for a in advice]

    report = "\n".join(md)
    exit_code = 0 if not failures else 1
    # 报告先于清理落盘：清理出任何问题都不影响报告与退出码
    print(report)
    if args.out:
        try:
            Path(args.out).write_text(report + "\n", encoding="utf-8")
            print(f"\n[report] {args.out}")
        except OSError as e:
            print(f"[error] 报告写入失败: {e}", file=sys.stderr)
            exit_code = 1
    cleanup_temp_dir(td)
    return exit_code


if __name__ == "__main__":
    sys.exit(main())
