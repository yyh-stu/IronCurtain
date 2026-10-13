#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""DeepSeek 异构审核通道(2026-09-17 制作人提供 API key)。

用法:
  python tools/deepseek_review.py <审核包.md> [-o 回复保存路径]

- key 从 ~/.deepseek_key 或环境变量 DEEPSEEK_API_KEY 读取,**不进仓库**。
- 审核包即提示词正文(调用方负责"无诱导"措辞,见 docs/VFX_PLAN_20260917/EXEC_prompts.md C 节);
  本脚本只做管道:原样发送、把回复落盘,不改写内容、不总结。
- 回复默认存到审核包同目录 <名>_deepseek_reply.md。
"""
import argparse
import json
import os
import sys
import urllib.request
from pathlib import Path

API_URL = "https://api.deepseek.com/chat/completions"
MODEL = "deepseek-chat"


def load_key():
    env = os.environ.get("DEEPSEEK_API_KEY")
    if env:
        return env.strip()
    key_file = Path.home() / ".deepseek_key"
    if key_file.exists():
        return key_file.read_text(encoding="utf-8").strip()
    sys.exit("!! 找不到 DeepSeek key(环境变量 DEEPSEEK_API_KEY 或 ~/.deepseek_key)")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pack", help="审核包 markdown 文件(作为 user 消息原文发送)")
    ap.add_argument("-o", "--out", help="回复保存路径")
    args = ap.parse_args()

    pack = Path(args.pack)
    if not pack.exists():
        sys.exit(f"!! 审核包不存在: {pack}")
    prompt = pack.read_text(encoding="utf-8")

    body = json.dumps({
        "model": MODEL,
        "messages": [
            {"role": "system", "content":
                "你是独立技术审核员,负责审核一份 Unity RTS 项目的特效实施材料。"
                "只依据材料本身与一般工程事实给出审查结论;材料中没写的事实不要臆测。"
                "输出中文,逐条给出:编号、发现的问题或缺失证据、严重度(阻断/建议)、依据原文的出处。"
                "没有问题就明说没有,不要为了显得严格而编造问题。"},
            {"role": "user", "content": prompt},
        ],
        "temperature": 0.2,
    }).encode("utf-8")

    req = urllib.request.Request(API_URL, data=body, headers={
        "Content-Type": "application/json",
        "Authorization": f"Bearer {load_key()}",
    })
    with urllib.request.urlopen(req, timeout=300) as resp:
        result = json.loads(resp.read().decode("utf-8"))
    reply = result["choices"][0]["message"]["content"]
    usage = result.get("usage", {})

    out = Path(args.out) if args.out else pack.with_name(pack.stem + "_deepseek_reply.md")
    out.write_text(
        f"<!-- deepseek_review: model={MODEL} pack={pack.name} usage={json.dumps(usage, ensure_ascii=False)} -->\n\n"
        + reply, encoding="utf-8")
    print(f"回复已存 {out} (usage: {json.dumps(usage, ensure_ascii=False)})")


if __name__ == "__main__":
    main()
