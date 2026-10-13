# 审核包:VFX-M0-CONTRACTS-R1(公共接口提案,5 条)

你是独立审核员。以下是一个 Unity URTS 项目"特效计划"中,执行方提出的 5 条 Sim 侧公共接口提案。
背景约束(项目计划明文):**不允许改变玩法数值、结算顺序或既有公共签名**;提案只允许"新增只读事件/查询"或"内部方法转公开"。
请逐条(1-5)给出:结论(放行/修改后放行/驳回)、你发现的风险或缺失字段、依据。
材料中没有的事实不要臆测;认为信息不足就写"信息不足,需补充 X"。

## 现有事实(已由另一模型核到代码行,供你交叉审查,不必盲信)

- 伤害流程:每次攻击先 Emit `DamageDealt`(字段:受害者id/Amount=扣盾前值/AttackerId,0=无来源),再入队;
  同 tick 末按受害者聚合:护盾一次性吸收总量、溢出扣血,之后统一判死发 `EntityDied`。
- 无"结算后摘要"事件;盾/血的最终变化只能事后读字段。
- 地堡脉冲:每 3 秒对 2 米内敌军直接扣血,**不发任何事件**。
- 攻城锥:对敌建筑直伤走公共方法 `ApplyBuildingDamage`,发 `DamageDealt` 但 AttackerId 恒 0。
- 海克斯八卡的单位资格:大多可由公开查询(移速倍率/输出加成/承伤加成/免伤乘子)反推;
  "重装军团"判定(主将 Tag 以"重装"开头,磐石/镇岳)目前是 private 方法;
  同卡重复释放的识别依赖内部字段 ActivatedTick(无公开读法)。
- 道具"临时阵地"的放置校验 `PlacementBlocked`(越界/轮廓外/水域/路权/占用)是 internal 方法。

## 提案

1. **SettledDamage 结算后事件**:在统一结算(聚合扣盾扣血)结束后,仅对盾或血发生实际变化的受害者各发一条
   `SettledDamage`(字段:受害者id, ShieldBefore/After, HpBefore/After, AbsorbedByShield)。
   声称:纯新增事件,不改变结算本身;现有消费者无人监听它。
2. **地堡脉冲发 DamageDealt**:脉冲扣血处补一条 `DamageDealt`(AttackerId=堡垒实体id),结算语义不变。
3. **HexSystem.EffectProbe(sim, entity) 只读查询**:返回结构含"受哪些卡增益"位标记与关键数值
   (钢铁洪流/轻装机动/铁壁/以逸待劳/沙包资格等);另加 `ActivationStampOf(sim, side)` 暴露 ActivatedTick。
   声称:纯只读,复用既有 private 判定,不新增状态。
4. **ToolsSystem.PlacementBlocked internal→public**:仅可见性变化,供放置预览特效读取合法性。
5. **DamageDealt.AttackSeq(备选)**:事件加攻击序号字段以区分同 tick 同攻击者的多次合法攻击。
   声称:仅在实测出现"误聚合去重"时才实施。

## 请你审查的问题(开放题)

- 每条提案是否真的满足"不改数值/不改结算顺序/不改既有签名"?有没有隐藏的顺序依赖或消费者风险?
- 字段设计有没有缺口(例如 SettledDamage 的受害者已死亡/已移除时怎么办;AttackerId 语义是否会与"无来源"混淆)?
- 有没有更小的替代方案达到同样目的?
- 有没有材料没给但你判断必须补充的信息?
