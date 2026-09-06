# M2 修复轮 —— 你上一轮 ProductionSystem/ProductionTests 有 4 个具体 bug,逐一修复

## 一、任务
只修改 `Assets/Scripts/Sim/ProductionSystem.cs` 和 `Assets/Tests/EditMode/ProductionTests.cs` 两个文件(输出全文)。ProductionSet.cs 与所有其他文件**禁止改动**。修复后 10 个生产测试 + 既有 42 个测试必须全绿。

## 二、四个 bug 与修法(实测证据)

### Bug1(致命,引擎):生成单位时"Collection was modified"
`Tick()` 的 `foreach (var e in sim.Entities.Values)` 遍历期间,`TrySpawnUnit` 调用 `sim.SpawnEntity` 往同一字典添加实体 → 抛 InvalidOperationException(T2/T6/T8/T9 全挂在这)。
**修法**:Tick 开头把实体快照到局部数组/列表(`var list = new List<Entity>(sim.Entities.Values);`),遍历快照;出生与 SetMoveTarget 都在快照循环内安全。

### Bug2(引擎):0.1f 浮点累积导致"到点不出兵"
`slot.Progress += dt`(dt=0.1f)累积 60 次约 5.9999995,`Progress >= (float)need` 在 tick 60 判定失败(T1 实测:Expected 1 But was 0)。
**修法**:改用"整数 tick 计数"或余量比较。推荐:Slot 增加 `float Progress`,比较改为 `slot.Progress + dt >= need`(先判定后累加),或 `if (slot.Progress >= need - 1e-4f)`。**必须保证 6.0s 恰在 60 tick 产出、9.0s 恰在 90 tick 产出**。

### Bug3(测试逻辑):T4 与"最短队列"分配策略自相矛盾
你的 Enqueue 把第一只磐石放进 slot0、第二只断峰放进 slot1(最少待产项)。但 T4 断言 `Slots[0].Pending.Count == 1` 且取消 slot0 后"只应产出磐石"——与分配策略冲突。
**修法**:T4 改为:入队两只后断言 `Slots[0].Pending.Count==1 && Slots[1].Pending.Count==1`;`CancelLast(sim, b.Id, 0, out _)` 取消 slot0 的磐石 → 断言 slot0 空;Step(60) 后只产出断峰 1 只;再对 slot1 `CancelLast` 取消进行中 → 断言不再增产。取消进行中的断言照旧(Current 为空、产量不变)。

### Bug4(测试逻辑):T5 铺垫单位越界
`new GridPos(3, 5 + i % 20)` 行号 3 越界(有效行 0–2)→ "位置越界" 异常。
**修法**:39 只 1 人口单位放在合法格且互不冲突,如 `new GridPos(i % 3, 2 + i / 3)`(行 0–2 × 列 2–14,共 39 格)。后续断言不变。

## 三、验收
1. 只输出上述两个文件全文(// FILE: 标记)
2. 10 个生产测试 + 既有 42 个测试全绿
3. Bug2 的修复必须满足:GDD 节奏 60 tick 出 1 人口、90 tick 出镇岳,三兵营 55 人口 110 秒(T6)在 ±20 tick 容差内
4. 其余契约(队列语义/人口暂停恢复/EP 封顶/出生点占用等待/自动行军/摧毁清队列)保持上一轮实现不动,只修这 4 个 bug

## 四、输出格式
只输出文件,禁止解释、禁止 Markdown 围栏。
