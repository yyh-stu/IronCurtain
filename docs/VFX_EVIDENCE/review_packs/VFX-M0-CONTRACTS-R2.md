# 审核包:VFX-M0-CONTRACTS-R2(公共接口提案修订版,回应 R1 审核)

R1 审核(DeepSeek,`VFX-M0-CONTRACTS-R1_deepseek_reply.md`)后的修订。请逐条(1-4;原提案 5 维持备选不再送审)
给出:结论(放行/修改后放行/驳回)、残留风险、依据。R1 提出的"共性缺失:现有消费者清单"已在下文补充。

## 补充事实:现有消费者清单(执行方核实)

`DamageDealt` 现有监听者(仅 View 层;Sim 侧 MatchStats 走独立的 AddCombatDamage 内部调用,不经事件):
1. `BeamFx`(BoardView 分发):按 AttackerId 生成远程弹道——**对来源身份敏感**(AttackerId=0 或 自伤 会跳过)。
2. `FloatingTextView`:飘字(伤害数字)。
3. `AudioSystem`:受击音效触发。
4. `IcAcceptance`(自检,只读取证)。
`EntityDied` 当前无 View 监听者(VfxDirector 的内部验证标记为 M1.1 新增,只读位置)。

## 修订后提案

### 1R.SettledDamage(修订)
- **顺序钉死**:在 `ApplyStagedDamage` 内、**判死与 EntityDied 之前**发出;字段增 `DiedThisTick`(bool)。
  消费者在 SettledDamage 时点可读最终盾/血快照,生死看 DiedThisTick;EntityDied 语义不变、仍随后发出。
- **边界确认**:盾血均无净变化(全额免伤/乘子归零)不发事件——**这是意图**(表现只关心实际变化);
  "被打了但没掉血"的可视化由 DamageDealt(结算前,含 Amount)承担,两事件职责分工写入契约。
- 消费者影响:纯新增,清单中无人监听;BeamFx/FloatingText/AudioSystem 均不迁移(旧事件保留原语义)。

### 2R.地堡脉冲(修订:不复用 DamageDealt)
- 采纳 R1 替代方案:**新增专用只读事件 `FortPulsed`**(SubjectId=堡垒实体id,无 Amount、无受害者清单),
  在脉冲结算处发出;脉冲伤害路径**完全不变**(仍直接扣血、不经聚合、不与盾交互)。
- 表现层据此:堡垒口闪光 + 低贴地脉冲;受害者侧命中反馈不做(没有可靠受害者事件,记已知限制,
  待 SettledDamage 落地后如需再评估——注意脉冲不经聚合,SettledDamage 也不覆盖它,这是明确的口径)。

### 3R.海克斯只读面(修订:缩小契约面)
- 采纳 R1 "更小替代":只公开两个最小查询——
  `HexSystem.IsHeavyRegimentOf(sim, entity)`(复用 private 判定;该方法为纯函数,只读 Tag 与 Hero,无副作用)
  与 `HexSystem.ActivationStampOf(sim, side)`(=ActivatedTick,语义:**本次激活发生的 tick**;同卡重放时该值变化)。
- 放弃打包式 EffectProbe/位标记(版本化问题留给真实需要时再议);八卡其余资格继续用既有公开查询推导。

### 4R.PlacementBlocked internal→public(维持 R1 放行,补编码约定)
- 返回编码固化进契约文档:`(越界/轮廓外/水域/路权/占用)` 对应 reason 字符串常量,方法签名
  `(SimCore, Side, GridPos, out string reason) → bool`;仅可见性变化。

## 请你审查

- 修订是否消解了 R1 的全部阻断/建议项?有没有新引入的问题(例如 SettledDamage 先于 EntityDied 是否
  与"判死在结算末"的现有顺序冲突;FortPulsed 无受害者字段是否够用)?
- 消费者清单是否改变了你对 1R/2R 的风险判断?
