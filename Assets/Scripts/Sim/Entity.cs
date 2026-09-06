using System;

namespace IronCurtain.Sim
{
    public enum Side { Blue = 0, Red = 1, Neutral = 2 }

    public enum EntityKind { Unit, Building }

    public enum EntityState { Idle, Moving, Attacking, Exchanging, Routing, Dead }

    public enum SiegeIntent { None = 0, Capture = 1, Destroy = 2 }

    public sealed class Entity
    {
        public int Id;
        public Side Side;
        public EntityKind Kind;
        public GridPos Pos;
        public float Hp;

        /// <summary>连续位置(米)。单位的移动权威;建筑保持 Pos 格中心语义(由调用方按需转换)。</summary>
        public SimVec2 PosF;

        // M8.5d 卡死自动绕行
        public SimVec2 PrevPosF;     // 上一 tick 位置(卡死检测)
        public int StuckTicks;       // 连续停滞 tick 数
        public SimVec2? Detour;      // 旁路点(null=直行)
        public int DetourTicks;      // 旁路进行 tick 数(超时保险)

        public HeroId? Hero;
        public BuildingId? Building;

        // 兵营生产队列(M2,仅兵营建筑非空)
        public ProductionSet Prod;

        // 攻城(M3)
        public bool Rubbled;         // 建筑:废墟状态(无阵营归属,需重建)
        public bool IsFort;          // 堡垒(海克斯/道具生成,无 BuildingId,走专用逻辑)
        public SiegeIntent Intent;   // 建筑:当前攻城意图(由进攻方下达)
        public Side IntentIssuer;    // 意图下达方
        public bool Sealed;          // 建筑:交换封城中
        public int TargetBuildingId; // 单位:攻城目标建筑(0=无)

        // 驻军(M3)
        public System.Collections.Generic.List<int> GarrisonFormal = new System.Collections.Generic.List<int>(); // 建筑:正式驻军(单位id)
        public System.Collections.Generic.List<int> GarrisonTemp = new System.Collections.Generic.List<int>();   // 建筑:临时驻军(单位id)
        public int GarrisonBuildingId; // 单位:所在建筑(0=未驻军)
        public int TempGarrisonId; // 城后等候归属(临时驻军登记,不冻结移动;0=无)

        // 移动(M1.1)
        public float MoveSpeed;
        public EntityState State = EntityState.Idle;
        public bool HasMoveTarget;
        public GridPos MoveTarget;
        public float MoveAccum;

        // 战斗(M1.2)
        public float MaxHp;
        public float Attack;
        public float AttackIntervalSec;
        public float AttackCooldown;
        public float Range;
        public float Shield;
        public float ShieldMax;

        // 冲锋标记(逐风,M1.3 驱动,伤害计算读取)
        public bool IsCharging;
        // 冲锋首击标记(拆塔首击 5 只结算一次;取消冲锋即重置)
        public bool ChargeHitUsed;

        // 状态效果(M1.3)
        public readonly StatusEffects Status = new StatusEffects();
        public float SecondsSinceCombat;

        // 兵团底座(M8.2a)
        public bool IsSoldier;      // 士兵:兵团成员,轻量 Unit
        public int GeneralId;       // 主将实体 id(0=无;主将死亡/移除后士兵无将续战)
        public int FormationSlot;   // 阵型槽号 0..19(士兵唯一;主将恒 0,无意义)

        // 兵团自主行为(M8.5b)
        public byte AutoKind;      // 自动决策类型:0=无,1=索敌进攻,2=支援
        public int AutoTargetId;   // 自动决策目标实体 id
        public byte SupKind;       // 被玩家打断而失效的自动决策(防复读)
        public int SupTargetId;

        // 布阵朝向覆盖(M8.5c 规划器设置;null=默认规则)
        public SimVec2? FacingOverride;

        // 补兵行军源(M8.6b:出生生产点;null=无行军表现,View 即时落位)
        public SimVec2? MarchOrigin;

        public Entity(int id, Side side, EntityKind kind, GridPos pos, float hp)
        {
            Id = id;
            Side = side;
            Kind = kind;
            Pos = pos;
            Hp = hp;
            // 单位出生即视为已脱战(补兵"脱战 3s"判定),建筑保持 0(受击回血规则不变)
            SecondsSinceCombat = kind == EntityKind.Unit ? 99f : 0f;
        }
    }
}
