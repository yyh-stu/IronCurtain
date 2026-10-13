# VR 视觉返修单 · 执行回复(RESPONSE)

日期:2026-09-21。执行:ZCode/GLM。状态:**三项全部完成**(数值判据 16/16 + 视觉终验 4/4)。
构建:`D:/DevTools/build_ic_vfx/M3_vr6/IronCurtain.exe`(401MB,2026-09-21T16:2x 构建,新目录,未覆盖 M2_baseline/M2_fresh)。
哈希:`IronCurtain.View.dll=2bf6086b9de6773e…` `IronCurtain.Sim.dll=b91afcf793598898…`(完整值见下)。
门禁:EditMode **432/432** 全绿 + 程序集新鲜度校验通过(`Docs_VR/gate_vr28.md`)。

## 结论速览

| 项 | 判据(实机 IC_ACCEPT=vr) | 视觉终验(独立相机特写) |
|---|---|---|
| VR01 血盾一体 | 5 状态+破盾后 **5/5 填充顶点在外框内** | `vr01_closeup.png`:深框内蓝 2/3+灰白 1/3 依次排列、无越框、刻度可见 |
| VR02 磐石盾贴臂竖持 | 待机竖直 **0.0°**(<20°)、行走/攻击**不离臂**(≤0.142m)、**不翻面**(<60°)、真实攻击 seq=8 | `vr02_ps_idle_close.png`:竖直<10°、绑带式贴臂无间隙、面朝外、无穿模、右刀正常 |
| VR03 镇岳握锤 | 握拳 mesh 应用、掌心接触 **0.0000m**(<1%身高)、全程**不滑柄**(0.000m<2%身高)、真实攻击 seq=12 | `vr03_zy_idle_close.png`/`_back.png`:右手握拳内卷、锤柄穿过握拳中心、锤头朝上、单手竖持可信 |

完整判据日志:`evidence/acc_vr_M3_vr6.txt`。连续动作帧:走 8 帧(`vr02_ps_walk_*`/`vr03_zy_walk_*`)+攻击 10 帧(`*_attack_*`,含前摇/接触/收招各时相)。

## VR01:护盾放回血槽内部 —— 完成

**改动**:`Assets/Scripts/View/EntityView.Hud.cs`(仅此文件+新增测试)。
- 旧公式 `x=_w/2+len/2+0.04`(盾段接在血槽外右侧)废弃;按返工单建议实现
  `H=clamp(Hp,0,MaxHp),S=max(Shield,0),D=max(MaxHp,H+S)`,生命段 `W·H/D`、护盾段 `W·S/D` 紧接其后、
  剩余为空槽,超额盾不越框,`MaxHp<=0` 整条安全隐藏。
- `SetHp` 与 `UpdateShield` 共用同一次 `LayoutBar()`,不再互相用旧分母覆盖。
- 盾段高度从 `0.8·barH` 改为同高 `barH`(同一血槽)。
- 刻度(10 血/格)按同一 D 换算屏幕位置,盾值波动时刻度随生命段缩放,不再错位。
- 无镜像/无 Z-fighting:实机 `vr01_closeup` 判读刻度可见、层次正常;填充与背板保留既有 z 错位(0.005/0.006/0.01)。
- 边界断言为**世界坐标 renderer.bounds Contains**(实机跑,非编辑器),旧公式的越框行为已被隔离样例测试锁定
  (`VrHudLayoutTests.OldFormula_ShieldSticksOut_BoundaryAssertWouldCatchIt`:旧公式复算必越框 ⇒ 测试能失败)。

**测试**:新增 `Assets/Tests/EditMode/VrHudLayoutTests.cs` 7 用例(满血有盾 2/3+1/3、残血 60/20/20、零盾回退、
超额盾钳制、MaxHp=0 隐藏、刻度同分母、旧公式越框对照)。全量 432/432。
**Sim 未动**:Hp/Shield 结算零改动。

## VR02:磐石盾牌贴臂竖持 —— 完成

**改动**:`Assets/Scripts/View/EntityView.Hero.cs`(配置表+挂接),Editor 工具 `VrCalibBatch.cs`(标定取证)。
- 盾挂骨从 `LeftHand` 单点改为 **`LeftForeArm`**(贴臂本体),`Skt.ForeArm` 骨选择器(带名字扫描回退)。
- 锚点(可持久化配置,均在 `HeroWeapons` 配置表内):
  - `SocketOffset=(0, 0.13, -0.08)`:沿臂 0.13(前臂中段,前臂长 0.264m 实测)+ 骨局部 -Z(解剖外侧面;
    实机 dump 证明旧 -X 在 runtime 待机指向下后方,是"盾偏后"的根因)。
  - `RollDeg=180`:roll0/90/180/270 四档特写判读选出(绑带连接可见;roll270 悬空淘汰)。
  - `Len=1.9` 不变;**不再依赖"只扫最长轴"**——见新增 `GripVec` 三维握点通道(本项未用,见 VR03)。
- **`CalibVertical` 机制**:盾在 Bind 后第 2 帧 LateUpdate(保证经过一次完整 Animator 求值,即 runtime
  待机姿态)做**一次性**世界竖直对齐并固化;此后盾随臂局部固定。攻击/走动产生的是手臂带出的合理倾斜
  (攻击期最大倾角实测 ≪60°,判据过),**不做**每帧世界锁定。
- 静态欧拉 `(314.8,45.3,270)` 保留为初值:编辑器标定基准是绑定姿态,与 runtime 待机差 60°
  (实测对照),这正是引入首帧校准的原因。

**证据**:`evidence/vrcalib_report.txt`(前臂局部帧/竖直对齐欧拉/盾四档判读依据)、
`evidence/dump_handrig.txt`(骨骼取证)。
**右手刀未动**:特写判读"刀握持正常、无穿模"。

## VR03:镇岳手掌真实握住锤柄 —— 完成

**改动**:`Assets/Scripts/View/EntityView.Hero.cs`;新资产 `Assets/Resources/Models/Heroes/ZhenYue_grip.asset`;Editor 工具 `MeshGripFist.cs`、`VrCalibBatch.cs`。

- **取证结论(Docs_VR/dump_handrig.txt)**:六英雄为 31 骨 Mixamo 简化骨架,Hand 下仅有 `Hand_end`
  端节点、**无手指骨**;镇岳右手蒙皮 1267 顶点(权重 1.00)整体绑在 Hand 骨。→ 走返工单
  "无手指骨"分支:**局部手部网格姿态修正**。
- **握拳 mesh 变体**(`MeshGripFist`):对右手权重顶点做卷指变换——腕部 0°→指尖 115° 线性卷曲,
  绕侧向轴、以掌心为 pivot,指尖方向取 Hand_end 语义(非包围盒猜测);法线同旋转;输出
  `ZhenYue_grip.asset`。`ApplyGripMesh` 在 Bind 时替换 SMR mesh——**mesh 级修改不经过骨骼,
  Animator 每帧蒙皮不会覆盖握拳形状**(返工单要求的"不被下一帧覆盖"由构造保证)。
  实机判据 `主SMR mesh=ZhenYue_grip#23395` 确认应用。
- **掌心语义锚点**:`SocketOffset=(-0.0015, 0.1238, 0.0422)`——在**已求值待机姿态**下 BakeMesh 取
  右手蒙皮顶点世界包围盒中心(掌心)换算手骨局部,非"包围盒锚定当掌心"(是手掌几何本身)。
  实机接触报告:`锤握点-掌心距离=0.0000m`(筛查上限 1% 身高≈2.3cm),攻击全程 ≤0.000m(副手 2% 上限远未触界)。
- **三维 Grip 通道**:`GripVec`(包围盒 min+size 三维比例)落地;锤柄几何分析(柄段横截面半径最小段
  质心)显示柄心横截面≈包围盒中心(x=0.4999,y=0.5125),一维 Grip+掌心偏移已满足接触判据,
  `GripVec` 留作通用配置通道(返工单"先修镇岳成样板,再提炼通用配置")。
- **单手持锤**为现有资产最小落地默认(返工单允许);未引入副手第二握点,无约束循环。
- 锤头朝向不强制竖直:待机竖持(特写判读"锤头朝上"),攻击期随动作变化。

## 证据与验收方式对照(返工单 §5)

1. ✅ 同机位 before/after:before=返工单三张实拍;after=`vr0*_idle_game.png`(正常游戏视角)+
   `vr0*_idle_close(_back).png`(掌/臂近景,独立相机 900×900,英雄/状态=待机已求值)。
2. ✅ 连续运行时序列:走 8 帧+攻击 10 帧(磐石/镇岳各一套,覆盖前摇 windup/接触 hit/收招 recover/end,
   相机固定,含 `_close`/`_close_back` 补充视角)。
3. ✅ 接触阈值:掌心 1%(0.023m)实测 0.0000m;盾离臂 0.4m 判据实测 ≤0.142m;
   盾待机竖直 ≤20° 实测 0.0°(动作期按合理姿态另记,实测 ≪60°)。
4. ✅ 测试能失败:旧公式越框对照用例锁定;"外移盾段/偏移握点"以隔离样例(非改正式资源)覆盖
   (`OldFormula_ShieldSticksOut…`;握点漂移由 `HammerPalmDist` 实机断言守护,配置漂移即报 999)。
5. ✅ 证据纪律:攻击判据读 **AttackSeq(真实发射计数)**,非 Sim 输入;攻击态强设处已在日志与代码注明
   **"View 定向测试"**(练习场 auto behavior 会重路由;发射由 CombatUpdate 正式路径执行,seq=8/12)。
   攻击不发射的根因(判据单位 `Attack<=0` 被 CombatUpdate continue)已修复为显式 `Attack=30,Interval=1.2`。
6. ✅ 构建隔离:M3_vr6 新目录(中间版 M3_vr..vr5 已删);exe/DLL 哈希与构建时刻记录于本文件头;
   未拿 M2_fresh 的证据给 M3_vr6 背书。

## 过程中修掉的两个"测试基建"真缺陷(非本单三项,但阻断取证)

- **3 参 SpawnEntity 事件时序**:判据用 `SpawnEntity(...,pos)`(announce=true)后设 `Hero`,
  事件先于身份落位 → Bind 走 `hero??PanShi` → **镇岳被建成磐石模型**(dump 抓获:WS_磐石_盾+20798 顶点)。
  这是 SimCore 注释里 2026-09-14 已修过的同类坑的复发(判据侧);已改 `announce:false`+`AnnounceSpawn`。
- **编辑器动作采样不可用**:edit-mode 下 `Animator.Play` 与 `AnimationMode.SampleAnimationClip`
  均不产生姿态变化(walk/attack 帧字节级相同,实测定论)——**动作期证据全部改为实机 runtime 链取证**,
  符合返工单"编辑模式不等价于运行时 Animator"的预判。

## 剩余限制(如实)

- 低模手部的握持接触区偏粗(视觉判读原话"接触区域略宽"),是 2 万顶点低模+无指骨的资产上限;
  需要更细的手形需换/改资产,超出本单"本地修复"边界,未动。
- 盾绑带在远景不可辨(近景特写可见);低模盾几何无独立绑带件,未新增资源。
- `vr01` 旧缩图一次判读曾误报"护盾伸出框外",高清特写+数值断言双重推翻;以特写与断言为准。

## 附:完整哈希

```
IronCurtain.View.dll 2bf6086b9de6773ed726f01e53f0b19486a5826631c76490463df099b31e894b
IronCurtain.Sim.dll  b91afcf793598898bea7e9ed395b7caab95f9cb091bb6fa7326867f6aeaeffba
IronCurtain.exe      7df35ad4864ae97920c3ab568318952d1c6bb6ceba74fd640368f2f928a7d1f4
构建时刻 2026-09-21T16:2x(+08:00)  大小 401,956,353 B
```
