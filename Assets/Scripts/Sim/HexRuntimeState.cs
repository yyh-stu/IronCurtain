using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>Per-match authoritative state for hex draws, held cards and active effects.</summary>
    internal sealed class HexRuntimeState
    {
        internal sealed class SideState
        {
            public HexId? Active;
            public long ActiveExpiresTick;
            public long ActivatedTick;
            public readonly List<long> VengeanceExpiryTicks = new List<long>();
            public int SandbagUnitId;
            public long SandbagExpiresTick;
            public int UsedCardsMask;
            public int ConsumedNodesMask;
            public int PendingNodeMinute;
            public readonly List<HexId> PendingChoices = new List<HexId>();
            public readonly List<HexId> Hand = new List<HexId>();

            public void Clear()
            {
                Active = null;
                ActiveExpiresTick = 0;
                ActivatedTick = 0;
                VengeanceExpiryTicks.Clear();
                SandbagUnitId = 0;
                SandbagExpiresTick = 0;
                UsedCardsMask = 0;
                ConsumedNodesMask = 0;
                PendingNodeMinute = 0;
                PendingChoices.Clear();
                Hand.Clear();
            }
        }

        internal sealed class FortEntry
        {
            public int EntityId;
            public Side Side;
            public long ExpireTick;
            public long NextPulseTick;
        }

        public readonly SideState Blue = new SideState();
        public readonly SideState Red = new SideState();
        public readonly List<FortEntry> Forts = new List<FortEntry>();

        public SideState Of(Side side) => side == Side.Blue ? Blue : Red;

        public void Clear()
        {
            Blue.Clear();
            Red.Clear();
            Forts.Clear();
        }
    }
}
