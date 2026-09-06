using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    public sealed class ProductionSet
    {
        public const int QueueCount = 3;

        public sealed class Slot
        {
            public HeroId? Current;
            public float Progress;

            /// <summary>生产已完成但出生点被占,等待腾位中(GL M 方案,核实后采纳)。</summary>
            public bool WaitingForSpawn;

            public readonly Queue<HeroId> Pending = new Queue<HeroId>();
        }

        public readonly Slot[] Slots;

        public ProductionSet()
        {
            Slots = new Slot[QueueCount];
            for (int i = 0; i < QueueCount; i++) Slots[i] = new Slot();
        }

        public bool HasWork
        {
            get
            {
                for (int i = 0; i < Slots.Length; i++)
                {
                    if (Slots[i].Current != null) return true;
                    if (Slots[i].Pending.Count > 0) return true;
                }
                return false;
            }
        }

        public int InProgressPop(Func<HeroId, int> popOf)
        {
            int sum = 0;
            for (int i = 0; i < Slots.Length; i++)
            {
                var cur = Slots[i].Current;
                if (cur.HasValue) sum += popOf(cur.Value);
            }
            return sum;
        }

        public void Clear()
        {
            for (int i = 0; i < Slots.Length; i++)
            {
                Slots[i].Current = null;
                Slots[i].Progress = 0f;
                Slots[i].WaitingForSpawn = false;
                Slots[i].Pending.Clear();
            }
        }
    }
}
