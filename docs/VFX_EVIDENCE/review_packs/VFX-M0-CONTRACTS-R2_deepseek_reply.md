<!-- deepseek_review: model=deepseek-chat pack=VFX-M0-CONTRACTS-R2.md usage={"prompt_tokens": 988, "completion_tokens": 1515, "total_tokens": 2503, "prompt_tokens_details": {"cached_tokens": 0}, "prompt_cache_hit_tokens": 0, "prompt_cache_miss_tokens": 988} -->

# VFX-M0-CONTRACTS-R2 审核结论

先说明审核边界:以下判断只基于本材料与一般工程事实。材料未给出的实现细节(如 `ApplyStagedDamage` 内部真实顺序、`HexSystem` 现有公开面)我不做臆测,凡涉及这些的地方我会标为"需证据"。

---

## 1R. SettledDamage(修订)

**结论:修改后放行。**

**消解情况:**
- R1 关于"纯新增、无消费者"的共性缺失,已由补充清单证实:现有 4 个 `DamageDealt` 监听者均不迁移,`SettledDamage` 确为纯新增。这一点 R1 的阻断理由(消费者影响未评估)已被消解。
- 边界(盾血无净变化不发事件)被明确声明为意图,并给出职责分工(DamageDealt 承担"被打了没掉血"的可视化),这是可接受的契约设计。

**残留风险 / 需证据:**
- **顺序冲突问题(建议级,需证据):** 提案称在"判死与 EntityDied 之前"发出,并新增 `DiedThisTick`。但材料同时说"判死在结算末"。若判死逻辑确实位于结算流程末端,则"在判死之前发出 SettledDamage"意味着 `DiedThisTick` 在发出时**尚未被计算**——除非判死被提前到 SettledDamage 之前。材料没有说明判死与 SettledDamage 的先后如何同时满足"先于 EntityDied"和"判死在结算末"。**这是本修订唯一实质性的新引入疑点**,需执行方给出 `ApplyStagedDamage` 内的实际调用顺序,确认 `DiedThisTick` 在事件发出时已可读。
- 若 `DiedThisTick` 依赖判死结果,而判死又在结算末,则存在"事件发出时字段未就绪"的时序矛盾。建议在契约中钉死:`DiedThisTick` 的计算点必须早于 SettledDamage 发出点。

**依据原文:** "在 `ApplyStagedDamage` 内、**判死与 EntityDied 之前**发出;字段增 `DiedThisTick`";"生死看 DiedThisTick";"判死在结算末"(引自 R1 语境,材料未反驳)。

---

## 2R. 地堡脉冲(新增 FortPulsed)

**结论:放行。**

**消解情况:**
- 采纳 R1 替代方案,不复用 `DamageDealt`,避免污染其"来源身份敏感"语义(BeamFx 对 AttackerId=0/自伤会跳过)。这是正确的隔离。
- 脉冲伤害路径不变、不经聚合、不与盾交互,口径明确。
- "受害者侧命中反馈不做"被显式记为已知限制,并说明 SettledDamage 也不覆盖脉冲(因脉冲不经聚合)——**这个口径是自洽的**,没有夸大覆盖范围。

**残留风险:**
- 无受害者字段导致受害者侧反馈缺失,属**已声明的已知限制**,非缺陷。若后续需要,须另立事件,当前不阻断。
- 建议级:契约文档应写明"FortPulsed 与 SettledDamage 互不覆盖",避免未来消费者误以为脉冲伤害会出现在 SettledDamage 流中。材料已口头说明,建议固化进文档。

**依据原文:** "新增专用只读事件 `FortPulsed`(SubjectId=堡垒实体id,无 Amount、无受害者清单)";"脉冲不经聚合,SettledDamage 也不覆盖它,这是明确的口径"。

---

## 3R. 海克斯只读面(缩小契约面)

**结论:放行。**

**消解情况:**
- 采纳 R1 "更小替代",放弃打包式 EffectProbe/位标记,规避了版本化问题。契约面缩小到两个最小查询,符合"只读、无副作用"的诉求。
- `IsHeavyRegimentOf` 声明为纯函数(只读 Tag 与 Hero,无副作用),`ActivationStampOf` 语义明确(本次激活 tick,同卡重放会变化)。

**残留风险 / 需证据:**
- **建议级:** `ActivationStampOf` 语义为"本次激活发生的 tick",同卡重放时值变化。消费者若用它做去重/缓存,需知道"重放会刷新该值"。建议在契约中明确消费者不得将其当作稳定标识。材料已点出该行为,但未说明消费者约束。
- 材料称 `IsHeavyRegimentOf` "复用 private 判定",但未给出该 private 方法是否真的无副作用。**需证据**:确认被复用的 private 判定确为纯函数(材料已声明,但属执行方自述,建议以代码为准)。

**依据原文:** "只公开两个最小查询";"该方法为纯函数,只读 Tag 与 Hero,无副作用";"同卡重放时该值变化"。

---

## 4R. PlacementBlocked internal→public

**结论:放行。**

**消解情况:**
- 仅可见性变化,签名与返回编码固化进契约文档,reason 字符串常量枚举了 5 类。R1 已放行,本修订补充编码约定,无新增风险。

**残留风险:**
- 建议级:reason 字符串常量应集中定义(单一来源),避免调用方硬编码字面量导致漂移。材料未说明常量定义位置,建议在契约中指定。

**依据原文:** "返回编码固化进契约文档:`(越界/轮廓外/水域/路权/占用)` 对应 reason 字符串常量";"仅可见性变化"。

---

## 汇总

| 编号 | 结论 | 主要残留 |
|---|---|---|
| 1R | 修改后放行 | `DiedThisTick` 与"判死在结算末"的时序需证据确认(建议级,但为唯一实质疑点) |
| 2R | 放行 | 无受害者反馈为已声明限制;建议固化"两事件互不覆盖" |
| 3R | 放行 | `ActivationStampOf` 消费者约束、private 判定纯函数性需证据 |
| 4R | 放行 | reason 常量单一来源建议 |

**R1 阻断/建议项消解情况:** 消费者清单缺失(阻断)已消解;2R/3R 的替代方案已采纳;4R 维持放行。**未发现被驳回项。**

**新引入问题:** 仅 1R 的 `DiedThisTick` 时序疑点一处,属需证据确认,不构成阻断——若执行方能证明判死计算点早于 SettledDamage 发出点,则 1R 可直接放行。

**没有发现其他问题。** 材料未涉及的部分(如 `ApplyStagedDamage` 真实实现、`HexSystem` 现有公开面)我未做推测。