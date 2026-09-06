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
        EntityDied,
        OvertimeStarted, // M6.4a 常规时限到,进入加时
        MatchEnded       // M6.4a 对局结束(摧毁胜利或积分判定;SubjectId=(int)Winner,-1=平局)
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
