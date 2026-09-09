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
        public int AttackerId; // 2026-09-09 弹道表现:DamageDealt 的攻击者(0=无/塔/ unspecified)

        public SimEvent(long tick, SimEventType type, int subjectId, float amount = 0f, int attackerId = 0)
        {
            Tick = tick;
            Type = type;
            SubjectId = subjectId;
            Amount = amount;
            AttackerId = attackerId;
        }

        public override string ToString() => $"[{Tick}] {Type} #{SubjectId}";
    }
}
