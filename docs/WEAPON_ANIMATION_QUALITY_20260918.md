# 铁幕前线 · 武器挂载与动作质量提升交接（2026-09-19 修订）

> 目的：给执行编排和审核员一份可复现的武器/动作质量方案。本文只做源码、资源、控制器、工具和证据审查，**不代执行端改代码或资源，不把静态截图当动态放行**。

## 0. 当前状态的边界

项目状态入口 `D:/DevTools/IronCurtain/docs/PROJECT_STATE.md` 已记录：门禁 424/424、实机 38/0、武器资源验证 7/7、动画控制器三套、效果台账 60/60。这个结论可以作为执行端交付声明，但不等同于本交接中的武器握持和动作质量已经通过。

本次交叉检查发现三份记录的口径尚未完全合并：

| 记录 | 目前可确认的内容 | 不能由它单独证明的内容 |
|---|---|---|
| PROJECT_STATE.md | 同时含早期 57/60/待做条目和后续 100% 终态段；后者给出构建与证据文件名 | 六英雄动态握持、转向、攻击接触帧；需按时间顺序引用 |
| M0_effect-ledger.md 尾部 | 仍保留 57/60、T03/U03/U04 待做的旧汇总 | 不能覆盖最新 100% 声明，需更新版本/时间 |
| `作品集/实机验证证据/acc_all_2026-09-19_100PCT.txt` | 真实路由、目标模式、临时阵地部署、面板和对局判据 | 文本中未找到 T03 绿/红预览、U03 金色闪光、U04 EP 浮字的明确判据；也没有武器 7/7 或动作逐帧段落 |

因此本交接的放行口径是：**VFX/功能终态可按最新 STATE 追踪；武器/动作质量仍需下面的专门证据补齐，不能用“60/60”代替。**

## 1. 工程、源码和证据边界

| 项目 | 路径 / 说明 |
|---|---|
| 工程 | `D:/DevTools/IronCurtain` |
| 挂载接线 | `Assets/Scripts/View/EntityView.Hero.cs` |
| 通用视图与动画状态 | `Assets/Scripts/View/EntityView.cs` |
| 菜单展示 | `Assets/Scripts/View/MenuStage.cs` |
| 英雄控制器生成 | `Assets/Editor/GenerateHeroAnimators.cs` |
| 骑乘控制器生成 | `Assets/Editor/GenerateRiderAnimator.cs` |
| 武器资源验证 | `Assets/Editor/WeaponValidate.cs` |
| 握持扫描 | `Assets/Editor/WeaponTuneBatch.cs` |
| 英雄特写 | `Assets/Editor/HeroCloseupBatch.cs` |
| 动作扫描 | `Assets/Editor/ClipAuditBatch.cs` |
| 根运动诊断 | `Assets/Editor/CheckClipRootMotion.cs` |
| 武器资源 | `Assets/Resources/Models/Weapons/*.fbx` |
| 英雄/动作资源 | `Assets/Resources/Models/Heroes/*.fbx`、`Heroes/Anims/*.fbx` |
| 旧动态诊断 | `D:/DevTools/shots/play_check/rootmotion_report.json` |
| 最新实机文本 | `C:/Users/HP/Desktop/作品集/实机验证证据/acc_all_2026-09-19_100PCT.txt` |

执行期间不要删除或覆盖历史证据；批处理如会清空 `D:/DevTools/shots/clipaudit`，必须改用带时间戳的新目录并保留命令、源码哈希、资源哈希和 Unity 日志。

## 2. 已确认的源码事实与问题分级

### 2.1 武器挂载

| ID | 级别 | 静态事实 | 结论/风险 |
|---|---|---|---|
| F-W01 | P0 | `AccumulateLocal` 在武器根使用 `Matrix4x4.identity`，递归子节点的局部 TRS；随后 `localRotation * la` 把武器轴送入 socket。 | **这是正确的“武器自身空间”计算，不是要再叠一次根旋转的缺陷。** 真正不稳定的是 `GripAnchorLocal` 只把最长轴的一条坐标写入，另外两维固定为 0，无法表达真实握柄中心/偏心握点。 |
| F-W02 | P0 | socket 作为手骨子物体创建，未配置掌心偏移；`Len` 用最终朝向前的世界 Renderer AABB 最大边，随后用 `Vector3.one*s` 覆盖原 prefab scale。 | 手腕原点不等于掌心，且尺寸测量受根朝向、父级缩放和原始 scale 影响；换模型或姿态后会出现“长度对了、握点不对”。需显式 Grip/Tip/Face 数据和 scale 回归。 |
| F-W03 | P1 | 目前 `Rot/Grip/RollDeg` 是 `HeroWeapons` 内的静态 C# 配置；`WeaponAimOverride/WeaponGripOverride` 是全局标定覆盖。 | 能复现当前七件装备，但不是资产级可审计标定；同一场景多把装备或换资源时容易串值。应保存 GUID→标定记录，不要继续堆全局覆盖。 |
| F-W04 | P1 | `HandBone` 先调用 `Animator.GetBoneTransform`，未先检查 Avatar 有效性/人形映射；资源缺失现在会计数并报错，但重复挂接和手骨映射仍缺专门门禁。 | 无效 Avatar、手骨缺失、同一模型重复初始化时，必须明确失败而非只靠日志。资源 7/7 只证明 Resources.Load 和 Renderer，不证明握点正确。 |
| F-W05 | P1 | 六套英雄映射中没有手指骨；挂载只跟随 LeftHand/RightHand，未提供掌心、拇指/食指或第二握点。 | 视觉上可能“武器穿过手/手张开”；不能用一张站姿图证明抓握。若原始权重没有手指，只做局部手姿态/握点补偿，不先引入大型 IK 框架。 |
| F-W06 | P1 | `BoardView.SpawnBeamIfRanged` 的起点是英雄根部上方固定高度（英雄约 1.9、士兵约 1.0），不是弓弦出箭点/杖尖。 | 这是 VFX 与武器挂载的接口问题：弹道应消费一个稳定的 muzzle/nock/tip producer；只能指定单一 owner，避免 VFX 和武器两边各自补一套。 |

### 2.2 动作、朝向和时钟

| ID | 级别 | 静态事实 | 结论/风险 |
|---|---|---|---|
| F-A01 | P1 | 普通地面近战 `PanShi/ZhenYue/DuanFeng` 共用 `HeroAnimator/HeroAttack`；`ZhuFeng` 会切到真实存在的 `RiderAnimator`，并非四近战共用。 | “动作抽象”主要来自三名地面近战共用一套挥砸；不能把逐风误报为普通近战。先做镇岳重击、磐石盾刀、断峰/逐风突刺三组金样，再决定是否新增资产。 |
| F-A02 | P0 | 生成器已把攻击 clip 设为 `loop=false`，并在攻击状态添加退出时自转换；`EntityView` 仍以 `state==Attacking` 维持攻击态，同时以冷却重置触发 `attack`。 | 非循环设置本身是合理方向，但“攻击态自转换 + Sim 冷却 + Trigger”是否重复播、漏播或与接触帧错位，必须实机记录 normalizedTime、Trigger、Sim 事件 ID；不能只看 controller 文本。 |
| F-A03 | P0 | `EntityView.Bind` 只按阵营设置初始 ±90°；普通 Tick 更新位置、状态、血量，没有按目标/速度更新模型朝向。行军专用组件是例外。 | 普通英雄攻击移动目标或换侧后可能仍面向固定阵营方向，是动作“抽象/不对劲”的高概率原因。需定义单一 facing owner，避免与骑乘和行军组件互相写旋转。 |
| F-A04 | P1 | 根运动历史报告显示 HeroAttack/Walk 有显著 RootT；`CompensateAnimDrift` 将 Hips XZ 直接从 body localPosition 扣除。 | 需要按 clip 决定“Sim 位移+原地动画”或受控 Root Motion；当前补偿把 Hips 父空间位移直接用于 body 父空间，非单位变换/旋转时需量测，不能宣称所有 clip 都已正确。 |
| F-A05 | P1 | 骑手能加载 `RiderAnimator`，马体本身没有确认过 Animator/controller；运行时主要是 mount bob，未证明真实马步/冲锋 gait。 | 逐风验收必须分开看马、骑手、长枪；“有 RiderAnimator”不等于马脚步和枪尖已同步。 |
| F-A06 | P1 | `MenuStage` 和战斗侧都有归一化，但目标高度、根层级、骑手位置和取包围盒的时机不同；菜单/战斗的 hips drift 补偿还混用不同父空间。 | 菜单准、实机偏是可复现风险。应统一标定来源，分别对菜单与战斗做一次相同姿态/相同资源哈希回归。 |
| F-A07 | P1 | GameBootstrap 的暂停会停止 Sim，但没有看到统一的 Animator/playable 时钟绑定；UI/特效有自己的 deltaTime。 | 暂停、倍速、重开时可能出现仿真停而骨骼继续走，或特效与动作不同步。先定义 combat clock，再决定 Animator.updateMode/手动采样；不要让 VFX 单独修时钟。 |
| F-A08 | P1 | `ClipAuditBatch` 是 MenuStage + `AnimationMode.SampleAnimationClip` 的静态采样；旧逻辑对弓/杖采样过通用动作，固定 12 个时间点且不覆盖真实 Animator/补偿/Sim。 | 它适合发现资源级异常，不是运行时验收；必须按实际控制器和英雄加载，覆盖起止帧、过渡、攻击事件和 8 方向。 |

## 3. 目标架构与不可越权规则

1. Sim 仍是攻击、移动、伤害、射程和事件 ID 的唯一权威；动画只表现，不反向改数值。
2. 武器数据以资源 GUID 为主键，至少保存 `GripPoint`、`Forward/Up`、`FaceNormal`、`ScalePolicy`、左右/双手类型和 muzzle/nock/tip（没有真实标记时明确 fallback）。
3. socket 负责掌心/第一握点；双手武器只允许“武器跟主手、辅手跟第二握点”的单向约束，禁止双手互相求解形成循环。
4. facing 只由一个运行时 owner 写入；攻击事件、移动方向、骑乘控制器和行军组件通过接口提供意图，不在 LateUpdate 互相抢旋转。
5. 攻击采用“Sim 事件 ID → 一击 token/trigger → 接触窗口 → 收招”的明确方案。若当前 Sim 是瞬时命中，没有预摇时间就记录为“即时命中+表现延迟”，不要伪造伤害时点。
6. 根运动按 clip 单独决策。不得因为一份 RootT 报告就对所有动作统一 Bake/补偿；必须以清洁重导入和运行时世界位移回归为准。
7. VFX 只消费武器侧的稳定 muzzle/nock/tip 快照；武器侧不复制弹道/命中特效逻辑。
8. 任何“100%”必须同时给出工程门禁、实机判据、证据路径和源码/资源哈希；台账、STATE 和证据三者不一致时标记为“待同步”，不静默覆盖旧汇总。

## 4. 实施分批（15 个小模块）

执行编排每次领一批，独立审核拦截后返原提交者修复/反证；连续三轮仍拦截才升级。普通批次每 3 个模块，公共契约和制作人金样单独审。

### W-A：资源与握点基线

| ID | 实施 | 通过标准 |
|---|---|---|
| W01 | 建立六英雄×七件武器/坐骑的 GUID、Renderer、材质、原始 scale/rotation、手骨和预期挂接清单 | 资源数、配置数、引用数一致；缺件/无 Renderer/无有效 Avatar 均阻断；报告带源码/资源哈希 |
| W02 | 把 `Grip/Forward/Face/Tip` 从隐含最长轴提升为每资产可审计数据；保留现有 Roll 作为迁移值 | 每件装备有选定值和局部坐标；盾、弓、长柄不能只凭最长轴；全局 override 归零或有作用域 |
| W03 | 扩展校准工具到六英雄、七件装备和 `RollDeg/Face/Tip`；输出独立新目录 | 候选、选定参数、前后近景、资源哈希齐全；不覆盖旧证据，不清空共享目录 |

### W-B：坐标、掌心与接口

| ID | 实施 | 通过标准 |
|---|---|---|
| W04 | 保留“根 identity 进入 FBX 自身空间”的正确算法；修正真正的握点/掌心偏移和 prefab scale 继承；加入非单位 root/父缩放回归 | 武器根旋转只应用一次；换根旋转、父 scale、朝向后 Grip 世界误差仍达标，不出现双变换 |
| W05 | 对刀、锤、盾、弓、矛、枪、杖定义握点/面向；若无手指骨，采用最小局部手姿态或明确“只保证掌心” | 掌心到 Grip ≤ 目标英雄高度 1%（默认约 2 cm）；面向角误差 ≤5°；两手接触窗口误差 ≤2% |
| W06 | 由武器侧提供只读 `Muzzle/Nock/Tip` 快照，VFX/BoardView 改为消费该接口 | 弓箭从弓弦/出箭点、杖从杖尖、近战从武器接触区取点；无重复 owner、无根部固定高度残留 |

### W-C：动作时序与朝向

| ID | 实施 | 通过标准 |
|---|---|---|
| A01 | 固化一击 token/Trigger 与 Sim attack event ID；明确即时命中或预摇/接触窗口策略 | 每个 Sim 事件最多一个表现 token；漏播/重播可由日志计数；不改伤害和冷却 |
| A02 | 对 idle/walk/attack 逐 controller 导出 loop、exit、transition、normalizedTime；修正攻击态自转换与冷却触发的重复播风险 | 一击有进入—接触—收招；连续攻击只在事件 ID 到达时重启；过渡无卡死/无限自播 |
| A03 | 建立 facing owner，覆盖静止、行走、攻击、目标位移、换边和失去目标 | 8 方向与目标移动时模型朝向误差 ≤5°；行军/骑乘不会被通用 Tick 抢写 |

### W-D：金样、根运动与骑乘

| ID | 实施 | 通过标准 |
|---|---|---|
| A04 | 先做镇岳重击、磐石盾刀、断峰/逐风突刺三组金样；弓/杖保留各自攻击 clip | 制作人确认节奏、形体和武器接触后再扩全量；三名地面近战不宣称各有新资产，复用要记录 |
| A05 | 按 clip 量测 RootT/Hips/脚底并选择原地动画或受控 Root Motion；转换父空间后再补偿 | 行走无漂移/明显滑步；攻击前冲和 Sim 位置一致；记录 clean reimport 前后差异 |
| A06 | 逐风专项：骑手鞍位、腿、RiderIdle/Charge/Thrust、马体 bob、长枪 tip | 静止/行军/冲锋/前刺均无穿马、脱鞍、枪尖跳变；马是否有真实 gait 要在报告中明确 |

### W-E：时钟、回归与交付

| ID | 实施 | 通过标准 |
|---|---|---|
| A07 | 把 Animator/playable、Sim、VFX 纳入一个可暂停/倍速审计；记录 1x/2x/4x/暂停骨骼矩阵和 Sim tick | 暂停两秒骨骼 phase 不变；倍速比例可解释；VFX 不独自继续跑 |
| A08 | 改造 ClipAudit：实际英雄控制器、真实状态、完整首尾帧、过渡、8 方向、隐藏/重现/重开 | 静态采样与运行时片段来源一致；每个结果有状态、clip、normalizedTime、事件 ID 和哈希 |
| A09 | 六英雄全量回归与交付：待机/行走/攻击/转向/骑乘/换边/死亡/重开；同步 STATE、BRIEF、台账和桌面副本 | P0 全关闭；未测项、仅契约批准项、静态通过项分开；三份状态记录同一版本/证据索引 |

顺序：W-A → W-B → W-C → 制作人确认 W-D 金样 → W-E。若 W04 的回归证明根旋转口径没问题，不要再改几十组 Rot；只修 Grip/掌心/资产数据。

## 5. 专项验收清单（15 项）

- [ ] 台账、STATE、证据文件明确同一版本；旧 57/60 汇总已标过时或更新。
- [ ] `acc_all_2026-09-19_100PCT.txt` 补有 T03 绿/红预览、U03 金闪、U04 EP 浮字的可检索判据和截图索引。
- [ ] 六英雄×武器资源 GUID/Renderer/材质/Avatar/绑定结果齐全，缺件不静默。
- [ ] 根 identity 口径通过非单位 root、父 scale、重导入回归；没有重复根旋转。
- [ ] 七件装备都有显式 Grip/Forward/Face/Scale 记录；Grip 不再只写最长轴一维。
- [ ] 掌心—握点距离 ≤1% 英雄高度，面向角 ≤5°；盾面、弓弦、长柄方向正确。
- [ ] 待机、行走、攻击关键帧无脱手、穿体、盾横躺、弓弦穿身；手指缺失已明确补偿边界。
- [ ] A01/A02 日志证明每个 Sim 事件最多一个动作 token，攻击不会由状态自转换无限重播。
- [ ] 普通英雄随目标/速度转向；8 方向、换边、失去目标无固定朝向残留。
- [ ] HeroAttack 共用范围如实记录；镇岳、磐石、断峰/逐风金样已获制作人确认，不把逐风误报为普通近战。
- [ ] Root Motion/Compensate 逐 clip 有数值；行走不漂、攻击前冲不改 Sim 位置。
- [ ] 弓/杖/近战 VFX 起点来自 muzzle/nock/tip 单一接口，不再用固定根部高度。
- [ ] 逐风静止、行军、冲锋、前刺中骑手、马、长枪无穿模/脱鞍/枪尖跳变。
- [ ] 暂停、1x/2x/4x、重开时 Sim、Animator、VFX 时钟关系有日志且可复现。
- [ ] 实际 Animator/EntityView 动态证据覆盖首尾帧、过渡、事件 ID、隐藏/重现和三次重开；源码/资源哈希绑定。

放行规则：第 1–9、11–12 任一未通过，武器/动作 P0 不放行；第 10、13–15 可先标 P1，但必须写明范围、负责人和复验时间。视觉模型只给线索，最终以运行时动态片段、数字误差和制作人金样意见为准。

## 6. 已知限制、成本与不越权事项

1. 本次没有启动 Unity，也没有修改产品代码、资源或状态文件；“7/7 资源验证”和“60/60 效果台账”未被本交接重复执行。
2. `WeaponValidate` 的 PASS 只覆盖资源可加载/Renderer/GUID，不能推出手掌握持、动态不穿模或动作像样。
3. `ClipAuditBatch` 的静态采样、旧特写和根运动 JSON 不能替代实际 Animator/Sim/VFX 联动；旧图不绑定当前源码/资源哈希时只能做历史参考。
4. 不建议现在调用外部 3D/动作生成服务。先解决握点、朝向、状态/时钟和证据链；新资产会增加变量和费用。
5. 不强制引入 IK 或大型动画包。只有在已确认存在可用手指/手掌 rig、且显式握点仍不足时，才另立最小 IK 评估。
6. 总编排负责范围、指标、依赖和放行；执行编排拆批与集成；提交者修复拦截；审核员不代写；制作人裁决风格、复用和金样。

## 7. 关键文件与转交首批

| 类别 | 路径 |
|---|---|
| 本文正本 | `D:/DevTools/IronCurtain/docs/WEAPON_ANIMATION_QUALITY_20260918.md` |
| 桌面交接副本 | `C:/Users/HP/Desktop/铁幕前线_武器挂载与动作质量交接_GPT.md` |
| 挂载/握点 | `D:/DevTools/IronCurtain/Assets/Scripts/View/EntityView.Hero.cs` |
| 状态/朝向/动画触发 | `D:/DevTools/IronCurtain/Assets/Scripts/View/EntityView.cs` |
| 菜单归一化 | `D:/DevTools/IronCurtain/Assets/Scripts/View/MenuStage.cs` |
| 控制器 | `D:/DevTools/IronCurtain/Assets/Editor/GenerateHeroAnimators.cs`、`GenerateRiderAnimator.cs` |
| 资源门禁 | `D:/DevTools/IronCurtain/Assets/Editor/WeaponValidate.cs` |
| 静态工具 | `D:/DevTools/IronCurtain/Assets/Editor/WeaponTuneBatch.cs`、`HeroCloseupBatch.cs`、`ClipAuditBatch.cs`、`CheckClipRootMotion.cs` |
| 最新实机文本 | `C:/Users/HP/Desktop/作品集/实机验证证据/acc_all_2026-09-19_100PCT.txt` |
| 状态入口 | `D:/DevTools/IronCurtain/docs/PROJECT_STATE.md`、`PROJECT_BRIEF.md`、`VFX_PLAN_20260917/M0_effect-ledger.md` |

**给执行编排的首条任务：**只领取 W01–W03。先将 100% 声明与台账/证据的版本差异对齐，再生成六英雄×七件装备的 GUID、Avatar、握点、tip 和哈希报告；不要先改 Rot/Grip 数值。提交后由独立审核员检查资源清单、证据目录是否可复现；W-A 通过再处理 W04–W06。执行期间不得覆盖 VFX 执行端正在使用的公共文件或证据目录。
