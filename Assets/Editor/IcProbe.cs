using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 2026-09-14 新增:制作人点名六条的**同一把尺子**。前一个窗口的教训是"改了但没验证",
    /// 所以这里只出可比较的数字(停位半径/墙血/顶面高度/悬空高度/驻军人数),修前修后跑同一入口。
    /// 用法:Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.IcProbe.Run -logFile &lt;log&gt;
    /// 输出:D:/DevTools/shots/icprobe.txt + probe_*.png
    /// </summary>
    public static class IcProbe
    {
        private const string ShotDir = "D:/DevTools/shots";
        private static readonly StringBuilder Log = new StringBuilder();
        private static readonly HeroId[] AllHeroes =
        {
            HeroId.PanShi, HeroId.ZhenYue, HeroId.DuanFeng, HeroId.ZhuFeng, HeroId.ZhuiRi, HeroId.XingLuo
        };

        private static void L(string s) { Log.AppendLine(s); Debug.Log("[icprobe] " + s); }

        private static SimCore BuildSim()
        {
            MatchSetup.Reset();
            var cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = MatchSetup.Build(cfg, null, null);   // 双方都不挂 AI:探针要完全受控
            sim.PopulationCapOverride = 999;
            return sim;
        }

        private static Camera MakeCamera()
        {
            var go = new GameObject("ProbeCam");
            var cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.42f, 0.62f, 0.85f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 900f; cam.fieldOfView = 32f;
            var lgo = new GameObject("ProbeLight");
            var l = lgo.AddComponent<Light>();
            l.type = LightType.Directional; l.intensity = 1.05f; l.color = new Color(1f, 0.98f, 0.93f);
            lgo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
            return cam;
        }

        private static void Shoot(Camera cam, Vector3 eye, Vector3 look, string name, int w = 1400, int h = 900)
        {
            cam.transform.position = eye; cam.transform.LookAt(look);
            var warm = new RenderTexture(64, 64, 24);
            cam.targetTexture = warm; cam.Render(); Object.DestroyImmediate(warm); cam.targetTexture = null;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            Directory.CreateDirectory(ShotDir);
            var path = ShotDir + "/" + name;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
            L("  拍照 " + path);
        }

        /// <summary>找一块空着的格子(建筑占位 + 单位 1 米内都算占用)。</summary>
        private static GridPos FreeCell(SimCore sim, int row, int col)
        {
            for (int d = 0; d < 60; d++)
                for (int dr = -d; dr <= d; dr++)
                    for (int dc = -d; dc <= d; dc++)
                    {
                        var p = new GridPos(row + dr, col + dc);
                        if (!TerrainMap.IsInside(p)) continue;
                        if (Occupied(sim, p)) continue;
                        return p;
                    }
            return new GridPos(row, col);
        }

        private static bool Occupied(SimCore sim, GridPos p)
        {
            var c = SimVec2.FromGrid(p);
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e.Kind == EntityKind.Building && !e.Rubbled && e.Pos == p) return true;
                if (e.Kind == EntityKind.Unit && SimVec2.Distance(e.PosF, c) < 1.2f) return true;
            }
            return false;
        }

        /// <summary>放一个"光杆"主将(拆掉随行士兵,免得士兵围上来把测量搅浑)并落到指定点。</summary>
        private static Entity LoneGeneral(SimCore sim, HeroId hero, SimVec2 at)
        {
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, hero, FreeCell(sim, 80, 40));
            var kids = new List<int>();
            foreach (var kv in sim.Entities)
                if (kv.Value.IsSoldier && kv.Value.GeneralId == g.Id) kids.Add(kv.Key);
            foreach (var k in kids) sim.RemoveEntity(k);
            sim.FreeCell(g.Pos);
            g.PosF = at; g.Pos = at.ToGrid();
            g.HasMoveTarget = false; g.State = EntityState.Idle;
            return g;
        }

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            L("=== IcProbe " + System.DateTime.Now.ToString("MM-dd HH:mm") + " ===");
            L("常数:Radius=" + WallSystem.Radius + " WallHalf=" + WallSystem.WallHalf
                + " WallHitMargin=" + WallSystem.WallHitMargin + " WallClickTolerance=" + WallSystem.WallClickTolerance
                + " WallFootOffset=" + WallSystem.WallFootOffset);
            try { ScenarioProduction(); } catch (System.Exception e) { L("!! 场景1异常 " + e); }
            try { ScenarioWall(); } catch (System.Exception e) { L("!! 场景2异常 " + e); }
            try { ScenarioFlag(); } catch (System.Exception e) { L("!! 场景3异常 " + e); }
            try { ScenarioGarrison(); } catch (System.Exception e) { L("!! 场景4异常 " + e); }
            try { ScenarioViewAudit(); } catch (System.Exception e) { L("!! 场景5异常 " + e); }
            L("=== 结束 ===");
            File.WriteAllText(ShotDir + "/icprobe.txt", Log.ToString());
            Debug.Log(Log.ToString());
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        // ---- 场景1:六将生产(属性/模型/并排出图) + 脚下是否悬空 ----
        private static void ScenarioProduction()
        {
            L("---- 场景1 生产六将 ----");
            var sim = BuildSim();
            int baseId = 0;
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Base
                    && kv.Value.Side == Side.Blue) { baseId = kv.Key; break; }

            var before = new HashSet<int>();
            foreach (var kv in sim.Entities) before.Add(kv.Key);
            foreach (var h in AllHeroes)
            {
                string err;
                bool ok = ProductionSystem.Enqueue(sim, baseId, h, out err);
                L("入队 " + h + " ok=" + ok + " err=" + (err ?? "-"));
            }
            for (int i = 0; i < 180 * sim.Config.TickRate; i++) sim.Step();

            var made = new List<Entity>();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || before.Contains(e.Id) || e.IsSoldier) continue;
                made.Add(e);
                int n = 0; string kidHero = "-";
                foreach (var kv2 in sim.Entities)
                    if (kv2.Value.IsSoldier && kv2.Value.GeneralId == e.Id)
                    { n++; if (kidHero == "-") kidHero = kv2.Value.Hero.ToString(); }
                L("  新主将 id=" + e.Id + " hero=" + e.Hero + " side=" + e.Side + " 兵数=" + n + " 兵hero=" + kidHero);
            }
            L("  合计新主将=" + made.Count + " (期望 6)");

            var cam = MakeCamera();
            var views = new List<GameObject>();
            float baseX = 100f, y = TerrainMap.CenterY, gap = 4.2f;
            for (int i = 0; i < made.Count; i++)
            {
                var go = new GameObject("Lineup" + i);
                var v = go.AddComponent<EntityView>();
                v.Bind(made[i], made[i].Side, made[i].Hero, null);
                var wp = BoardView.WorldPos(new SimVec2(baseX + i * gap, y));
                go.transform.position = new Vector3(wp.x, 0f, wp.z);
                // 脚下悬空量:模型网格世界最低点 vs 模型根(脚下环所在的平面)
                float minY = float.MaxValue; uint hv = 2166136261u;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                {
                    if (r.name.StartsWith("Foot") || r.name == "GlowRing" || r.name == "GlowRingSel") continue;
                    minY = Mathf.Min(minY, r.bounds.min.y);
                    hv = (hv ^ (uint)r.sharedMaterial.GetInstanceID()) * 16777619u;
                }
                float hgt = 0f;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) hgt = Mathf.Max(hgt, r.bounds.max.y);
                L("  视图 " + made[i].Hero + " 根位置Y=" + go.transform.position.y.ToString("F2")
                    + " 网格最低Y=" + minY.ToString("F2") + " 悬空=" + (minY - go.transform.position.y).ToString("F2")
                    + "m 模型高=" + hgt.ToString("F2") + "m 材质哈希=" + hv.ToString("X8"));
                views.Add(go);
            }
            if (views.Count > 0)
            {
                float cx = baseX + (views.Count - 1) * gap * 0.5f;
                var c = BoardView.WorldPos(new SimVec2(cx, y));
                Shoot(cam, new Vector3(c.x, 9f, c.z - 19f), new Vector3(c.x, 1.4f, c.z), "probe_lineup.png", 1900, 760);
            }
            foreach (var g in views) Object.DestroyImmediate(g);
        }

        /// <summary>隔离仿真:只有我们放进去的单位(屏障是静态系统,不需要建筑实体)。
        /// 2026-09-14:屏障场景原来跑在整局里,红军自主单位会扑上来接战 → 数据被战斗污染。</summary>
        private static SimCore IsolatedSim(bool autoBehavior)
        {
            MatchSetup.Reset();
            var cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = new SimCore(new SimConfig { AutoBehavior = autoBehavior }, new SeededRng(5));
            sim.Game = cfg;
            sim.PopulationCapOverride = 999;
            return sim;
        }

        // ---- 场景2:磐石够不够得着敌方屏障 ----
        private static void ScenarioWall()
        {
            L("---- 场景2 贴墙可击性(隔离仿真) ----");
            var rc = WallSystem.BaseCenter(Side.Red);
            L("  红方基地中心=" + rc.X.ToString("F1") + "," + rc.Y.ToString("F1") + " 屏障半径=" + WallSystem.Radius);

            var sim = IsolatedSim(true);
            float hp0 = WallSystem.Hp(Side.Red, 2);
            var a = LoneGeneral(sim, HeroId.PanShi, new SimVec2(rc.X - WallSystem.Radius - 10f, rc.Y));
            var dest = WallSystem.NearestWallPoint(Side.Blue, new SimVec2(rc.X - WallSystem.Radius, rc.Y));
            L("  行军目的地=" + dest.X.ToString("F2") + "," + dest.Y.ToString("F2")
                + " 其半径=" + SimVec2.Distance(rc, dest).ToString("F2") + "(墙皮 20)");
            sim.SetMoveTarget(a.Id, dest.ToGrid(), true);
            for (int k = 0; k < 12; k++)
            {
                for (int i = 0; i < sim.Config.TickRate; i++) sim.Step();
                var u = sim.GetEntity(a.Id);
                if (u == null) break;
                float rr = SimVec2.Distance(rc, u.PosF);
                L("   自动ON t=" + k + "s 离墙皮=" + (rr - WallSystem.Radius).ToString("F2")
                    + " state=" + u.State + " 有目标=" + u.HasMoveTarget + " 目标格=" + u.MoveTarget
                    + " AutoKind=" + u.AutoKind + " 墙血=" + WallSystem.Hp(Side.Red, 2).ToString("F1"));
            }
            L("  自动ON 12s 结论:中路段血=" + WallSystem.Hp(Side.Red, 2).ToString("F1") + "(200 起,碎=0)");

            var s4 = IsolatedSim(false);
            var a4 = LoneGeneral(s4, HeroId.PanShi, new SimVec2(rc.X - WallSystem.Radius - 10f, rc.Y));
            s4.SetMoveTarget(a4.Id, dest.ToGrid(), true);
            for (int k = 0; k < 12; k++)
            {
                for (int i = 0; i < s4.Config.TickRate; i++) s4.Step();
                var u = s4.GetEntity(a4.Id);
                if (u == null) break;
                float rr = SimVec2.Distance(rc, u.PosF);
                L("   自动OFF t=" + k + "s 离墙皮=" + (rr - WallSystem.Radius).ToString("F2")
                    + " state=" + u.State + " 墙血=" + WallSystem.Hp(Side.Red, 2).ToString("F1"));
            }
            L("  自动OFF 12s 结论:中路段血=" + WallSystem.Hp(Side.Red, 2).ToString("F1"));

            // 逐半径摆位打 3 秒:画出"打得到"的区间(阻挡带 vs 命中带)
            for (float off = 0.2f; off <= 2.6f; off += 0.4f)
            {
                var s2 = IsolatedSim(true);
                var p2 = new SimVec2(rc.X - WallSystem.Radius - off, rc.Y);
                LoneGeneral(s2, HeroId.PanShi, p2);
                float h2 = WallSystem.Hp(Side.Red, 2);
                for (int i = 0; i < 3 * s2.Config.TickRate; i++) s2.Step();
                L("  离墙皮 " + off.ToString("F1") + "m → 3s 伤害 " + (h2 - WallSystem.Hp(Side.Red, 2)).ToString("F1")
                    + " 命中带内=" + WallSystem.OnWall(Side.Red, 2, p2, WallSystem.WallHitMargin)
                    + " 薄壳内=" + WallSystem.OnWall(Side.Red, 2, p2)
                    + " 敌方可通过=" + WallSystem.CanPass(Side.Red, p2));
            }

            // 混战环境参考:整局里同样下这条令,看有没有被战斗拖住(仅作对照,不作判据)
            var full = BuildSim();
            var fc = WallSystem.BaseCenter(Side.Red);
            var fa = LoneGeneral(full, HeroId.PanShi, new SimVec2(fc.X - WallSystem.Radius - 10f, fc.Y));
            full.SetMoveTarget(fa.Id, WallSystem.NearestWallPoint(Side.Blue, new SimVec2(fc.X - WallSystem.Radius, fc.Y)).ToGrid(), true);
            for (int i = 0; i < 40 * full.Config.TickRate; i++) full.Step();
            var fu = full.GetEntity(fa.Id);
            float fr = fu != null ? SimVec2.Distance(fc, fu.PosF) : -1f;
            L("  整局对照 40s:离墙皮=" + (fr - WallSystem.Radius).ToString("F2")
                + " 中路段血=" + WallSystem.Hp(Side.Red, 2).ToString("F1") + "(混战环境,仅供参考)");
            Shoot(MakeCamera(), new Vector3(fc.X - 26f, 15f, 3f - fc.Y - 18f),
                new Vector3(fc.X - WallSystem.Radius, 3f, 3f - fc.Y), "probe_wall.png");
        }

        // ---- 场景3:旗帜插多高 + 粒子污染的量级 ----
        private static void ScenarioFlag()
        {
            L("---- 场景3 旗帜高度 ----");
            var sim = BuildSim();
            var topYField = typeof(EntityView).GetField("_topY", BindingFlags.Instance | BindingFlags.NonPublic);
            float maxOff = 0f; string worst = "-";
            var cam = MakeCamera();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Building || e.Rubbled) continue;
                var go = new GameObject("Bv");
                var v = go.AddComponent<EntityView>();
                v.Bind(e, e.Side, null, e.Building);
                float topY = topYField != null ? (float)topYField.GetValue(v) : -1f;
                // 同一条口径跑两遍:只认 MeshRenderer(现行) vs 认所有 Renderer(粒子会进来)
                float roofMesh = 1f, roofAll = 1f;
                var model = go.transform.Find("Model");
                if (model != null)
                {
                    bool a1 = false, a2 = false;
                    foreach (var r in model.GetComponentsInChildren<Renderer>())
                    {
                        if (r == null || r.gameObject.name.StartsWith("Foot")) continue;
                        float yAll = model.InverseTransformPoint(r.bounds.max).y;
                        if (!a2 || yAll > roofAll) { roofAll = yAll; a2 = true; }
                        if (!(r is MeshRenderer)) continue;
                        float yM = model.InverseTransformPoint(r.bounds.max).y;
                        if (!a1 || yM > roofMesh) { roofMesh = yM; a1 = true; }
                    }
                }
                float flagY = -1f;
                foreach (var t in go.GetComponentsInChildren<Transform>()) if (t.name == "Flag") flagY = t.position.y;
                float off = flagY - (go.transform.position.y + topY);
                if (off > maxOff) { maxOff = off; worst = e.Building + "@" + e.Pos.Col + "," + e.Pos.Row; }
                if (e.Building == BuildingId.Base || e.Building == BuildingId.Barracks || e.Building == BuildingId.Tower)
                    L("  " + e.Building + " side=" + e.Side + " 建模目标顶=" + topY.ToString("F2")
                        + " 只认网格的顶=" + roofMesh.ToString("F2") + " 认所有Renderer的顶=" + roofAll.ToString("F2")
                        + " 采用RoofY=" + v.RoofY.ToString("F2") + " 旗布高=" + flagY.ToString("F2")
                        + " 高出顶面=" + off.ToString("F2") + "m");
                if (e.Building == BuildingId.Base && e.Side == Side.Blue)
                {
                    var center = BoardView.WorldPos(e.Pos);
                    Shoot(cam, new Vector3(center.x - 16f, 20f, center.z - 30f),
                        new Vector3(center.x, 6.5f, center.z), "probe_flag.png", 1200, 1000);
                }
                Object.DestroyImmediate(go);
            }
            L("  旗布最大高出顶面=" + maxOff.ToString("F2") + "m @" + worst);
        }

        // ---- 场景5:走**真实建视图路径**核对每个单位的模型 ----
        /// <summary>
        /// 2026-09-14 制作人:"不管生产什么主将,都是磐石带领十几个磐石建模的士兵,大小 1:1"。
        /// 前几轮探针都是**手工建视图**(那时 Sim 早配好了),所以一直没抓到 ——
        /// 真因在 View 的建视图时机:`SimCore.SpawnEntity` **先发 EntitySpawned 再返回**,
        /// 而 `SpawnGeneral/SpawnSoldier` 是在事件之后才写 Hero/IsSoldier。
        /// 本场景因此**必须走事件路径**:建 BoardView + 订阅事件 + 跑 Sim,
        /// 再把每个单位的实际模型层级打出来(士兵必须是程序化 SoldierModel,主将必须是 Heroes/<Hero>)。
        /// </summary>
        private static void ScenarioViewAudit()
        {
            L("---- 场景5 真实建视图路径核对(生产 逐风/星落/断峰) ----");
            MatchSetup.Reset();
            var cfg = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text);
            var sim = MatchSetup.Build(cfg, null, null);
            sim.PopulationCapOverride = 999;

            var root = new GameObject("AuditRoot");
            var boardGo = new GameObject("Board");
            boardGo.transform.SetParent(root.transform, false);
            var bv = boardGo.AddComponent<BoardView>();
            bv.Init(sim, root.transform);
            bv.PlayerSide = Side.Blue;
            sim.AddEventListener(bv.OnSimEvent);

            int baseId = 0;
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Base
                    && kv.Value.Side == Side.Blue) { baseId = kv.Key; break; }
            var heroes = new[] { HeroId.ZhuFeng, HeroId.XingLuo, HeroId.DuanFeng };
            foreach (var h in heroes)
            {
                string err;
                L("  入队 " + h + " ok=" + ProductionSystem.Enqueue(sim, baseId, h, out err) + " err=" + (err ?? "-"));
            }
            for (int i = 0; i < 200 * sim.Config.TickRate; i++) { sim.Step(); bv.TickViews(); }

            int gen = 0, sol = 0, bad = 0;
            var cam = MakeCamera();
            foreach (var kv in sim.Entities)
            {
                var e = kv.Value;
                if (e == null || e.Kind != EntityKind.Unit || e.Side != Side.Blue || e.State == EntityState.Dead) continue;
                var v = bv.GetView(e.Id);
                string model = v != null ? ModelChain(v.gameObject) : "(无视图)";
                bool soldierModel = model.Contains("SoldierModel/Torso");
                bool heroModel = model.Contains("Heroes/");
                string flag;
                if (e.IsSoldier) { sol++; flag = soldierModel ? "OK" : "✗ 士兵不是程序化模型"; }
                else
                {
                    gen++;
                    flag = heroModel && model.Contains("Heroes/" + e.Hero) ? "OK" : "✗ 主将模型与 Hero 不符";
                }
                if (flag != "OK") bad++;
                bool print = !e.IsSoldier || e.FormationSlot == 0;   // 每个兵团只打第一个士兵,免得刷屏
                if (print)
                    L("    " + (e.IsSoldier ? "兵" : "将") + " id=" + e.Id + " hero=" + e.Hero
                        + " 将id=" + e.GeneralId + " 模型=" + model + "  → " + flag);
            }
            L("  合计:主将 " + gen + " 士兵 " + sol + " 违规 " + bad + " (期望 违规=0)");

            int sample = 0; SimVec2 at = default;
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Unit && !kv.Value.IsSoldier && kv.Value.Side == Side.Blue
                    && kv.Value.Hero == HeroId.XingLuo) { at = kv.Value.PosF; sample = kv.Value.Id; break; }
            if (sample != 0)
            {
                var c = BoardView.WorldPos(at);
                Shoot(cam, new Vector3(c.x, 9f, c.z - 13f), new Vector3(c.x, 1.2f, c.z), "probe_views.png", 1600, 1000);
            }
            sim.ClearEventListeners();
            Object.DestroyImmediate(root);
        }

        private static string ModelChain(GameObject go)
        {
            var parts = new List<string>();
            CollectChain(go.transform, "", parts, 4);
            return string.Join(" ", parts.ToArray());
        }

        private static void CollectChain(Transform t, string prefix, List<string> outp, int depth)
        {
            if (depth <= 0) return;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c.name == "HpBar" || c.name == "CountLabel") continue;
                outp.Add(prefix + c.name);
                if (prefix.Length < 12) CollectChain(c, prefix + c.name + "/", outp, depth - 1);
            }
        }

        // ---- 场景4:驻军释放到底放出几个人 ----
        private static void ScenarioGarrison()
        {
            L("---- 场景4 驻军进出 ----");
            var sim = BuildSim();
            int barracks = 0;
            foreach (var kv in sim.Entities)
                if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Barracks
                    && kv.Value.Side == Side.Blue) { barracks = kv.Key; break; }
            var b = sim.GetEntity(barracks);
            var g = RegimentSystem.SpawnGeneral(sim, Side.Blue, HeroId.PanShi,
                FreeCell(sim, b.Pos.Row, b.Pos.Col - 6));
            var ids = new List<int> { g.Id };
            foreach (var kv in sim.Entities)
                if (kv.Value.IsSoldier && kv.Value.GeneralId == g.Id) ids.Add(kv.Value.Id);
            string err;
            bool ok = GarrisonSystem.EnterFormal(sim, ids, barracks, out err);
            L("  主将=" + g.Id + " 入城单位=" + ids.Count + " EnterFormal ok=" + ok
                + " 正式=" + b.GarrisonFormal.Count + " 临时=" + b.GarrisonTemp.Count);
            ok = GarrisonSystem.ExitRegiment(sim, barracks, HeroId.PanShi, out err);
            int still = 0, ghost = 0;
            foreach (var id in ids)
            {
                var u = sim.GetEntity(id);
                if (u == null) continue;
                if (u.GarrisonBuildingId != 0)
                {
                    still++;
                    if (!b.GarrisonFormal.Contains(id) && !b.GarrisonTemp.Contains(id)) ghost++;
                }
            }
            L("  ExitRegiment ok=" + ok + " err=" + (err ?? "-") + " 释放后仍在城内=" + still
                + " 幽灵=" + ghost + " 名册剩=" + (b.GarrisonFormal.Count + b.GarrisonTemp.Count) + " (期望 0/0/0)");

            // 极端:城门区全占 + 名册里有已阵亡的陈旧 id → 释放还能不能干净收尾
            var s2 = BuildSim();
            int bar2 = 0;
            foreach (var kv in s2.Entities)
                if (kv.Value.Kind == EntityKind.Building && kv.Value.Building == BuildingId.Barracks
                    && kv.Value.Side == Side.Blue) { bar2 = kv.Key; break; }
            var b2 = s2.GetEntity(bar2);
            var g2 = RegimentSystem.SpawnGeneral(s2, Side.Blue, HeroId.PanShi, FreeCell(s2, b2.Pos.Row, b2.Pos.Col - 6));
            var list2 = new List<int> { g2.Id };
            foreach (var kv in s2.Entities)
                if (kv.Value.IsSoldier && kv.Value.GeneralId == g2.Id) list2.Add(kv.Value.Id);
            GarrisonSystem.EnterFormal(s2, list2, bar2, out err);
            // 城门前列塞满己方士兵
            int blockers = 0;
            for (int dc = 0; dc <= 10 && blockers < 60; dc++)
                for (int dr = -8; dr <= 8 && blockers < 60; dr++)
                {
                    var cell = new GridPos(b2.Pos.Row + dr, b2.Pos.Col + dc);
                    if (!TerrainMap.IsInside(cell) || Occupied(s2, cell)) continue;
                    var u = s2.SpawnEntity(Side.Blue, EntityKind.Unit, cell);
                    u.Hero = HeroId.DuanFeng; u.IsSoldier = true; blockers++;
                }
            // 名册里塞一个已阵亡的陈旧 id(模拟"死在城里没清名册")
            var dead = s2.SpawnEntity(Side.Blue, EntityKind.Unit, FreeCell(s2, b2.Pos.Row + 10, b2.Pos.Col));
            dead.Hero = HeroId.PanShi;
            b2.GarrisonFormal.Add(dead.Id);
            s2.RemoveEntity(dead.Id);
            bool ok2 = false;
            string ex = null;
            try { ok2 = GarrisonSystem.ExitRegiment(s2, bar2, HeroId.PanShi, out err); }
            catch (System.Exception e) { ex = e.GetType().Name + ":" + e.Message; }
            var g2b = s2.GetEntity(g2.Id);
            bool ghostGeneral = g2b != null && g2b.GarrisonBuildingId != 0
                && !b2.GarrisonFormal.Contains(g2.Id) && !b2.GarrisonTemp.Contains(g2.Id);
            L("  城门塞满(" + blockers + ")+陈旧id: ExitRegiment ok=" + ok2 + " err=" + (err ?? "-")
                + " 异常=" + (ex ?? "无") + " 主将登记=" + (g2b != null ? g2b.GarrisonBuildingId.ToString() : "已消失")
                + " 主将幽灵=" + ghostGeneral + " 名册剩=" + (b2.GarrisonFormal.Count + b2.GarrisonTemp.Count));
        }
    }
}
