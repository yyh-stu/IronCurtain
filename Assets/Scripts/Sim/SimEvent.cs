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
        MatchEnded,      // M6.4a 对局结束(摧毁胜利或积分判定;SubjectId=(int)Winner,-1=平局)
        // 2026-09-11 屏障特效:SubjectId=段号(0-4),WallSide=守方,AttackerId=攻击者,Amount=伤害(WallDamaged)
        WallDamaged,
        WallBroken
    }

    public struct SimEvent
    {
        public long Tick;
        public SimEventType Type;
        public int SubjectId;
        public float Amount;
        public int AttackerId; // 2026-09-09 弹道表现:DamageDealt 的攻击者(0=无/塔/ unspecified)
        public int WallSide;   // 2026-09-11 屏障特效:WallDamaged/WallBroken 的守方((int)Side,0蓝/1红)
        public SimVec2 HitPos; // 2026-09-11 屏障特效:WallDamaged 的命中点(Sim 坐标)

        public SimEvent(long tick, SimEventType type, int subjectId, float amount = 0f, int attackerId = 0,
            int wallSide = 0, SimVec2 hitPos = default)
        {
            Tick = tick;
            Type = type;
            SubjectId = subjectId;
            Amount = amount;
            AttackerId = attackerId;
            WallSide = wallSide;
            HitPos = hitPos;
        }

        public override string ToString() => $"[{Tick}] {Type} #{SubjectId}";
    }
}
