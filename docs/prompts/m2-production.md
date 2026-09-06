# M2 生产与资源 —— 生产队列/人口/EP/自动出兵(GL M 绿场实现+测试)

## 一、角色与任务
你是《铁幕:前线》Unity 6 项目的经济系统主程。编排者已把 SimCore 的经济挂钩写好(见第三节),你的任务是**只新增三个文件**:`ProductionSet.cs`(队列数据结构)、`ProductionSystem.cs`(生产/EP/自动行军逻辑)、`ProductionTests.cs`(EditMode 测试)。**禁止修改任何既有文件**;既有 42 个测试必须保持全绿。

## 二、GDD 规则(唯一权威)
- 生产:1 人口单位 6 秒,镇岳 2 人口 9 秒(升级后 7.5 秒属 M4.1,本模块不做);单兵营最多 3 条生产队列;队列先进先出、支持混合排队;仅能从末尾取消,取消返还人口、已耗时间不补偿;无法修改中间顺序;启动前校验剩余人口,人口不足队列暂停,阵亡释放后自动恢复;资源点丢失致上限下降,已产兵团保留、队列强制暂停;兵营被摧毁清空队列、返还人口;全局人口达上限暂停生产
- 人口:大本营 +40;每座资源点 +5;初始 55(己方 3 资源点 +15)
- EP:上限 5,初始 0;每座己方资源点每 90 秒 +1;本模块只做累积与消耗接口,消耗场景(M4)后接
- 节奏断言(GDD 原文):三兵营全产 1 人口英雄,拉满 55 人口约 110 秒

## 三、给定 API(已实现,直接调用,勿改)

```csharp
// SimCore(实例方法)
Entity SpawnBuilding(Side owner, BuildingId type, GridPos pos); // 建筑实体,自动设置 e.Building
int OwnedResourcePointsOf(Side side);      // 按场上资源点建筑实况统计
int PopCostOf(HeroId id);                  // Game 配置的人口占用(磐石等 1,镇岳 2)
int PopulationOf(Side side);               // 场上单位人口 + 兵营进行中生产人口(调 e.Prod.InProgressPop)
int PopulationCapOf(Side side);            // 40 + 5×资源点数
float EpOf(Side side);
bool SpendEp(Side side, float amount);     // 余额不足返回 false
GridPos RallyPointOf(Side side);           // 默认 Blue=(1,29), Red=(1,0)
void SetRallyPoint(Side side, GridPos p);
Entity SpawnEntity(Side, EntityKind, GridPos); // 占用格冲突抛 InvalidOperationException
bool RemoveEntity(int id);
void SetMoveTarget(int id, GridPos target);    // 非单位/越界抛异常
long Tick;                                      // 10 tick/s,Step() 内先 Tick++ 后依次 MoveUnits/CombatUpdate/EconomyUpdate
GameConfig Game;                                // 数值配置
IReadOnlyDictionary<int, Entity> Entities;
```
Entity 关键字段:`Hero(HeroId?)`、`Building(BuildingId?)`、`Prod(ProductionSet,仅兵营非空)`、`Pos`、`Side`、`Kind`、`State`、`Hp/MaxHp/Attack/AttackIntervalSec/AttackCooldown/Range/MoveSpeed/HasMoveTarget/MoveTarget/MoveAccum`。
配置(GameConfig):
- `Heroes[6]`:Id/MaxHp(int)/Attack(int)/AttackIntervalSec(double)/MoveSpeed(double)/ChargeSpeed(double)/Range(int)/Population(int)/Shield(int)
- `Production`:Pop1Seconds(double)=6、ZhenYueSeconds(double)=9、QueuesPerBarracks(int)=3、BasePopulation(int)=40、ResourcePointPopulation(int)=5
- `Ep`:Cap(int)=5、IntervalSeconds(double)=90

生成英雄单位的标准写法(照抄此模式,double→float 必须显式转换):
```csharp
var c = sim.Game.Heroes[(int)heroId];
var u = sim.SpawnEntity(side, EntityKind.Unit, pos);
u.Hero = heroId; u.MaxHp = c.MaxHp; u.Hp = c.MaxHp;
u.Attack = c.Attack; u.AttackIntervalSec = (float)c.AttackIntervalSec;
u.Range = c.Range; u.MoveSpeed = (float)c.MoveSpeed;
if (c.Shield > 0) { u.Shield = c.Shield; u.ShieldMax = c.Shield; }
```

## 四、接口契约(严格实现)

### ProductionSet.cs(`namespace IronCurtain.Sim`)
```csharp
public sealed class ProductionSet
{
    public const int QueueCount = 3;
    public sealed class Slot
    {
        public HeroId? Current;        // 进行中英雄,null=空闲
        public float Progress;         // 0..生产秒数
        public readonly Queue<HeroId> Pending; // 待产队列(队首=下一个)
    }
    public readonly Slot[] Slots;      // 恰 3 个
    public int InProgressPop { get; }  // 所有 Current 的人口之和(由 SimCore 传入 HeroId→pop 的函数?不行——用静态回调注入:见下)
    // 建议:InProgressPop 由 ProductionSystem 提供计算,ProductionSet 存纯数据;此处要求只暴露 Slots 与数据访问,不自行读配置
    public bool HasWork { get; }       // 任意槽 Current!=null 或 Pending.Count>0
}
```
说明:人口求和需要配置,`InProgressPop` 请实现为 `public int InProgressPop(Func<HeroId,int> popOf)`(调用方传 sim.PopCostOf)。如你认为更优雅,可用 `public int InProgressPop(SimCore sim)`,但**禁止 ProductionSet 直接持有 GameConfig 引用**。

### ProductionSystem.cs(静态类)
```csharp
public static class ProductionSystem
{
    public static void Tick(SimCore sim, float dt);
    // 遍历所有兵营建筑(Kind==Building && Building==Barracks),每 tick:
    //   1) 空闲槽(Slot.Current==null)且 Pending 非空 → 取出队首:人口校验(场上单位+其余进行中 pop + 本英雄 pop ≤ PopulationCapOf)通过才开工,否则留队暂停
    //   2) 进行中槽:Progress += dt;达到生产秒数(1人口用 Pop1Seconds,镇岳用 ZhenYueSeconds)→ 完成:
    //      在兵营前方(朝敌方方向,Blue=col+1,Red=col-1)找空位出生(行序 1,0,2,同列找完向更远列延伸),全部被占则 Progress 停在满值等待
    //      出生成功:按第三节标准写法生成英雄单位 → SetMoveTarget(新单位, sim.RallyPointOf(ownerSide))
    public static bool Enqueue(SimCore sim, int barracksId, HeroId hero, out string error);
    // 兵营不存在/非兵营 → error;加入"待产项最少"的槽(并列取索引小者);成功返回 true
    public static bool CancelLast(SimCore sim, int barracksId, int slotIndex, out string error);
    // 指定槽:若 Pending 非空 → 移除队尾,error 为空;若 Pending 空但 Current!=null → 取消进行中(清空 Current/Progress),人口随之释放(人口是派生值,取消即释放,无需显式操作);两者皆空 → error="队列为空"
    public static void OnBarracksDestroyed(SimCore sim, Entity barracks);
    // 兵营被毁/易主:清空该实体 Prod 全部槽(人口派生值自动释放)
}
```

### ProductionTests.cs(EditMode,[Test] 10 个)
场景布局常量(与 M3 计划一致,直接在测试里用):Blue 兵营 (1,2)、Red 兵营 (1,26);Blue 资源点 (1,4)、(1,14 中立未占)、(1,24 敌);测试自建所需建筑:`sim.SpawnBuilding(side, BuildingId.Barracks, pos)` 后须 `barracks.Prod = new ProductionSet();`。
1. 单兵营节奏:Enqueue 磐石×5 → 每 60 tick 出 1 只;第 1 只于 tick 60 后存在(EntityCount 检查),600 tick 内 5 只全出
2. 镇岳生产:Enqueue 镇岳 → 90 tick 产出;PopulationOf 含 2(进行中即占用)
3. 三队列并行:同兵营 3 槽各入 1 只磐石 → 60 tick 后场上 3 只(并行非串行)
4. 队尾取消:槽内 [磐石,断峰] → CancelLast → 只剩磐石会在后续产出;取消进行中(CancelLast 于 Current 进行中且 Pending 空)→ 该槽停止,产出数不增加
5. 人口上限暂停与恢复:置 cap 条件使开工被拒(如 cap 不足 2 人口时 Enqueue 镇岳)→ 队列保持 Pending 不产;解除(加资源点或降低需求,用 1 人口英雄测试)→ 恢复生产
6. 55 人口 110 秒(GDD 节奏):3 座 Blue 兵营(位置 (1,2)/(1,3)/(1,4)?——出生格冲突!改放 (0,2)/(1,2)/(2,2) 三行同列),各持续产 1 人口英雄;断言 1100 tick 左右产满 55 只(允许 ±20 tick 误差),之后因人口上限不再增产
7. EP 节奏:Blue 3 座资源点 → 900 tick 后 EpOf(Blue)==3;再续 900 tick ==5(封顶),继续不涨
8. 出生点被占:兵营前方 3 格(行 1,0,2 同列)全占满 → 生产完成但单位不出,Progress 停满;腾出一格 → 下 tick 出生
9. 自动行军:Enqueue 并产出后,新单位 HasMoveTarget==true 且 MoveTarget==RallyPointOf(side)
10. 兵营摧毁清队列:入队若干后 OnBarracksDestroyed → Prod 无任何 Current/Pending;PopulationOf 不再计入

## 五、硬性验收
1. 只输出三个文件,禁止改其他文件;单文件 ≤300 行
2. Sim 层禁止 UnityEngine;测试层允许(Resources 读 gameconfig 的 SetUp 模式照抄既有测试)
3. 全部测试编译且通过(连同既有 42 个)
4. 生产逻辑必须纯 tick 驱动,禁止 Time/随机
5. 断言带中文失败消息

## 六、输出格式
只输出文件,禁止解释、禁止 Markdown 围栏。每个文件以 `// FILE: <相对项目根路径>` 开头后接完整内容。三个文件一次给全。
