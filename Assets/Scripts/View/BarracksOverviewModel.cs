using System.Collections.Generic;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    /// <summary>
    /// 兵营总览模型(M8.4c 改版):区块=己方主基地(将生产);补兵池接口。
    /// 纯逻辑,无 UnityEngine 依赖。只读 Sim;生产走 ProductionSystem、补兵走 ReinforceSystem 公开 API。
    /// 边界态:被摧毁(Destroyed)、被敌方占领(按 Side 过滤自然消失)。
    /// </summary>
    public sealed class BarracksOverviewModel
    {
        public enum BlockState { Producing, Destroyed }

        public struct SlotInfo
        {
            public HeroId? Current;      // 进行中英雄(无=空闲)
            public float Progress;       // 已进行秒数
            public float TotalSeconds;   // 该英雄生产总时长(展示用)
            public bool WaitingForSpawn; // 等待出生点/名额
            public int PendingCount;     // 待产队列长度
        }

        public struct BlockInfo
        {
            public int BarracksId;
            public BlockState State;
            public SlotInfo[] Slots;     // 恒 3 槽
        }

        private readonly SimCore _sim;
        private readonly Side _player;

        public BarracksOverviewModel(SimCore sim, Side player)
        {
            _sim = sim;
            _player = player;
        }

        public List<BlockInfo> DescribeBlocks()
        {
            var list = new List<BlockInfo>();
            foreach (var e in _sim.Entities.Values)
            {
                if (e.Kind != EntityKind.Building) continue;
                if (e.Building != BuildingId.Base) continue; // M8.4c:区块=主基地
                if (e.Side != _player) continue; // 敌方/中立/易主后自然消失由 Side 过滤保证(废墟仍保留显示)

                var block = new BlockInfo { BarracksId = e.Id, Slots = new SlotInfo[3] };
                if (e.Rubbled)
                {
                    block.State = BlockState.Destroyed;
                    for (int i = 0; i < 3; i++)
                        block.Slots[i] = new SlotInfo { TotalSeconds = 0f };
                }
                else
                {
                    block.State = BlockState.Producing;
                    var prod = e.Prod;
                    for (int i = 0; i < 3; i++)
                    {
                        SlotInfo info = default;
                        if (prod != null)
                        {
                            var slot = prod.Slots[i];
                            info.Current = slot.Current;
                            info.Progress = slot.Progress;
                            info.WaitingForSpawn = slot.WaitingForSpawn;
                            info.PendingCount = slot.Pending.Count;
                            if (slot.Current.HasValue)
                                info.TotalSeconds = TotalSecondsOf(e, slot.Current.Value);
                        }
                        block.Slots[i] = info;
                    }
                }
                list.Add(block);
            }
            return list;
        }

        public string StartProduction(int buildingId, HeroId hero)
        {
            if (!_sim.Entities.TryGetValue(buildingId, out var b) ||
                b.Kind != EntityKind.Building || b.Building != BuildingId.Base || b.Side != _player)
                return "目标不是主基地";
            if (b.Rubbled) return "主基地已摧毁,不可生产";
            if (b.Prod == null) return "主基地无生产集";
            return ProductionSystem.Enqueue(_sim, buildingId, hero, out _) ? null : "入队失败";
        }

        public string Cancel(int buildingId, int slotIndex)
        {
            if (!_sim.Entities.TryGetValue(buildingId, out var b) ||
                b.Kind != EntityKind.Building || b.Building != BuildingId.Base || b.Side != _player)
                return "目标不是主基地";
            if (b.Rubbled) return "主基地已摧毁,不可生产";
            if (b.Prod == null) return "主基地无生产集";
            return ProductionSystem.CancelLast(_sim, buildingId, slotIndex, out _) ? null : "队列为空";
        }

        /// <summary>补兵池当前存量。</summary>
        public int ReinforcePoolOf() { return ReinforceSystem.PoolOf(_sim, _player); }

        /// <summary>补兵池上限。</summary>
        public int ReinforcePoolCap() { return ReinforceSystem.PoolCap; }

        /// <summary>请求补兵(SJF);成功返回 null,失败返回错误文案。</summary>
        public string RequestReinforce(out string error)
        {
            if (ReinforceSystem.RequestReinforce(_sim, _player, out error)) return null;
            return error;
        }

        /// <summary>已用名额=存活主将数。</summary>
        public int PopulationOf() { return _sim.PopulationOf(_player); }

        /// <summary>名额上限。</summary>
        public int PopulationCapOf() { return _sim.PopulationCapOf(_player); }

        /// <summary>将产出时长(M8.4b:20s/将,恒定)。</summary>
        private float TotalSecondsOf(Entity baseB, HeroId hero)
        {
            return ProductionSystem.GeneralProductionSeconds;
        }
    }
}
