# 编译门禁报告
- t0: 2026-09-21 02:35:38
- 最新 .cs: `D:\DevTools\IronCurtain\Assets\Scripts\View\IcAcceptance.cs` mtime=2026-09-21 02:35:34
- 模式: 编译+测试
- Unity 退出码: 1
- error CS 唯一行数: 2（Scripts have compiler errors: True）

## DLL 新鲜度校验（防伪绿核心，仅工程程序集）
- 新鲜工程程序集 3 个
- ✅ `Assembly-CSharp-Editor.dll` dll=2026-09-21 02:33:20 (源码最新 2026-09-21 02:33:13, +7.5s)
- ✅ `IronCurtain.Sim.dll` dll=2026-09-20 18:07:16 (源码最新 2026-09-20 18:04:29, +166.3s)
- ✅ `IronCurtain.Tests.dll` dll=2026-09-21 02:23:31 (源码最新 2026-09-21 02:19:13, +257.3s)
- ❌ `IronCurtain.View.dll` 陈旧 -265.4s（dll=2026-09-21 02:31:09 源码最新=2026-09-21 02:35:34，由 `D:\DevTools\IronCurtain\Assets\Scripts\View\IcAcceptance.cs` 导致）

## 编译日志错误行（诊断）
- Assets\Scripts\View\IcAcceptance.cs(574,30): error CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?)
- Assets\Scripts\View\IcAcceptance.cs(618,29): error CS0266: Cannot implicitly convert type 'long' to 'int'. An explicit conversion exists (are you missing a cast?)
（主证据已失败，上述错误行作为失败证据一并列入）

## 结论
❌ 失败：
- 测试结果 xml 缺失或损坏（编译失败时 Unity 不产出）
- 陈旧程序集：存在 dll 早于其归属源码最新 mtime，本次结果不可信（伪绿）
- 存在编译错误（error CS=2，已列入失败证据）

## 下一步建议
- 用 --force-rebuild 清空 Library/ScriptAssemblies 后重跑，或重启 Unity 强制重编译
- 按报告错误行清单修复 .cs 后重跑
