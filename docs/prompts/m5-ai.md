# M5 AI 系统 —— 简单/普通两档(GL M 绿场实现+测试;新手教学与智能决策体待用户定稿)

## 一、角色与任务
你是《铁幕:前线》Unity 6 项目 AI 系统主程。本模块实现**简单与普通两档 AI**。只新增两个文件:`Assets/Scripts/Sim/AiSystem.cs` 与 `Assets/Tests/EditMode/AiTests.cs`。**禁止修改任何既有文件**(120 个既有测试必须保持全绿)。编排者已把 `AiSystem.Tick(this, dt)` 挂进 SimCore.Step 末尾。

## 二、设计总纲(权威)
- **信息原则**:AI **只读 VisionSystem 视图获取敌方信息**,绝不直读敌方实体的精确状态(位置/血量/兵种);己方信息(人口/EP/建筑/单位)可直读。这是硬性架构约束,违反即作弊。
- **决策节奏**:每 **2 秒(20 tick)** 一个决策帧(所有档位一致,模拟人的反应延迟)。
- **指令接口**:AI 与玩家完全同源——生产 Enqueue、移动 SetMoveTarget、攻城 SetIntent/IssueOrder、海克斯 UseHex、升级 BuyUpgrade。无任何特殊通道。
- **单线适配**(用户拍板):简单 AI = 单线匀速爆兵(正式版三路同构参数化)。

## 三、档位行为(严格实现)

### 简单 AI
1. 持续生产:每个决策帧,为己方每个兵营(Building==Barracks && Prod!=null)的**空闲槽补满队列**:只产 1 人口英雄(磐石,若人口不足则等待);绝不产镇岳。
2. 不买升级、不抽海克斯、不主动占中立、不下达攻城意图。
3. 被动回防(唯一反应):若 VisionSystem 显示**敌方单位出现在己方任意建筑 8 格内**(建筑格 8 格内存在 IsUnitVisible 的敌单位),则把**离该建筑最近的若干空闲己方单位**(State==Idle、GarrisonBuildingId==0,至多 5 只)SetMoveTarget 到该建筑前方 1 格(行序 1/0/2 找空位)。
4. 出兵方向:依赖生产系统默认集结点(蓝 (1,29)/红 (1,0)),不需要额外逻辑。

### 普通 AI
每个决策帧依次执行(全部可选动作,一次最多执行一个"大动作",优先级从上到下):
1. **补产**:同简单,但产兵配比:**每产 3 只 1 人口英雄(磐石/断峰交替),下一只必为镇岳**(人口足够时);镇岳升级已买时镇岳产速自然变快。
2. **升级**:EP≥2 且镇岳未升级 → BuyUpgrade(镇岳)。
3. **规律抽卡**:tick≥1800 且 HexSystem.CanDraw → DrawChoices 三张中按固定优先级选一张 UseHex(优先级:SteelTide > RapidDeploy > HoldFast > Vengeance > LightMobility > IronWall > HiddenPath > Sandbag;沙包目标传 0 会失败,跳过沙包)。
4. **集结进攻**(核心):无进行中波次时,统计己方野战单位(非驻军、State!=Dead,含 Exchanging 除外)总数 ≥ 8 只 → 启动波次:
   - 目标选择(按优先级):
     a. 若存在中立资源点(Building==ResourcePoint && Side==Neutral && !Rubbled,己方可见):选它,Intent=Capture;
     b. 否则在**已知**的敌方建筑中(VisionSystem.IsBuildingKnown)选 LastKnownGarrison 最小者(平局取距己方最近者;敌方大本营排除);
        该建筑为 Tower → Intent=Destroy;否则 → Intent=Capture;
   - 执行:对波次单位(至多 10 只,优先距目标近者)调用 SiegeSystem.SetIntent(目标,己方,Intent) 与 SiegeSystem.IssueOrder(波次单位,目标)。
   - 波次状态记录:参与单位 id 列表(静态内部字典);单位阵亡/离场自动从列表剔除。
5. **进攻受挫撤退**:波次进行中,若波次存活单位 < 启动时的 40% → 撤销波次:剩余单位 SetMoveTarget 回己方大本营前 1 格,清空波次记录。
6. **主动防守**:若 VisionSystem 显示**可见敌单位总数 ≥ 6 且集中在己方某建筑 10 格内** → 将离该建筑最近的空闲己方单位(至多 6 只)调往该建筑:若建筑 CanGarrison 且驻军未满 → EnterFormal;否则 SetMoveTarget 到建筑前方 1 格。

## 四、给定 API(已实现,直接调用,勿改;签名一行一条)

```csharp
// SimCore
long Tick; GameConfig Game; IReadOnlyDictionary<int, Entity> Entities; Entity GetEntity(int id);
int PopulationOf(Side side); int PopulationCapOf(Side side); float EpOf(Side side);
bool BuyUpgrade(Side side, HeroId hero, out string error); bool IsUpgraded(Side side, HeroId hero);
void SetMoveTarget(int id, GridPos target); void SetRallyPoint(Side side, GridPos p);
// Entity 关键字段
Side Side; EntityKind Kind; BuildingId? Building; HeroId? Hero; GridPos Pos; EntityState State;
int GarrisonBuildingId; bool Rubbled; bool IsFort; int TargetBuildingId; List<int> GarrisonFormal; List<int> GarrisonTemp;
ProductionSet Prod;   // 兵营生产队列(ProductionSet.Slots:Slot.Current/Pending;HasWork)
// ProductionSystem
bool Enqueue(SimCore sim, int barracksId, HeroId hero, out string error);
// SiegeSystem
bool SetIntent(SimCore sim, int buildingId, Side issuer, SiegeIntent intent, out string error);
bool IssueOrder(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error);
// HexSystem
bool CanDraw(SimCore sim, Side side); List<HexId> DrawChoices(SimCore sim, Side side);
bool UseHex(SimCore sim, Side side, HexId card, int targetUnitId, out string error);
// VisionSystem(敌方信息唯一入口)
bool IsUnitVisible(SimCore sim, Side side, int unitId);
bool IsBuildingKnown(SimCore sim, Side side, int buildingId);
int? LastKnownGarrison(SimCore sim, Side side, int buildingId);
bool IsFresh(SimCore sim, Side side, GridPos cell);
// GarrisonSystem
bool EnterFormal(SimCore sim, IReadOnlyList<int> unitIds, int buildingId, out string error);
```
枚举全表:Side=Blue/Red/Neutral;HeroId=PanShi/ZhenYue/DuanFeng/ZhuFeng/ZhuiRi/XingLuo;BuildingId=Base/Barracks/ResourcePoint/Tower;SiegeIntent=None/Capture/Destroy;EntityState=Idle/Moving/Attacking/Exchanging/Routing/Dead;HexId=SteelTide/RapidDeploy/Vengeance/LightMobility/IronWall/HoldFast/HiddenPath/Sandbag。GridPos=(行,列),行0..2,列0..29。

## 五、接口契约

### AiSystem.cs(`namespace IronCurtain.Sim`,静态类)
```csharp
public enum AiTier { Simple, Normal }
public static void Configure(SimCore sim, Side side, AiTier tier); // 登记该方 AI(重复调用覆盖)
public static void Reset();                                        // 清空全部(测试用)
public static void Tick(SimCore sim, float dt);
// 每 20 tick 对已登记的每个 AI 执行一次决策(内部累加器,精确按 sim.Tick % 20 == 0 触发);
// 敌方信息只经 VisionSystem;己方信息直读;全部指令走玩家接口
```
内部结构自由(波次状态等静态字典),但必须满足:无波次时普通 AI 不重复下达同一意图;简单 AI 永不调用 UseHex/BuyUpgrade/SetIntent。

### AiTests.cs(EditMode,[Test] 10 个)
SetUp 照抄既有模板;地图:蓝大本营 (1,0)、蓝兵营 (1,2)+Prod、蓝塔 (1,6);红大本营 (1,28)、红兵营 (1,26)+Prod、红塔 (1,22);中立资源点 (1,14)。给蓝方登记 Simple 或 Normal。
1. 简单满产:登记 Simple,跑 25 tick → 蓝兵营队列非空(有 Pending 或 Current)
2. 简单不越权:给足 EP(2 资源点+900 tick)再跑 60 tick → 无升级(IsUpgraded false)、无海克斯(ActiveOf null)、无攻城意图(红塔 Intent==None)
3. 简单被动回防:红单位放 (1,8)(蓝塔 8 格内)→ 决策帧后蓝空闲单位有 MoveTarget 指向塔前
4. 普通补产镇岳:登记 Normal,跑 25 tick → 蓝兵营队列含镇岳(Pending 或 Current 中出现 HeroId.ZhenYue)
5. 普通升级:给蓝 2EP(资源点 900 tick),登记 Normal → 决策帧后 IsUpgraded(蓝,镇岳)==true
6. 普通规律抽卡:tick 推进到 1800+ 且 EP≥2(提前给足),登记 Normal → 决策帧后 HexSystem.ActiveOf(蓝)!=null
7. 普通集结:蓝 7 只野战单位 → 无攻城意图;补到 8 只 → 下一决策帧出现对某目标的 SetIntent(蓝方 Intent 非 None)
8. 普通挑弱侧:两个已知红建筑(红塔驻军 1、红兵营驻军 5,均已通过 VisionSystem 揭示)→ 集结后目标为驻军 1 的那个
9. 普通占中立优先:中立资源点存在且未占 → 集结后首个意图目标为中立资源点且 Intent==Capture
10. 普通主动防守:红 8 只单位推进到蓝塔 10 格内且可见 → 决策帧后蓝空闲单位被调往塔(EnterFormal 或 MoveTarget)

## 六、硬性验收
1. 只输出两个文件;单文件 ≤300 行(AiSystem 可同文件多辅助类)
2. Sim 层禁止 UnityEngine;测试层照抄模板
3. 120 旧测试 + 10 新测试全绿
4. 敌方信息只能经 VisionSystem 查询(代码评审点:搜索 sim.Entities 中敌方 Side 的直读仅允许枚举己方过滤)
5. 断言带中文消息

## 七、输出格式
只输出文件,禁止解释、禁止 Markdown 围栏。以 `// FILE: <路径>` 开头接全文。两个文件一次给全。
