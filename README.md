# 铁幕：前线 · IronCurtain

一款**轻量化占地 RTS** 的可玩 Demo：10 分钟 1v1、三路对称占地、无农民采集、无局外数值付费，胜负全靠临场决策。

**Unity 6000.0.30f1 · URP · 纯 C# 确定性模拟核心 · 单人独立开发（个人 + 自建 AI 工作流）**

---

## 玩法概要

- **三路对称占地**：十座建筑争夺，胜负由占领与摧毁推动，不做采集运营与局外养成
- **魔法屏障**：立于三路末端（等同 MOBA 高地），五段各 200 血，破哪一段就等于选择从哪一路上高地
- **双路径攻城**：近战入城交换占领 / 远程摧毁重建
- **驻军与交换力**：除大本营外所有建筑可驻军且不限量，把「占领」从计数改造成兵力交换博弈；名额 = 主将数量、上限 8、士兵不占名额
- **六英雄闭环克制链**：统一伤害公式（攻击 × 护甲 × 克制 × 修正），任何英雄都有天敌
- **海克斯卡牌**：单张生效 + 抽取窗口，用一次性选择制造局内策略分叉

## 技术亮点

### 1. 确定性模拟核心

定帧、逻辑与表现分离、**可无头回放**。确定性让自动化测试能直接断言「配置值经公式后的运行结果」——改一行数值立刻暴露冲突，这是敢让模型大规模产码的前提。

### 2. 验证体系

| 层次 | 内容 |
|---|---|
| 自动化 | **68 个测试文件 / 432 个测试方法**，全绿才允许提交 |
| 实机 | `Assets/Scripts/View/IcAcceptance.cs`（2831 行）在**真实播放器内**驱动：按钮走真实回调、时间线走真实 `Sim.Step`、截图走 `ScreenCapture`，判定全部落在**可比较的数字**上（面板矩形 / 组件计数 / 相机机位 / Sim 状态） |
| 人工 | 三态分离：工程验收 / 实机验收 / 制作人验收，未适用写「不适用」，未执行写「待验」 |

### 3. AI + 游戏开发管线

- **多智能体工作流**：七类角色分工，五道自动化检查 + 一道独立审查（API 符号 → 静态检查 → 重写检测 → 干净编译 → 测试全绿 → 批次审核），写码与审查分派给不同厂商模型做独立评审
- **MCP for Unity**：把编辑器与测试能力封装成可被编排调用的工具链，模型可远程驱动编译、跑测试、进 Play、截图与逐帧采样
- **37 个 Editor 扩展脚本**：探针（`IcProbe` / `UiProbe` / `ProductionProbe` / `WallPerfProbe`）、批处理抓帧（`CaptureBatch` / `PlayCaptureBatch` / `HeroCloseupBatch`）、资产生成（`GenerateHeroMats` / `GenerateBuildingMats` / `GenerateUiAssets`）、模型审计（`ModelAuditBatch`）

## 项目结构

```
Assets/
  Scripts/Sim/     42 个 .cs   确定性模拟核心（无 UnityEngine 依赖的逻辑层）
  Scripts/View/    71 个 .cs   表现层（UI Toolkit / 相机 / 特效 / 音频）
  Editor/          37 个 .cs   编辑器工具链与构建入口
  Tests/           68 个 .cs   EditMode 测试
  Scenes/          Menu / BP / Main
docs/                         GDD、子系统规格、事故复盘与决策档案
```

## 如何运行

1. 用 **Unity 6000.0.30f1** 打开本仓库
2. 打开 `Assets/Scenes/Menu.unity`，进入 Play 模式
3. 出独立播放器：菜单 `IronCurtain/Build Player`
4. 出 WebGL 版本：`Unity.exe -quit -batchmode -projectPath <路径> -executeMethod BuildWebGL.Build -logFile build_webgl.log`

## 演示与文档

- 实机演示录屏：<https://www.bilibili.com/video/BV1VXe768E5V/>
- 作品集速览：<https://docs.qq.com/doc/DWFZJWEtqTU5VaG1W>
- 完整 GDD：<https://docs.qq.com/doc/DWGhLRHhMTHZSQ1RO>

## 开发方式

**独立开发**：从玩法原型、系统设计到可玩构建由我一人闭环完成。策划、程序、美术、测试的职能由自建的多智能体工作流承担；我负责产品方向与优先级、审美与手感终审、实机试玩与缺陷定位。

项目期间累计留下 100+ 次拍板与验收记录，并沉淀出 8 份设计决策档案（原来 → 改后 → 为什么 → 沉淀）。
