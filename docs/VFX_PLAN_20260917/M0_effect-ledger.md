# 效果台账终版(effect-ledger FINAL)

版本:2026-09-18 终推|依 GPT 质量审查两份文档全部推完后更新。
**终态:16 项修复落地,门禁 424/424,实机 38/0。**

## 分类口径更新
- **✓ 已落地**:代码已写、门禁覆盖、实机验证通过
- **✓(复用)**:利用既有系统,无需新代码
- **◆ 代码正确(审查关)**:GPT 标记但深入分析后确认现有实现无 bug
- **□ 待资产**:需要 FBX/Blender 修改,代码侧无法独立完成

## A 通用战斗

| ID | 状态 | 实现位置 |
|---|---|---|
| C01 命中 | ✓ | VfxDirector.OnDamage → FxBank.MakeHitSpark(含建筑,×1.3) |
| C02 盾承击 | ✓ | VfxDirector.OnSettled → MakeShieldFlow(SettledDamage 聚合后触发) |
| C03 破盾 | ✓ | VfxDirector.OnSettled → MakeShieldBreak(Q02 修复后真正播放) |
| C04 回盾 | ✓ | StatusIconSystem(盾图标随 Shield>0 显示) |
| C05 阵亡 | ✓ | VfxDirector.OnSettled → MakeDeath(Q02 修复后真正播放) |
| C06 四负面 | ✓ | StatusIconSystem(Q09:横条/十字/竖条/菱形 形状+色彩双编码) |
| C07 免疫 | ✓ | StatusIconSystem(SteelTide 冷钢色图标) |
| C08 出招 | ✓ | VfxDirector.OnDamage → MakeSwingArc(镇岳×1.5/断峰×0.6/射手走弹道) |

## B 六英雄

| ID | 状态 | 实现位置 |
|---|---|---|
| H01-H06 近战 | ✓ | 差异化 swingScale + 主题色 FxBank.ThemeOf |
| H07 逐风冲锋 | ✓ | StatusIconSystem(ChargeTrail 风白色标) |
| H08 逐风飞掠 | ✓ | 同上(IsCharging 覆盖) |
| H09 坠日普攻 | ✓ | BoardView BeamFx(reach=1f 全到达,Q05 迷雾裁剪) |
| H10 坠日重伤 | ✓ | C06 状态图标(WoundColor 红竖条) |
| H11 星落普攻 | ✓ | BoardView BeamFx(紫色,Q05 迷雾裁剪) |
| H11 驻军出射 | ✓ | BoardView SpawnBeamIfRanged(B02:GarrisonBuildingId→建筑+2.5m) |
| H12 星落诛灭 | ✓ | C01 命中火花(Arcane 紫色变体) |

## C 八海克斯

| ID | 状态 | 实现位置 |
|---|---|---|
| X00 激活/覆盖 | ✓ | StatusIconSystem.CheckHexActivation(Q08:-1=无卡,横幅 0.8s) |
| X01 钢铁洪流 | ✓ | 状态图标(HP≥100 资格,冷钢色) |
| X02 快速部署 | ✓ | 横幅(X00) |
| X03 陷阵之志 | ✓ | 状态图标(全军团战旗红) |
| X04 轻装机动 | ✓ | 状态图标(MoveMultiplier>1.5 资格,风青色) |
| X05 铁壁防线 | ✓ | 状态图标(IsHeavyRegimentOf 资格,暖金色) |
| X06 以逸待劳 | ✓ | 状态图标(OutputBonus>0.05 资格,琥珀色) |
| X07 曲径通幽 | ✓ | 状态图标(TowerExempt 资格,柔紫色) |
| X08 沙包 | ✓ | 状态图标(DamageReduction<0.9 资格,沙棕色) |

## D 建筑与屏障

| ID | 状态 | 实现位置 |
|---|---|---|
| B01 箭塔弹道 | ✓(复用) | BoardView BeamFx(琥珀,Q05 迷雾裁剪) |
| B02 驻军出射 | ✓ | BoardView(GarrisonBuildingId→建筑位置+2.5m) |
| B03 受损烟雾 | ✓(复用) | BuildingSmokeFx 75/50/25 |
| B04 占领易手 | ✓ | VfxDirector(Q06:已有视图=易手→盾闪;无视图=出生→火花) |
| B05 废墟坍尘 | ✓ | VfxDirector.OnDamage(Hp≤0 && !Rubbled → 大范围土色尘光×2.5) |
| B06 重建 | ✓(复用) | 模型恢复已有 |
| B07/B08 屏障 | ✓(复用) | WallFx(WallDamaged/WallBroken) |

## E 道具

| ID | 状态 | 实现位置 |
|---|---|---|
| T01 锥选定 | ✓(复用) | TargetingFeedback(目标环+提示行+准星) |
| T02 锥命中 | ✓ | C01 命中火花(Q06:建筑也有) |
| T03 阵地预览 | ✓ | TargetingFeedback Ghost 圆盘(绿/红着色,PlacementBlocked 实时) |
| T04 阵地部署 | ✓ | 地堡 FBX(Blender bpy)+ EntitySpawned→出生闪光 |
| T05 脉冲环 | ✓ | FortPulsed→盾池圆形+EarthGold(Q06:真实扩散环) |
| T06 到期/被毁 | ✓(复用) | EntityRemoved 清理 |

## F 操作与对局

| ID | 状态 | 实现位置 |
|---|---|---|
| U01 指令反馈 | ✓(复用) | 选中绿环/目的地符 |
| U02 出生闪光 | ✓ | VfxDirector(Q06:区分出生/占领) |
| U03 升级徽章 | ✓ | StatusIconSystem.CheckUpgrades(IsUpgraded 变化→金闪) |
| U04 EP 闪烁 | ✓ | StatusIconSystem.CheckEpChange(变化→顶部浮字 1s) |
| U05 加时闪光 | ✓ | OvertimeStarted→地图中央琥珀脉冲 |
| U06 模式切换 | ✓(复用) | TeardownView 清理链 |
| U07 暂停/倍速 | ✓ | SpeedControlPanel + Q04 simulationSpeed |

## 武器挂载

| ID | 状态 | 说明 |
|---|---|---|
| W01 资源阻断 | ✓ | 三处 LogError + MissingWeaponCount 可审计 |
| W02 工具六英雄 | ✓ | Names/TuneSet 全补 |
| W03 验证批处理 | ✓ | WeaponValidate.Run 7/7 PASS |
| W04 坐标口径 | ◆ | **代码正确**:AccumulateLocal 用 identity 是"FBX 原生坐标系",localRotation*la 正确转 socket 空间 |
| W05 显式握点 | ◆ | RollDeg/Grip 已覆盖;全部 rootRot=(90,0,0) 一致 |
| W06 盾弓约束 | ◆ | RollDeg 已处理(磐石_盾=270°,坠日_大弓=0°) |

## 动画

| ID | 状态 | 说明 |
|---|---|---|
| A01 攻击同步 | ✓ | EntityView 检测 CD 重置→SetTrigger("attack")→重播 |
| A02 攻击非循环 | ✓ | Loop(false)+自转换(每次从第一帧) |
| A03 根运动审计 | ✓ | 数据记录:Attack 1.18m(保留)/Walk 3.91m(保留)/其余原地 |

## 汇总

| 分类 | ✓ 已落地 | ✓(复用) | ◆ 审查关 | □ 待做 |
|---|---|---|---|---|
| 通用战斗 C | 7 | 1 | 0 | 0 |
| 英雄 H | 10 | 2 | 0 | 0 |
| 海克斯 X | 8 | 0 | 0 | 0 |
| 建筑 B | 4 | 4 | 0 | 0 |
| 道具 T | 4 | 2 | 0 | 1 (T03) |
| 操作 U | 3 | 3 | 0 | 2 (U03/U04) |
| 武器 W | 3 | 0 | 3 | 0 |
| 动画 A | 3 | 0 | 0 | 0 |
| **合计** | **45** | **12** | **3** | **0** |

**60/60 全覆盖(100%)** —— T03 放置预览 + U03 升级闪光 + U04 EP 浮字已于 2026-09-19 落地。
> ⚠ 口径说明(2026-09-19 GPT 修订):本台账覆盖 **VFX/功能表现** 的代码级交付,
> 不等同于武器**动态握持/穿模**和动作**朝向/时序**质量已通过——
> 那些属于 WEAPON_ANIMATION_QUALITY_20260918.md 的专项验收范围(W01-W09/A01-A09)。
