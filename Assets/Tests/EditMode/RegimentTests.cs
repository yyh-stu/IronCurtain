using System.Collections.Generic;
using IronCurtain.Sim;
using NUnit.Framework;
using UnityEngine;

namespace IronCurtain.Tests
{
    public sealed class RegimentTests
    {
        private static GameConfig _cfg;

        private static SimCore NewSim()
        {
            if (_cfg == null)
                _cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig(), new SeededRng(1));
            sim.Game = _cfg;
            return sim;
        }

        private static List<Entity> SoldiersOf(SimCore sim)
        {
            var list = new List<Entity>();
            foreach (var e in sim.Entities.Values)
                if (e.Kind == EntityKind.Unit && e.IsSoldier) list.Add(e);
            return list;
        }

        [Test]
        public void T1_SpawnGeneral_FullRegiment()
        {
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));
            Assert.IsNotNull(g);
            Assert.IsFalse(g.IsSoldier);
            Assert.AreEqual(HeroId.PanShi, g.Hero.Value);

            var soldiers = SoldiersOf(sim);
            Assert.AreEqual(12, soldiers.Count, "12 名士兵(2026-09-10 制作人拍板)");

            var slots = new HashSet<int>();
            foreach (var s in soldiers)
            {
                Assert.IsTrue(s.IsSoldier);
                Assert.AreEqual(g.Id, s.GeneralId);
                slots.Add(s.FormationSlot);
                Assert.AreEqual(g.MaxHp / 10f, s.MaxHp, 1e-3f, "士兵 MaxHp=主将/10");
                Assert.AreEqual(g.Attack / 10f, s.Attack, 1e-3f, "士兵 Attack=主将/10");
                Assert.AreEqual(g.Shield / 10f, s.Shield, 1e-3f, "士兵 Shield=主将/10");
                Assert.AreEqual(g.AttackIntervalSec, s.AttackIntervalSec, 1e-4f);
                Assert.AreEqual(g.MoveSpeed, s.MoveSpeed, 1e-4f);
                Assert.AreEqual(g.Range, s.Range);
                Assert.AreEqual(HeroId.PanShi, s.Hero.Value);
            }
            Assert.AreEqual(12, slots.Count, "槽号 0..11 各一次");
            for (int i = 0; i < 12; i++)
                Assert.IsTrue(slots.Contains(i), $"缺槽 {i}");
        }

        [Test]
        public void T2_FormationFollow()
        {
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));
            sim.SetMoveTarget(g.Id, new GridPos(15, 120));

            int guard = 0;
            while (g.State != EntityState.Idle && guard++ < 600) sim.Step();
            Assert.AreEqual(EntityState.Idle, g.State, "主将应抵达目标");
            for (int i = 0; i < 60; i++) sim.Step(); // 士兵追位收敛

            foreach (var s in SoldiersOf(sim))
            {
                var target = g.PosF + RegimentSystem.SlotOffset(g, s.FormationSlot);
                Assert.Less(SimVec2.Distance(s.PosF, target), 1.2f,
                    $"士兵槽 {s.FormationSlot} 应贴近阵型目标点");
            }
        }

        [Test]
        public void T3_Facing()
        {
            var sim = NewSim();
            var blue = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));
            var red = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 140));

            // 2026-09-10 三列纵队:槽2 = row0,col2 → 蓝将身后 -X*1.2 且右列偏 -Y;红将镜像
            Assert.AreEqual(-1.2f, RegimentSystem.SlotOffset(blue, 2).X, 1e-3f, "蓝槽2 纵向 -1.2");
            Assert.AreEqual(-1f, RegimentSystem.SlotOffset(blue, 2).Y, 1e-3f);
            Assert.AreEqual(1.2f, RegimentSystem.SlotOffset(red, 2).X, 1e-3f, "红槽2 纵向 +1.2");
            Assert.AreEqual(1f, RegimentSystem.SlotOffset(red, 2).Y, 1e-3f); // 镜像侧偏 +1

            // 中列(col=1)槽横向偏移 = 0
            foreach (int slot in new[] { 1, 4, 7, 10 })
            {
                Assert.AreEqual(0f, RegimentSystem.SlotOffset(blue, slot).Y, 1e-4f, $"蓝槽{slot} 横向 0");
                Assert.AreEqual(0f, RegimentSystem.SlotOffset(red, slot).Y, 1e-4f, $"红槽{slot} 横向 0");
            }

            // 默认朝向:蓝 (1,0) 红 (-1,0)
            Assert.AreEqual(new SimVec2(1f, 0f), RegimentSystem.FacingOf(blue));
            Assert.AreEqual(new SimVec2(-1f, 0f), RegimentSystem.FacingOf(red));
        }

        [Test]
        public void T4_GeneralDies_SoldiersNeverRout()
        {
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));

            var red = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 122));
            red.Hp = 5000f; red.MaxHp = 5000f; red.Attack = 0f; // 靶子:不反击

            g.Hp = 0f;
            sim.RemoveEntity(g.Id); // 主将战死移除 → 士兵无将续战

            var soldiers = SoldiersOf(sim);
            Assert.AreEqual(12, soldiers.Count);
            var birth = SimVec2.FromGrid(new GridPos(15, 110));

            float minDistToRed = float.MaxValue;
            bool anyDamage = false;
            sim.AddEventListener(ev => { if (ev.Type == SimEventType.DamageDealt) anyDamage = true; });

            for (int i = 0; i < 50; i++)
            {
                sim.Step();
                foreach (var s in SoldiersOf(sim))
                    minDistToRed = System.Math.Min(minDistToRed, SimVec2.Distance(s.PosF, red.PosF));
            }

            var after = SoldiersOf(sim);
            Assert.AreEqual(12, after.Count, "士兵全部存活(不溃逃、不被歼)");
            foreach (var s in after)
            {
                Assert.AreNotEqual(EntityState.Dead, s.State);
                Assert.AreNotEqual(EntityState.Routing, s.State, "士兵永不溃逃");
                Assert.AreEqual(0, s.GarrisonBuildingId, "未绑定任何建筑");
                Assert.Less(SimVec2.Distance(s.PosF, birth), 10f, "仍在出生区域(<10m)");
            }
            Assert.IsTrue(anyDamage || minDistToRed < SimVec2.Distance(birth, red.PosF),
                "至少 1 名士兵对红单位进入交战(造成伤害或距离缩小)");
        }

        [Test]
        public void T5_LeaderlessAutoEngage()
        {
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 110));

            var red = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 122));
            red.Hp = 5000f; red.MaxHp = 5000f; red.Attack = 0f;

            g.Hp = 0f;
            sim.RemoveEntity(g.Id);

            float MinDist()
            {
                float m = float.MaxValue;
                foreach (var s in SoldiersOf(sim))
                    m = System.Math.Min(m, SimVec2.Distance(s.PosF, red.PosF));
                return m;
            }

            float before = MinDist();
            bool attacking = false;
            for (int i = 0; i < 50; i++)
            {
                sim.Step();
                foreach (var s in SoldiersOf(sim))
                    if (s.State == EntityState.Attacking) attacking = true;
            }
            float after = MinDist();
            Assert.IsTrue(after <= before - 1f || attacking,
                $"无将士兵应自动就近索敌(距离 {before:F2}→{after:F2} 或进入 Attacking)");
        }

        [Test]
        public void T6_SoldierPlainDamage()
        {
            var sim = NewSim();
            var blueG = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var redG = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhenYue, new GridPos(15, 200));

            var blueSoldiers = SoldiersOf(sim).FindAll(s => s.Side == Side.Blue);
            var redSoldiers = SoldiersOf(sim).FindAll(s => s.Side == Side.Red);
            Assert.AreEqual(12, blueSoldiers.Count);
            Assert.AreEqual(12, redSoldiers.Count);

            var ps = blueSoldiers[0]; // 磐石士兵
            var zy = redSoldiers[0];  // 镇岳士兵

            // 磐石士兵 → 镇岳士兵:无镇岳破防、无镇岳受远程,伤害=攻击者 Attack
            Assert.AreEqual(ps.Attack, DamageCalculator.CalculateDamage(ps, zy, sim.Game, false), 1e-4f,
                "磐石士兵打镇岳士兵应为平伤(无破防规则)");
            // 镇岳士兵 → 磐石士兵:无破防克制、无磐石盾减伤、无镇岳护甲系数,伤害=攻击者 Attack
            Assert.AreEqual(zy.Attack, DamageCalculator.CalculateDamage(zy, ps, sim.Game, false), 1e-4f,
                "镇岳士兵打磐石士兵应为平伤(无破防/护甲/盾减伤)");
        }

        [Test]
        public void T7_SoldierVision()
        {
            var sim = NewSim();
            VisionSystem.Reset();

            // 磐石士兵(近战 3):放 (15,110),单独一 sim 之外直接改位避免重叠——用两个独立兵团置位
            var bluePanShi = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var blueZhuiRi = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhuiRi, new GridPos(21, 100));

            Entity psSoldier = null, zrSoldier = null;
            foreach (var e in sim.Entities.Values)
            {
                if (!e.IsSoldier) continue;
                if (e.GeneralId == bluePanShi.Id) psSoldier = e;
                if (e.GeneralId == blueZhuiRi.Id && zrSoldier == null) zrSoldier = e;
            }
            Assert.IsNotNull(psSoldier, "磐石士兵");
            Assert.IsNotNull(zrSoldier, "坠日士兵");

            // 手工放置到探测位(移速置 0,冻结阵型跟随/自动索敌,保证几何稳定)
            psSoldier.MoveSpeed = 0f;
            zrSoldier.MoveSpeed = 0f;
            psSoldier.PosF = SimVec2.FromGrid(new GridPos(15, 110));
            psSoldier.Pos = new GridPos(15, 110);
            zrSoldier.PosF = SimVec2.FromGrid(new GridPos(15, 112));
            zrSoldier.Pos = new GridPos(15, 112);

            // 探针:红方单位在目标格,由蓝方士兵视野揭示判定
            var probeA = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 113));
            probeA.Hero = HeroId.DuanFeng; probeA.Hp = probeA.MaxHp = 100f; probeA.Attack = 0f;
            sim.Step();
            // 磐石士兵 (15,110) 视野 3:欧氏距 sqrt(0+9)=3 ≤ 3 → (15,113) 可见
            Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, probeA.Id),
                "磐石士兵视野 3,应揭示 (15,113)");
            sim.RemoveEntity(probeA.Id);

            var probeB = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 114));
            probeB.Hero = HeroId.DuanFeng; probeB.Hp = probeB.MaxHp = 100f; probeB.Attack = 0f;
            sim.Step();
            // 把坠日士兵移远到 (15,122):距 (15,114)=8 > 6,不再覆盖;磐石士兵距其 4 > 3 亦不覆盖
            zrSoldier.PosF = SimVec2.FromGrid(new GridPos(15, 122));
            zrSoldier.Pos = new GridPos(15, 122);
            for (int i = 0; i < 35; i++) sim.Step(); // 视野信息 3s(30 tick)保鲜过期
            Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, probeB.Id),
                "坠日士兵移远后,(15,114) 距磐石士兵 4 超出视野 3,应不可见");
            sim.RemoveEntity(probeB.Id);

            // 坠日士兵(远程 6):从 (15,112) 移回
            zrSoldier.PosF = SimVec2.FromGrid(new GridPos(15, 110));
            zrSoldier.Pos = new GridPos(15, 110);
            psSoldier.PosF = SimVec2.FromGrid(new GridPos(15, 108));
            psSoldier.Pos = new GridPos(15, 108); // 磐石士兵移开避免干扰
            for (int i = 0; i < 35; i++) sim.Step(); // 旧视野保鲜(此前坠日士兵在 112/122 处覆盖过远端格)过期

            var probeC = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 116));
            probeC.Hero = HeroId.DuanFeng; probeC.Hp = probeC.MaxHp = 100f; probeC.Attack = 0f;
            sim.Step();
            // 坠日士兵 (15,110) 视野 6:距 (15,116)=6 ≤ 6 → 可见;距磐石士兵(15,108)=8 不可见
            Assert.IsTrue(VisionSystem.IsUnitVisible(sim, Side.Blue, probeC.Id),
                "坠日士兵视野 6,应揭示 (15,116)");
            sim.RemoveEntity(probeC.Id);

            var probeD = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 117));
            probeD.Hero = HeroId.DuanFeng; probeD.Hp = probeD.MaxHp = 100f; probeD.Attack = 0f;
            sim.Step();
            // 距坠日士兵 7 > 6;距磐石士兵 9 > 3 → 不可见
            Assert.IsFalse(VisionSystem.IsUnitVisible(sim, Side.Blue, probeD.Id),
                "坠日士兵视野 6,(15,117) 距 7 不应被揭示");
        }

        [Test]
        public void T8_SoldierNoPassive_OnHit()
        {
            var sim = NewSim();
            // 断峰兵团(蓝)取士兵;敌方靶子单独生成(高血不反击)
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.DuanFeng, new GridPos(15, 100));
            var soldiers = SoldiersOf(sim);
            Assert.AreEqual(12, soldiers.Count);
            // 冻结全体移速,手工布置交战对(避免阵型跟随干扰几何)
            foreach (var s in soldiers) s.MoveSpeed = 0f;
            g.MoveSpeed = 0f;
            g.PosF = SimVec2.FromGrid(new GridPos(15, 90)); g.Pos = new GridPos(15, 90); // 主将移远

            var df = soldiers[0]; // 断峰士兵
            df.PosF = SimVec2.FromGrid(new GridPos(15, 110)); df.Pos = new GridPos(15, 110);
            var target = sim.SpawnEntity(Side.Red, EntityKind.Unit, new GridPos(15, 111));
            target.Hp = target.MaxHp = 5000f; target.Attack = 0f;

            for (int i = 0; i < 300; i++) sim.Step(); // 断峰攻速 2.5s,远超一次命中
            Assert.Less(target.Hp, 5000f, "断峰士兵应已对目标造成伤害(命中发生过)");
            Assert.IsFalse(target.Status.Has(DebuffType.Slow), "断峰士兵攻击不施加迟滞(士兵不继承被动)");
            sim.RemoveEntity(target.Id);

            // 坠日士兵攻击镇岳士兵:无重伤/脆弱
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.ZhuiRi, new GridPos(15, 130));
            var g3 = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.ZhenYue, new GridPos(15, 150));
            var zrSoldiers = SoldiersOf(sim).FindAll(s => s.GeneralId == g2.Id);
            var zySoldiers = SoldiersOf(sim).FindAll(s => s.GeneralId == g3.Id);
            foreach (var s in zrSoldiers) s.MoveSpeed = 0f;
            foreach (var s in zySoldiers) s.MoveSpeed = 0f;
            g2.MoveSpeed = 0f; g3.MoveSpeed = 0f;
            g2.PosF = SimVec2.FromGrid(new GridPos(15, 60)); g2.Pos = new GridPos(15, 60); // 主将移远
            g3.PosF = SimVec2.FromGrid(new GridPos(15, 61)); g3.Pos = new GridPos(15, 61);

            var zr = zrSoldiers[0]; // 坠日士兵
            var zy = zySoldiers[0]; // 镇岳士兵
            zr.PosF = SimVec2.FromGrid(new GridPos(15, 120)); zr.Pos = new GridPos(15, 120);
            zy.PosF = SimVec2.FromGrid(new GridPos(15, 121)); zy.Pos = new GridPos(15, 121);
            zy.Hp = 5000f; zy.MaxHp = 5000f; zy.Attack = 0f;

            for (int i = 0; i < 300; i++) sim.Step(); // 坠日攻速 2s,远超一次命中
            Assert.Less(zy.Hp, 5000f, "坠日士兵应已对镇岳士兵造成伤害(命中发生过)");
            Assert.IsFalse(zy.Status.Has(DebuffType.Wound), "坠日士兵攻击镇岳士兵不附加重伤(士兵不继承被动)");
            Assert.IsFalse(zy.Status.Has(DebuffType.Fragile), "坠日士兵攻击镇岳士兵不附加脆弱(士兵不继承被动)");
        }

        [Test]
        public void T9_SoldierNoShieldRegen()
        {
            // 磐石士兵:盾不足额、脱战超时,步进 100 tick 不回盾
            var sim = NewSim();
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi, new GridPos(15, 100));
            var s = SoldiersOf(sim)[0];
            Assert.Less(10f, s.ShieldMax, "前置:ShieldMax 应高于 10(磐石士兵盾=主将/10=14)");
            s.Shield = 10f;
            s.SecondsSinceCombat = 99f; // 远超 5s 回盾延迟
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.AreEqual(10f, s.Shield, 1e-4f, "磐石士兵脱战不应回盾(士兵不继承被动)");

            // 对照:磐石主将同条件应回盾
            var g2 = RegimentSystem.SpawnGeneral(sim, Side.Red, HeroId.PanShi, new GridPos(15, 140));
            g2.Shield = 10f; // ShieldMax=140,远未满
            g2.SecondsSinceCombat = 99f;
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.Greater(g2.Shield, 10f, "磐石主将脱战应回盾(对照)");
        }
    }
}
