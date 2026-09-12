using System.Collections.Generic;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    /// <summary>BP 选将(2026-09-13 制作人需求+修正):开始游戏后先选将再进战斗。
    /// 规则:①双方各选 3 名,3 轮、每轮同时选 1 名、限时 10s;②选取期间互不可见对方选择,
    /// 双方都锁定后**立即揭示并跳过剩余时间**(超时自动补选),揭示 3s 后进下一轮;
    /// ③"允许重复"指蓝红双方可以选中同一英雄,但**同一方 3 轮内不可重复**;
    /// ④AI 选将遵循"重装/战士/射手各选一个"的角色覆盖原则。
    /// 纯逻辑类(只依赖 System),计时由外部喂 dt,便于 EditMode 确定性测试。</summary>
    public sealed class BpSession
    {
        public const int Rounds = 3;
        public const float PickSeconds = 10f;
        public const float RevealSeconds = 3f;

        public enum Phase { Picking, Reveal, Done }

        public Phase State = Phase.Picking;
        public int Round;                        // 0 起
        public float Timer = PickSeconds;
        public readonly List<HeroId> Blue = new List<HeroId>();
        public readonly List<HeroId> Red = new List<HeroId>();
        public HeroId? BlueLock;                 // 本轮蓝方锁定(仅蓝方可见;红方对 UI 不可见,见规则②)

        /// <summary>本轮揭示结果(双方锁定的瞬间写入,UI 据此展示)。</summary>
        public HeroId RevealedBlue, RevealedRed;

        private readonly System.Random _rng;
        private float _aiPickAt;                 // 红方 AI 拟锁时刻(本轮剩余时间)
        private bool _aiPicked;
        private HeroId? _aiPlan;                 // AI 本轮拟选(提前定好,到点才锁)

        public BpSession(int seed)
        {
            _rng = new System.Random(seed);
            RollAiPlan();
        }

        /// <summary>蓝方点击英雄=立即锁定;重复选已选过的英雄被拒绝(规则③);双方锁定即揭示(规则②)。</summary>
        public bool PickBlue(HeroId hero)
        {
            if (State != Phase.Picking || BlueLock.HasValue || Blue.Contains(hero)) return false;
            BlueLock = hero;
            TryEnterReveal();
            return true;
        }

        /// <summary>推进计时。Picking:AI 在拟锁时刻锁定;超时为未锁方自动补选(同样遵循规则③④);
        /// Reveal:3s 后进下一轮或 Done。</summary>
        public void Tick(float dt)
        {
            if (State == Phase.Done) return;
            Timer -= dt;
            if (State == Phase.Picking)
            {
                if (!_aiPicked && Timer <= _aiPickAt)
                {
                    _aiPicked = true;
                    TryEnterReveal();
                }
                if (Timer <= 0f)
                {
                    if (!BlueLock.HasValue) BlueLock = AutoPick(Blue);
                    TryEnterReveal(true);
                }
            }
            else if (State == Phase.Reveal && Timer <= 0f)
            {
                Round++;
                if (Round >= Rounds) { State = Phase.Done; return; }
                State = Phase.Picking;
                Timer = PickSeconds;
                BlueLock = null;
                _aiPicked = false;
                RollAiPlan();
            }
        }

        /// <summary>双方锁定即进揭示(跳过剩余时间,规则②);force=超时路径(AI 侧由 AutoPick 兜底)。</summary>
        private void TryEnterReveal(bool force = false)
        {
            var red = _aiPicked || force ? (_aiPlan ?? AutoPick(Red)) : (HeroId?)null;
            if (!BlueLock.HasValue || !red.HasValue) return;
            RevealedBlue = BlueLock.Value;
            RevealedRed = red.Value;
            Blue.Add(BlueLock.Value);
            Red.Add(red.Value);
            State = Phase.Reveal;
            Timer = RevealSeconds;
        }

        /// <summary>角色覆盖选将(规则④):从"尚未选过的定位"里挑;该方已选过的英雄排除(规则③)。
        /// 已覆盖全部定位时(理论不会发生,3 轮=3 类),回退为未选英雄随机。</summary>
        private HeroId AutoPick(List<HeroId> taken)
        {
            var missing = new List<string>();
            foreach (var role in new[] { "重装", "战士", "射手" })
                if (!taken.Exists(h => RoleOf(h) == role)) missing.Add(role);
            var pool = new List<HeroId>();
            foreach (var h in All)
            {
                if (taken.Contains(h)) continue;
                if (missing.Count > 0 && !missing.Contains(RoleOf(h))) continue;
                pool.Add(h);
            }
            return pool.Count > 0 ? pool[_rng.Next(pool.Count)] : All[_rng.Next(All.Length)];
        }

        private void RollAiPlan()
        {
            _aiPlan = AutoPick(Red);
            _aiPickAt = 2f + (float)_rng.NextDouble() * 6f;   // 剩余 2~8s 时锁定
        }

        public static string RoleOf(HeroId h)
        {
            switch (h)
            {
                case HeroId.PanShi:
                case HeroId.ZhenYue: return "重装";
                case HeroId.DuanFeng:
                case HeroId.ZhuFeng: return "战士";
                default: return "射手";
            }
        }

        private static readonly HeroId[] All =
            { HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo };
    }
}
