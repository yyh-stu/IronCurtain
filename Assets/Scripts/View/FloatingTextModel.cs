using System.Collections.Generic;
using IronCurtain.Sim;

namespace IronCurtain.View
{
    public struct FloatItem
    {
        public int EntityId;
        public SimVec2 Origin;
        public string Text;
        public int ColorKind;
        public float Age;
    }

    /// <summary>M6.2 伤害飘字纯逻辑:可见性过滤 / 上限挤占 / 老化。禁 UnityEngine。</summary>
    public sealed class FloatingTextModel
    {
        public const float Life = 1.0f;
        public const int MaxActive = 40;

        public readonly List<FloatItem> Items = new List<FloatItem>();

        public bool TryAdd(SimCore sim, Side playerSide, int entityId, float amount)
        {
            var e = sim.GetEntity(entityId);
            if (e == null || e.State == EntityState.Dead) return false;
            if (e.Side != playerSide)
            {
                bool visible;
                if (e.Kind == EntityKind.Building)
                    visible = VisionSystem.IsBuildingKnown(sim, playerSide, entityId);
                else
                    visible = VisionSystem.IsUnitVisible(sim, playerSide, entityId);
                if (!visible) return false;
            }
            if (Items.Count >= MaxActive) Items.RemoveAt(0);
            var it = new FloatItem();
            it.EntityId = entityId;
            it.Origin = e.PosF;
            it.Text = DamageText(amount);
            it.ColorKind = e.Side == playerSide ? 0 : 1;
            it.Age = 0f;
            Items.Add(it);
            return true;
        }

        public void Advance(float dt)
        {
            for (int i = Items.Count - 1; i >= 0; i--)
            {
                var it = Items[i];
                it.Age += dt;
                if (it.Age > Life) Items.RemoveAt(i);
                else Items[i] = it;
            }
        }

        public static string DamageText(float amount)
        {
            return "-" + System.Math.Round(amount, System.MidpointRounding.AwayFromZero);
        }

        public static float RiseOffset(float age) { return age * 1.2f; }

        public static float ScaleOf(float age)
        {
            if (age <= 0.7f) return 1f;
            float t = (age - 0.7f) / 0.3f;
            return 1f - t;
        }
    }
}
