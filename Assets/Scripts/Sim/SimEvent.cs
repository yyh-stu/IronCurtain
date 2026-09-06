namespace IronCurtain.Sim
{
    public enum SimEventType
    {
        StepBegin,
        StepEnd,
        EntitySpawned,
        EntityRemoved,
        CommandExecuted,
        UnitAdvanced,
        DamageDealt,
        EntityDied
    }

    public struct SimEvent
    {
        public long Tick;
        public SimEventType Type;
        public int SubjectId;
        public float Amount;

        public SimEvent(long tick, SimEventType type, int subjectId, float amount = 0f)
        {
            Tick = tick;
            Type = type;
            SubjectId = subjectId;
            Amount = amount;
        }

        public override string ToString() => $"[{Tick}] {Type} #{SubjectId}";
    }
}
