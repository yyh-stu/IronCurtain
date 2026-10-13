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
        WallBroken,
        // 2026-09-17 特效契约(异构审核 R1→R2 批准,见 docs/VFX_PLAN_20260917/M0_presentation-contract.md §3):
        // SettledDamage:结算后摘要。SubjectId=受害者;ShieldBefore/After+HpBefore/After 为本次结算前后快照,
        //   Died=本 tick 因此死亡。发出点=ApplyStagedDamage 施伤循环内、逐受害者判死检查之后(先于 EntityDied 派发)。
        //   盾血均无净变化不发(职责分工:被打但没掉血的可视化由 DamageDealt 承担)。
        // FortPulsed:地堡脉冲。SubjectId=堡垒实体 id;无 Amount/受害者(脉冲伤害路径不变,不经聚合,
        //   与 SettledDamage 互不覆盖——两者口径已在契约文档钉死)。
        SettledDamage,
        FortPulsed,
        // 2026-09-20 GPT④:EP 变化事件。SubjectId=(int)Side, Amount=变化量(正=获得,负=消耗)。
        // View 层消费此事件替代轮询(轮询会漏掉"先花后补、净变=0"的情况)。
        EpChanged
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
        // 2026-09-17 SettledDamage 专用(其余事件为零值):结算前后快照 + 死亡标记
        public float ShieldBefore, ShieldAfter, HpBefore, HpAfter;
        public bool Died;

        public float AbsorbedByShield => ShieldBefore - ShieldAfter;

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
            ShieldBefore = ShieldAfter = HpBefore = HpAfter = 0f;
            Died = false;
        }

        public override string ToString() => $"[{Tick}] {Type} #{SubjectId}";
    }
}
