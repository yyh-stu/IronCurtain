# 审核包:VFX-M1-B1-R2(补证回复:行为↔用例映射、契约字段对照、五处交叉判据)

回复 VFX-M1-B1 审核的"建议:补充(1)(2)(3)"。请核对以下材料是否消解各发现;仍不成立的请指出。

## (1) 新用例 ↔ 声明行为映射(本批 11 个新用例)

| 用例 | 覆盖的声明行为 |
|---|---|
| VfxTests.战斗钟_倍速缩放且暂停冻结 | ×1/×2/×8、暂停=0、非档位倍速(3)成立 |
| VfxTests.双时钟_暂停时战斗钟冻结而UI钟继续走 | **1-1**:TickClock 真路径(显参注入重载,与 Update 同形),暂停战斗钟冻结+UI钟继续 |
| VfxTests.池_借还配对与超量降级 | **1-3**:超容量 → Borrow 返回 null(调用方降级=直接不播,不排队不新建;见 VfxDirector.OnSimEvent 的 `if (go == null) return`);借还配对;复用不触发工厂 |
| VfxTests.对局根_会话自增与拆除清空 | 会话严格自增、旧销毁不动新会话、显式 Dispose 清静态口 |
| VfxContractTests.契约1 | SettledDamage:盾吸收/破盾条目存在、Absorbed 派生、未死亡且盾无变化时血必降、Died=true、**先于 EntityDied**(索引比较)、无净变化不发 |
| VfxContractTests.契约2 | FortPulsed:SubjectId=堡垒id、3.2s 恰一次、伤害路径不变、SettledDamage 不覆盖脉冲 |
| VfxContractTests.契约3 | IsHeavyRegimentOf(磐石/士兵随团/断峰反例)、ActivationStampOf(0/激活tick/再激活刷新) |
| VfxContractTests.契约4 | PlacementBlocked 公开、reason=常量(Water/NotLaneOrPlaza/合法格) |
| VfxAdapterTests.来源规则 | 墙自伤滤除、AttackerId=0 无来源、驻军发射点挂建筑、野战自身、无来源=0 |
| VfxAdapterTests.摘要分类 | **2-4**:四类=ShieldAbsorb(盾降未空)/ShieldBreak(盾正→0)/Damage(纯掉血)/Death(Died 优先);互斥:Death>Died 判定最先,ShieldBreak 要求 ShieldBefore>0&&After<=0,ShieldAbsorb 要求降但未空,Damage 要求盾无变化;穷尽性:任何发出的 SettledDamage 必落其一(由构造保证) |
| VfxAdapterTests.一击去重 + 可见性闸 | 同tick同攻击者一次/跨tick重置/他人独立;己方恒见/远敌不可见/雾向弹道裁短 |

## (2) R1/R2 批准口径 ↔ 落地字段对照

| 契约条款(批准) | 落地 |
|---|---|
| SettledDamage:结算循环内、判死后、EntityDied 前 | `ApplyStagedDamage` 循环内:先施伤→判死(置 Dead 入列)→发事件;EntityDied 由循环后的既有派发(索引断言在契约1) |
| 字段 ShieldBefore/After+HpBefore/After+DiedThisTick | SimEvent 同名字段(DiedThisTick 命名为 `Died`);AbsorbedByShield=派生属性 |
| 盾血无净变化不发 | `if (diedThisTick \|\| shield!=before \|\| hp!=before)` |
| FortPulsed:SubjectId=堡垒id、无 Amount/受害者、路径不变 | ToolsSystem.Tick 脉冲块 Emit(SubjectId=b.Id),扣血代码零改动(契约2 断言血=直扣值) |
| 两查询:IsHeavyRegimentOf(纯函数)+ActivationStampOf(激活tick,非稳定标识) | HexSystem 公开;IsHeavyRegimentOf 转调 private 纯函数(只读 Tag/Hero);ActivationStampOf=St().ActivatedTick;注释含"非稳定标识,不得作缓存键" |
| PlacementBlocked:public+reason 常量单一来源 | public + `ToolsSystem.PlacementReason` 5 常量(越界/轮廓外/水域/路权/占用),实现处全部引用常量 |

## (3) 五处交叉情形判据

- **1-1 暂停/UI 钟**:见新用例"双时钟_暂停时战斗钟冻结而UI钟继续走"。
- **2-1 Died×无净变化**:不可达组合——Died 当且仅当 Hp 从 >0 跨到 ≤0,本身即血净变化;判据写为不变量
  (发出的 SettledDamage 必满足 Died⇒HpAfter≤0<HpBefore),契约1 已断言 HpAfter≤0。
- **3-1 回退半段**:`BeamVisibleFraction` 采样到第 i 点不可见时返回 `(i-0.5)/n`(夹取≥0)——
  即轨迹显示到"最后一个可见采样点与首个不可见点的中点";0 个可见点→0(整条不显示)。已写入代码注释。
- **3-2 死亡标记×可见性闸**:代码顺序为**可见性闸在前**——目标不可见时直接 return,不借池、不生成、无淡出
  (不存在"不可见却淡出"路径);灰盒淡出只对已生成的可见实例。
- **4-2 会话过期×在借对象**:Update 循环发现会话不符→立即销毁并出列;拆除(Dispose)时在借对象随 Root
  (VfxRoot 为其子物体)一并销毁,池 DisposeAll 只清空闲栈——销毁后的 GO 经 Unity null 语义在 Return 处
  安全跳过。运行时路径当前唯一证据是代码+实机自检(M6 补录屏),EditMode 侧以"显式 Dispose 清空"用例覆盖。

## 其余发现的处置

- 1-2 会话回绕:计数器改 **long**(每次对局/换边+1,物理上到不了 2^63)。
- 1-4 运行时销毁路径:如实保留为已知限制,列入 M6 录屏清单(换边三次无残留判据已有实机口径)。
- 2-3 去重影响面:**仅表现**(View 侧类,Sim 无引用);未预留 AttackSeq 切换接口——该类是唯一调用点,
  替换成本一行,按"出现实测误聚合再提"的既定口径处理。
- 4-1 新旧事件并存:DamageDealt 的消费者(BeamFx/飘字/音效)未动;SettledDamage/FortPulsed 当前零表现消费者
  (M2 起才接入),不存在同一画面双通道重复表现;接入时的分工(出招/弹道=旧,命中结果类=新)已写入契约文档。
- 4-3 浮点累积:BattleTime 仅驱动淡出/寿命装饰,不参与 tick/会话判定(会话=离散计数器);×8 长局误差上界
  纳入 M6 观感复核项。
