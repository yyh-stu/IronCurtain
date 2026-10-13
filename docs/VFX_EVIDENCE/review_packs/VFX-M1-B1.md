# 审核包:VFX-M1-B1(普通批次:M1.1 / M1.2 / M1.3)

你是独立审核员。材料是一个 Unity RTS 项目"特效计划 M1(最小公共表现基础)"三个子模块的实施记录。
请给出:每子模块(1-3)的发现(真实缺陷/缺失证据/规则不一致),严重度(阻断/建议),依据;没有问题就明说。

## 基线与变更

- baseline_head: cac5b75f62a1f073aaf36177fc355f592c5ba90f(工作树快照 tar sha256 4a7ca1a1…,M0 已存档)
- 本批 changed_paths:
  - Assets/Scripts/View/VFX/VfxDirector.cs(新)
  - Assets/Scripts/View/VFX/VfxPool.cs(新)
  - Assets/Scripts/View/VFX/VfxSourceRules.cs(新)
  - Assets/Scripts/View/VFX/VfxVisibility.cs(新)
  - Assets/Scripts/View/GameBootstrap.cs(装配+拆除各 2 行)
  - Assets/Sim 侧契约落地:SimEvent.cs(枚举+SettledDamage 字段)、SimCore.cs(ApplyStagedDamage 内发 SettledDamage)、
    ToolsSystem.cs(FortPulsed 事件 + PlacementBlocked 公开 + reason 常量)、HexSystem.cs(IsHeavyRegimentOf/ActivationStampOf)
  - 测试:VfxTests.cs(3)、VfxContractTests.cs(4)、VfxAdapterTests.cs(4)
- 门禁:EditMode 419/419(409 基线 + 10 新),error CS=0,程序集新鲜度通过。
- 契约四条此前已经你两轮审毕(R1/R2,VFX-M0-CONTRACTS 包);本批是它们的落地。

## 实施摘要(供审查,不作为通过声明)

- M1.1:VfxDirector=对局根+会话代号(自增)+双时钟(战斗钟=帧时×GameBootstrap.Speed、暂停冻结;UI钟=unscaled;
  非档位倍速同样成立)+内部验证标记(EntityDied→池化灰盒→MPB 淡出→会话过期自毁)。
  VfxPool=按效果族(借还配对/超量返回 null 走降级/整池销毁;EditMode 用 DestroyImmediate)。
- M1.2:SimEvent 增 SettledDamage(循环内、判死后、EntityDied 前;盾血无净变化不发;Died 标记)与 FortPulsed
  (SubjectId=堡垒id,伤害路径不变);HexSystem 两查询;PlacementBlocked 公开+常量。
  VfxSourceRules=墙体自伤滤除/无来源判定/驻军发射点改挂建筑/摘要四分类;VfxSwingDedup=同tick同攻击者出招去重。
- M1.3:VfxVisibility=附着(ShowAttached)/建筑实时态(ShowBuildingLiveState)/弹道分段采样(3米步长,返回可见比例,
  回退半段);死亡标记已接入可见性闸(不可见不显示)。
- 消费者回归:BeamFx/WallFx/FloatingTextView/AudioSystem 未改动、未迁移(旧事件原语义);全量 419 绿。

## 已知问题/局限(主动申报)

1. 出招去重按(攻击者,tick):同 tick 真多次合法攻击会误聚合(AttackSeq 契约维持备选)。
2. 弹道采样步长 3 米,窄于 3 米的雾带可能漏裁。
3. 验证标记(灰盒)未过任何美术判断;金样在 M2。
4. 静态口守卫用 ReferenceEquals、EditMode 拆除须显式 Dispose(两个 Unity 坑已写进代码注释)。

## 请你审查(开放题)

- 三个子模块有没有材料没说但你认为必须验证的点?
- Sim 侧契约落地与此前批准口径有没有偏差?
- 已知问题之外,你还看到哪些风险(生命周期/可见性泄露/倍速/池)?
