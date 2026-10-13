# M0.2 表现契约表(presentation-contract)

版本:M0 初稿|2026-09-17|执行编排落档。所有"事实"均已在当前工作树核到代码行;行号相对 `Assets/Scripts/`。
本表是 M1.2(表现数据适配)的设计输入。**任何标〔契约单〕的条目在落地前需按 README §5 单独送审,未获批前 View 只能用现有公开面。**

## 0. 事件面(SimEvent,`Sim/SimEvent.cs`)

| 事件 | 语义 | 关键字段 | 已核实事实 |
|---|---|---|---|
| DamageDealt | 一次伤害**登记**(结算前) | Subject=受害者, Amount=结算前原始伤害, AttackerId=来源(0=无) | `SimCore.QueueDamage`(827)先 Emit 再入 `_pendingDamage`;Amount **不等于**实际掉血 |
| EntityDied | 判死(结算后) | Subject=死者 | `ApplyStagedDamage` 统一判死后发出(结算后事件,破盾/死亡表现应优先挂它或快照) |
| EntityRemoved | 实体移除(换模/回收) | Subject=id | 死亡余留特效须在此前取位置快照(BoardView 听它即拆视图) |
| EntitySpawned | 生成/通告(堡垒经 AnnounceSpawn) | Subject=id | 含阵营变色复用(InstantCaptureSystem 用它刷归属色) |
| WallDamaged / WallBroken | 屏障段受击/破段 | Subject=段号, WallSide=守方, AttackerId, Amount | WallFx 现有消费者(`WallFx.cs:73/94`) |
| OvertimeStarted / MatchEnded | 加时/终局 | Subject=胜方(-1 平) | 结算转场用 |
| UnitAdvanced / CommandExecuted / Step* | 行军/命令/时钟 | — | 表现一般不用 |

## 1. 七项语义的现况与策略

### 1.1 结算阶段(盾聚合)
**事实**:伤害同 tick 按**受害者**聚合,盾一次吸收总量、溢出扣血,按受害者 id 升序施加(`SimCore.ApplyStagedDamage` 848-872);`DamageDealt.Amount` 是**扣盾前**值。
**策略**:命中闪光可用 DamageDealt;**破盾/掉血/死亡必须以结算后状态为准**——M1 维护每 tick 末的(Shield,Hp)快照,由快照差分出 破盾/净掉血/死亡,禁止逐 Amount 累加。
**缺口〔契约单·窄〕**:无"结算后摘要"事件。提案:SimCore 在 ApplyStagedDamage 末尾,仅对**状态发生转变**的受害者发一条只读 `SettledDamage`(字段:Subject, ShieldBefore/After, HpBefore/After, AbsorbedByShield)。现有消费者零影响(新增事件);发单送审。

### 1.2 来源身份
**事实**(已逐处核实):
- `AttackerId=0`:无明确来源(常见于旧杂项),**不得**解释为攻城锥。
- 屏障路径:攻击者对**自身**发一条 DamageDealt(`WallSystem.cs:35` 注释:"SimCore 仍无条件发 DamageDealt")→ 必须按"墙体拦截"滤除自伤闪白/弹道。
- **地堡脉冲无任何事件**:直接 `e.Hp -= RamDamage`(`ToolsSystem.Tick`)。缺口〔契约单〕:脉冲加一条 `DamageDealt`(AttackerId=堡垒 id)即可走通用管线,Sim 行为不变。
- 驻军城池攻击:AttackerId=**驻军射手主将** id,冷却挂主将(`GarrisonSystem.cs:360/389`)→ 弹道出发点需 View 侧把"射手主将且驻军中"的发射点改挂其驻扎建筑(可由 `GarrisonBuildingId` 判定,无需 Sim 改动)。
- 箭塔:AttackerId=塔 id(TowerCombatSystem 发弹道事件,已有消费者 BeamFx)。
- **攻城锥(已核,`DestroySystem.cs:212`)**:`ApplyBuildingDamage` 发 DamageDealt 但 **AttackerId 恒为 0**——与"其他无来源建筑伤害"不可区分。T02 若要专属锥击表现,需〔契约单·窄〕:ApplyBuildingDamage 增加 attackerId 入参(现调用方:UseRam 传道具来源、兵营/塔路径各自传实主),现有行为不变。

### 1.3 一击多伤
**事实**:溅射/扇形一击会对多名受害者各发一条 DamageDealt(同 tick 同 AttackerId);同 tick 同攻击者也可能有**多次合法攻击**(不同受击循环)。当前**无攻击序号**。
**策略(现状可行)**:出招表现按"同 tick 同 AttackerId 同目标邻域"聚合一次;命中表现逐受害者播。**已知局限**:同 tick 真多次攻击会被误聚合——记录为已知限制。
**缺口〔契约单·可选〕**:DamageDealt 增 `AttackSeq`(SimCore 自增,每次 ApplyAttack +1)。仅在 M3 实测出现误聚合时再提,不提前扩面。

### 1.4 表现时间
**事实**:倍速=`GameBootstrap.Speed`(float,含截图脚本写入的 3/6 等非档位值),暂停=`Paused`(Update 早退,Sim 与 View 全停)。粒子默认走 `Time.deltaTime`,**不会**自动跟倍速。
**策略**:M1.1 建两时钟——战斗钟(乘 Speed,暂停冻结,驱动粒子 simulationSpeed/轨迹)、UI 钟(unscaled,驱动面板动效)。倍速档位变化事件由 SpeedControlPanel 已有静态 `Latest` 可轮询。×8 节流按 README(合并士兵命中)。

### 1.5 消失与替换
**事实**:EntityRemoved 即拆 EntityView(BoardView);死亡单位保留尸体态至回收;换边=TeardownView 全拆。
**策略**:死亡余留特效在收到 EntityRemoved 时先向 BoardView.GetView 取最后位置/阵营快照,效果挂独立对局根;对局代号由 M1.1 分配,句柄绑代号防跨局复用。

### 1.6 视野
**事实**:单位可见=`FogRules.UnitVisible(sim, side, e)`(View 层,BoardView:98 在用);建筑=`FogRules.BuildingState`(Unknown/Stale/Fresh);TargetingFeedback 已示范 Fresh 过滤。
**策略**:所有附着效果经统一可见性闸(M1.3);弹道按起/终点+中段可见性裁剪;海克斯公告为全局例外(仅卡名+阵营)。

### 1.7 真实状态
**事实**:`state=Attacking` 是持续态;海克斯资格公开查询面:`MoveMultiplierOf/OutputBonusOf/DamageTakenBonusOf/DamageReductionOf/DebuffImmune/TowerExempt/ActiveOf/ActiveRemainingOf`(HeroId→Tag"重装"是 X05 铁壁资格的权威口径,`HexSystem.IsHeavyRegiment` 目前 private)。
**缺口(只读快照,不需改结算)**〔契约单·窄〕:提案 `HexSystem.EffectProbe(sim, entity)` 返回小结构(受哪些卡增益的位标记+关键数值),一次暴露 X01/X04/X05/X06/X08 的资格;另暴露 `ActivationStampOf(sim, side)`(=ActivatedTick)以识别**同卡重放**(X00 要求)。

## 2. 目录条目→现有接线映射(缺口汇总)

| 目录 ID | 现有接线 | 缺口 |
|---|---|---|
| C01/C05 | EntityView.FlashHit/SetDead、FloatingText | 死亡余留需快照钩(1.5);士兵/主将强弱分级=新 |
| C02/C03/C04 | 盾条 EntityView(Hud) | 破盾/回盾**零表现**→依赖 1.1 快照或契约单 |
| C06/C07 | StatusEffects.Has(规则) | 图标系统=新;免疫"被抵抗"通知无权威源(目录已允许降级) |
| H09/H11 | BeamFx(DamageDealt 驱动) | 出手预告缺失(README:优先即时短束) |
| B01/B02 | 箭塔弹道已有;驻军=主将位置出射 | 驻军出射点改挂建筑(1.2,View 内可解) |
| B03/B05/B06 | BuildingSmokeFx/SetRubbled/重建模型 | 重建竖光=新;其余复验 |
| B07/B08 | WallFx(WallDamaged/Broken) | 复用为主 |
| X00-X08 | 无 | 全部依赖 1.7 快照契约 + 激活通告(View 可先做 ActiveOf 轮询版,Stamp 后升级) |
| T01 | TargetingFeedback(目标环/提示/准星) | 合法/非法色分=增强 |
| T03 | 无 | 放置预览需 PlacementBlocked 只读暴露〔契约单·窄:ToolsSystem.PlacementBlocked internal→public〕|
| T04/T05/T06 | 地堡 FBX 已入库 | 部署闪入/脉冲/到期=新;脉冲依赖 1.2 契约单 |
| U01-U07 | 选中环/目的地符部分已有 | 多数=增强+生命周期统一 |

## 3. 送审队列(按优先级)——**异构审核状态(2026-09-17,DeepSeek R1→R2 两轮)**

| # | 提案(最终形态) | 审核结论 | 落地条件 |
|---|---|---|---|
| 1 | **SettledDamage**:结算循环内、逐受害者判死检查**之后**发出(EntityDied 派发之前);字段含 Died(落地名,契约原名 DiedThisTick,异构审核 R2 登记为可接受改名);盾血无净变化不发(意图:分工给 DamageDealt) | R2 修改后放行 | 时序证据已核:`ApplyStagedDamage` 判死就在施伤循环内(Hp≤0 即置 Dead 入列),循环内先判死后发事件即满足 |
| 2 | **FortPulsed 专用事件**(SubjectId=堡垒id,无 Amount/受害者;脉冲伤害路径不变)——采纳 R1"不复用 DamageDealt" | R2 放行 | 契约文档须写明"与 SettledDamage 互不覆盖" |
| 3 | **IsHeavyRegimentOf + ActivationStampOf 两个最小查询**(放弃打包 EffectProbe) | R2 放行 | 文档注明 ActivationStamp 非稳定标识(同卡重放会刷新);IsHeavyRegiment 纯函数性以代码为准 |
| 4 | **PlacementBlocked internal→public** | R1/R2 放行 | reason 常量集中单一来源定义 |
| 5 | AttackSeq(备选) | 信息不足,维持备选 | 需先有"误聚合去重"的实测证据与量化触发条件 |

补记(M1-B1-R2 审核建议关闭项):① BattleTime 浮点累积**仅装饰用途**(驱动淡出/寿命),不参与任何 tick/会话/规则判定,
×8 长局误差纯观感、无功能影响;② 会话过期×在借对象的 EditMode 覆盖见 VfxTests.会话过期时在借对象随拆除安全回收。

审核记录:`docs/VFX_EVIDENCE/review_packs/VFX-M0-CONTRACTS-R1(_deepseek_reply).md`、`-R2(_deepseek_reply).md`。
