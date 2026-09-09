# 任务证据台账

每个模型任务完成后，用 `D:\DevTools\glm-pipeline\record_task.py` 把最小证据写入 `task-ledger.jsonl`。JSONL 是机器账本，本文件只说明格式；不要把完整提示词、源码或 API key 复制进账本。

示例：

```powershell
python D:\DevTools\glm-pipeline\record_task.py `
  --task-id M6.5-01 `
  --model gpt-5.6-terra `
  --manifest D:\DevTools\glm-pipeline\staging\M6.5-01\manifest.json `
  --review D:\DevTools\glm-pipeline\reviews\M6.5-01.md `
  --stop-reason end_turn `
  --gate api=pass --gate static=pass --gate review=pass `
  --test editmode=238/238
```

每个子模块完成、返工或升级时立即记录，不等大模块结束。每个安全小块完成后必须在同一收口回合同步更新 `../PROJECT_STATE.md`，不能只写 `PROJECT_BRIEF.md`、验收索引或本台账。未到审核节点的子模块记为“机器门禁通过待凑批”；每 5 个子模块形成一个审核批次，最后不足 5 个的尾批在大模块完成时记录。

每条记录包含时间、任务 ID、子模块编号、审核批次、审核轮次、原源码提交模型、实际角色/模型、当前 Git HEAD、任务卡/输入/候选产物/审核报告/修复后产物路径与 SHA256、停止原因、费用、各门禁/测试结果、工程/实机/制作人三态验收、可恢复检查点、阻断项和制作人待办。同批连续三轮拦截后，还要记录升级模型、继续修复/直接放行裁决、理由、证据、接受风险和裁决 SHA256。台账只记录证据，不自动宣布任务完成。
