using System;
using System.Collections.Generic;

namespace IronCurtain.Sim
{
    /// <summary>城墙段三路索引(0 上 / 1 中 / 2 下,与 TerrainMap.LaneY 同口径)。</summary>
    public enum WallLane { Top = 0, Mid = 1, Bottom = 2 }

    /// <summary>
    /// 三路城墙(2026-09-11 制作人拍板,规格 MAP-3LANE-layout-spec.md §4):
    /// 绕基地的圆弧围栏(半径 40,不与路强行垂直——路有弧度,制作人明确叮嘱),每方三段,
    /// 段与段之间为折角(烽火台位,美术)。每段:
    /// - 门洞 = 路宽 14(自家士兵走门);
    /// - **五段可击破面**(上路/上草地/中路/下草地/下路),各 200 血,各自独立;
    /// - 击碎一段 = 该段消失(不可修复),该处可通行;完好时**敌方被挡、自家可穿**(魔法屏障,不设大门);
    /// - 屏障外 10 米提供本方视野。
    /// 纯 Sim/无 UnityEngine。实现上不生成实体,由通行判定 + 视野查询消费。
    /// </summary>
    public static class WallSystem
    {
        public const float Radius = 20f;          // 制作人 2026-09-11 裁决:基地中心外 20(原 40)
        // 2026-09-11 制作人:魔法屏障不设大门 —— 自家穿过由判定放行,敌方被挡,故无门洞参数。
        public const int Panels = 5;              // 五段可击破面:上路/上草地/中路/下草地/下路
        public const float BreachHalf = 7f;       // 破口半宽 = 14 / 2
        // 2026-09-13 制作人:"为什么不能屏障占地多少、建模范围就是多少" —— 正解。
        // 屏障建模是一张**零厚度曲面**(WallView: 贴 r=Radius,outPush 仅 0.006 防 z-fight),
        // 而这里原来是 ±5 米 —— 于是单位在离可见屏障 5 米处就被挡住,看着像"被空气挡住";
        // 攻击判定也受同一带影响,边界上还会出现"被挡却判不在墙上"(既打不着也过不去)。
        // 现把逻辑带收到与建模一致(薄壳),并保留下面的贴墙容差兜住边界浮点。
        public const float WallHalf = 0.6f;       // 墙体半宽 = 与建模一致(薄壳;原 5f)
        public const float WallClickTolerance = 4f;   // 点击拆墙的拾取容差(独立,不再由墙带宽派生)
        // 2026-09-14 制作人"磐石打不到屏障,还被挡着过不去" —— 真因是上一轮把贴墙容差加到了**阻挡侧**:
        // `CanPass` 用 WallHitMargin 挡停单位 → 停在离薄壳 1.6m 处(视觉上"被空气挡住");
        // 而伤害判定 `HitEnemyPanel` 用 0 容差(±0.6) → 停位永远不在命中带内 → 砍墙零伤害,
        // 同时 SimCore 仍无条件发 DamageDealt(看着像"打了但没效果")。
        // 现在口径拆清楚:**阻挡=薄壳本身**(走得到墙皮上),**命中/触发带=薄壳外扩 WallHitMargin**。
        public const float WallHitMargin = 0.8f;  // 命中带外扩(米):需 ≥ 单位单帧步长(最快 0.3m)+余量
        /// <summary>行军目的地:墙脚外侧一点点(必须落在命中带内,否则走过去也打不着)。</summary>
        public const float WallFootOffset = WallHalf + 0.15f;
        public const float VisionRadius = 10f;    // 墙外视野
        public const float WallHp = 200f;         // 制作人 2026-09-11 拍板:每段(可击破面)200 血

        /// <summary>
        /// 段中心方向角(弧度,世界系,以该方基地中心为原点):
        /// 取"该路距基地 20 米处"相对基地的方位角。蓝方中路=0°、上路≈−65°、下路≈+65°;
        /// 红方关于 Y 轴镜像(中路=180°)。Row 小=画面上方=角度负。
        /// </summary>
        public static float LaneAngleOf(Side side, int lane)
        {
            var c = BaseCenter(side);
            float dir = side == Side.Blue ? 1f : -1f;
            float x = c.X + dir * WallSystem.Radius;
            float y = TerrainMap.LaneY(x, lane) - c.Y;
            return (float)Math.Atan2(y, dir * 20f);
        }

        /// <summary>兼容旧签名(蓝方)。新代码用 LaneAngleOf(side, lane)。</summary>
        public static float LaneAngle(int lane) => LaneAngleOf(Side.Blue, lane);

        // ---- 每路的角半宽(按**真实车道几何**扫出来,缓存) ----

        private static readonly float[][] _laneHalf = { new float[3], new float[3] };
        private static readonly bool[][] _laneHalfReady = { new bool[3], new bool[3] };

        /// <summary>
        /// 某方某路在屏障半径处的**角度半宽**:屏障弧(半径 Radius)穿过该车道的那段角度的一半。
        /// 直接在弧上扫「哪些角度真的在车道上」(`TerrainMap.OnLane`),不拿固定角度估——
        /// 上下路是沿地图边缘的弧线,车道法向与径向不平行。
        /// 坑(2026-09-11 实测):不能从锚点角向两侧步进并在首次落空处停——
        /// `LaneAngleOf` 是用「x=基地±Radius 处的车道中心」定义的角度,而该角度对应的弧点 x 已经不同,
        /// 弧点未必落在车道上,一停就得到半宽 0。必须扫完整区间取真正在车道内的角度范围。
        /// 2026-09-11 制作人目视:"没有完全挡住上下路边缘" ⇒ 原实现半角=相邻锚点半距(±14.9°),
        /// 而车道在此半径处角半宽约 20°,每侧漏约 5°,敌人能从路边绕进去。
        /// </summary>
        public static float LaneHalfAngle(Side side, int lane)
        {
            int s = (int)side;
            if (_laneHalfReady[s][lane]) return _laneHalf[s][lane];
            var c = BaseCenter(side);
            const float step = 0.004f, span = 0.95f;
            float lo = float.NaN, hi = float.NaN;
            for (float d = -span; d <= span; d += step)
            {
                float a = LaneAngleOf(side, lane) + d;
                var p = new SimVec2(c.X + (float)Math.Cos(a) * Radius, c.Y + (float)Math.Sin(a) * Radius);
                if (!TerrainMap.OnLane(p, lane)) continue;
                if (float.IsNaN(lo)) lo = a;
                hi = a;
            }
            float half;
            if (float.IsNaN(lo))
            {
                half = (float)Math.Asin(Math.Min(1f, TerrainMap.LaneHalfWidth / Radius));   // 兜底:按车道半宽的几何估计
            }
            else
            {
                float anchor = LaneAngleOf(side, lane);
                float h1 = Math.Abs(AngleDiff(lo, anchor)), h2 = Math.Abs(AngleDiff(hi, anchor));
                half = (h1 > h2 ? h1 : h2);
            }
            half += 0.03f;                                  // 小余量,确保盖住路缘
            _laneHalf[s][lane] = half;
            _laneHalfReady[s][lane] = true;
            return half;
        }

        /// <summary>
        /// 五段中心角(弧度):**按车道实际覆盖划分**——
        /// 各路面板 = 整条车道(半角=LaneHalfAngle),草地面板 = 相邻两路之间的空档。
        /// 两个已知坑:
        /// ① 早期用"隔一个锚点"的距离当半角(29.8°)→ 草地段横扫整弧盖住中路段(三测试齐挂);
        /// ② 2026-09-11 制作人目视"红方全包围、蓝方范围正常" → **朴素角度平均跨 ±180° 出错**:
        ///    红方中路 180° 与上路 −120.5° 的平均给出 +29.75°(绕到圆的对侧)、半角被算成 75°(半圆级),
        ///    于是红方多出两片盖住对侧的巨幕,`OnWall/CanPass` 判定也跟着错(蓝方未跨界故只有红方出问题)。
        ///    故中点/角差一律走 `MidAngle`/`AngleDiff`(跨 ±180° 安全)。
        /// </summary>
        public static float PanelAngle(Side side, int panel)
        {
            float aTop = LaneAngleOf(side, 0), aMid = LaneAngleOf(side, 1), aBot = LaneAngleOf(side, 2);
            float hTop = LaneHalfAngle(side, 0), hMid = LaneHalfAngle(side, 1), hBot = LaneHalfAngle(side, 2);
            // 从上路经中路到下路的**遍历方向**:蓝方面向 +X 时角度递增;红方面向 −X 时整个弧是**角度递减**的
            // (上路 −120.5° → 中路 −180°(=180°) → 下路 −239.5°(=120.5°))。不区分方向会把红方草地段算成 99° 宽。
            float d = side == Side.Blue ? 1f : -1f;
            switch (panel)
            {
                case 0: return aTop;                                // 上路:整条车道
                case 1: return MidAngle(aTop + d * hTop, aMid - d * hMid);   // 上草地:两路之间
                case 2: return aMid;                                // 中路:整条车道
                case 3: return MidAngle(aMid + d * hMid, aBot - d * hBot);   // 下草地
                default: return aBot;                               // 下路
            }
        }

        /// <summary>两角的中点(跨 ±180° 安全:先把差归一到 (−π,π] 再取一半)。</summary>
        static float MidAngle(float a, float b)
        {
            return a + AngleDiff(b, a) * 0.5f;
        }

        /// <summary>有符号角差 a−b,归一化到 (−π, π]。</summary>
        static float AngleDiff(float a, float b)
        {
            float d = a - b;
            while (d > (float)Math.PI) d -= 2f * (float)Math.PI;
            while (d <= -(float)Math.PI) d += 2f * (float)Math.PI;
            return d;
        }

        /// <summary>五段各自半角:路面板=车道角半宽;草地面板=两路之间空档的一半。</summary>
        public static float PanelHalf(Side side, int panel)
        {
            float aTop = LaneAngleOf(side, 0), aMid = LaneAngleOf(side, 1), aBot = LaneAngleOf(side, 2);
            float hTop = LaneHalfAngle(side, 0), hMid = LaneHalfAngle(side, 1), hBot = LaneHalfAngle(side, 2);
            float d = side == Side.Blue ? 1f : -1f;   // 同 PanelAngle:遍历方向随方改变
            switch (panel)
            {
                case 0: return hTop;
                case 2: return hMid;
                case 4: return hBot;
                case 1: return Math.Abs(AngleDiff(aMid - d * hMid, aTop + d * hTop)) * 0.5f;
                default: return Math.Abs(AngleDiff(aBot - d * hBot, aMid + d * hMid)) * 0.5f;
            }
        }

        /// <summary>兼容旧调用(按"路"取段):0=上路段,1=中路段,2=下路段。</summary>
        public static float SegmentHalf(Side side, int lane)
        {
            return PanelHalf(side, lane == 0 ? 0 : (lane == 2 ? 4 : 2));
        }

        // ---- 血量状态(每方×3 段) ----
        private static readonly float[][] _hp = { new float[Panels], new float[Panels] };
        private static readonly bool[][] _broken = { new bool[Panels], new bool[Panels] };

        public static void Reset()
        {
            for (int s = 0; s < 2; s++)
                for (int l = 0; l < Panels; l++) { _hp[s][l] = WallHp; _broken[s][l] = false; }
            for (int s = 0; s < 2; s++)
                for (int l = 0; l < 3; l++) _laneHalfReady[s][l] = false;   // 重开时重算车道角宽
        }

        static WallSystem() => Reset();

        public static float Hp(Side side, int lane) => _hp[(int)side][lane];
        public static bool IsBroken(Side side, int lane) => _broken[(int)side][lane];

        /// <summary>某方基地中心。</summary>
        public static SimVec2 BaseCenter(Side side)
        {
            return new SimVec2(TerrainMap.BaseCenterX(side), TerrainMap.CenterY);
        }

        /// <summary>点 p 到该段弧心的角距(弧度);|Δ角|&gt;π 表示不在该段管辖。</summary>
        static float AngleTo(Side side, int panel, SimVec2 p)
        {
            var c = BaseCenter(side);
            float a = (float)Math.Atan2(p.Y - c.Y, p.X - c.X);
            float d = a - PanelAngle(side, panel);
            while (d > Math.PI) d -= 2f * (float)Math.PI;
            while (d < -Math.PI) d += 2f * (float)Math.PI;
            return d;
        }

        /// <summary>点是否在该段屏障的弧带内(角距 ≤ 段半角,且半径差 ≤ 墙半宽)。</summary>
        public static bool OnWall(Side side, int panel, SimVec2 p) => OnWall(side, panel, p, 0f);

        /// <summary>
        /// margin:外扩容差(米)。2026-09-13 制作人"磐石打不到屏障,还被挡着过不去":
        /// 单位被 `CanPass` 挡停时,位置正好落在墙带**边缘** —— 前进被挡(下一格在带内=不可穿),
        /// 而当前位置按不带容差的判定却"不在墙上" → 既打不着屏障、也过不去(边界 off-by-epsilon)。
        /// 攻击/阻挡判定加一点外扩即可让"贴墙的单位"能出刀。
        /// </summary>
        public static bool OnWall(Side side, int panel, SimVec2 p, float margin)
        {
            var c = BaseCenter(side);
            float r = SimVec2.Distance(c, p);
            if (Math.Abs(r - Radius) > WallHalf + margin) return false;
            return Math.Abs(AngleTo(side, panel, p)) <= PanelHalf(side, panel);
        }

        /// <summary>命中信息(owner=被打的守方,panel=段号)。2026-09-11 屏障特效:SimCore 据此发 WallDamaged/WallBroken。</summary>
        public struct WallHit { public Side Owner; public int Panel; }

        /// <summary>攻击者 e 对位置 p 出刀时命中的敌方完好墙段(无命中返回 null)。
        /// 命中带 = 薄壳外扩 `WallHitMargin`(见该常量注释:容差属于**命中侧**,不属于阻挡侧)。</summary>
        /// <summary>
        /// 2026-09-14(制作人:"攻击屏障时坠日要走到脸上才能攻击,他应该可以远程的"):
        /// 单位**武器射程内**是否存在敌方完好墙段 —— 有就能直接开打,不必贴到薄壳上。
        /// 判定= 沿"基地中心→单位"方向把命中点往外推到射程外沿,再用既有的命中带查询(薄壳外扩 margin)。
        /// </summary>
        public static WallHit? HitEnemyPanelWithin(Entity e, float range)
        {
            if (range <= 0f) return null;
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == e.Side) continue;
                var c = BaseCenter(owner);
                float len = SimVec2.Distance(c, e.PosF);
                if (len < 0.01f) continue;
                var dir = new SimVec2((e.PosF.X - c.X) / len, (e.PosF.Y - c.Y) / len);
                // 单位可能在墙内或墙外:取"该方向上离单位最近、且在射程内"的环上点
                float target = Radius + WallHitMargin;
                if (System.Math.Abs(len - target) > range) continue;      // 环不在射程内
                var p = new SimVec2(c.X + dir.X * target, c.Y + dir.Y * target);
                var hit = HitEnemyPanel(e, p);
                if (hit != null) return hit;
            }
            return null;
        }

        public static WallHit? HitEnemyPanel(Entity e, SimVec2 p) => HitEnemyPanel(e, p, WallHitMargin);

        /// <summary>同上,显式指定命中带外扩(测试要能分别量"薄壳内/命中带内")。</summary>
        public static WallHit? HitEnemyPanel(Entity e, SimVec2 p, float margin)
        {
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == e.Side) continue;
                for (int panel = 0; panel < Panels; panel++)
                {
                    if (IsBroken(owner, panel)) continue;
                    if (OnWall(owner, panel, p, margin)) return new WallHit { Owner = owner, Panel = panel };
                }
            }
            return null;
        }

        /// <summary>某段屏障弧的中心点(Sim 坐标;半径=Radius,方向=段中心角)。</summary>
        public static SimVec2 PanelCenterPos(Side side, int panel)
        {
            var c = BaseCenter(side);
            float a = PanelAngle(side, panel);
            return new SimVec2(c.X + (float)Math.Cos(a) * Radius, c.Y + (float)Math.Sin(a) * Radius);
        }

        /// <summary>对某段造成伤害(攻城/弹道结算处调用);归零 → 破。返回 true=本次伤害将其击破。</summary>
        public static bool Damage(Side side, int lane, float dmg)
        {
            int s = (int)side;
            if (_broken[s][lane]) return false;
            _hp[s][lane] -= dmg;
            if (_hp[s][lane] <= 0f) { _hp[s][lane] = 0f; _broken[s][lane] = true; return true; }
            return false;
        }

        /// <summary>
        /// 通行判定:单位 side 想从 p 穿过 owner 的城墙。
        /// 阻挡带 = **薄壳本身**(±WallHalf),单位可以一路走到墙皮上 —— 这才是"屏障占地多少就能走到多少"。
        /// 完好段:仅自家可过;破段:人人可过。返回 true=可通行。
        /// </summary>
        public static bool CanPass(Side side, SimVec2 p)
        {
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                for (int panel = 0; panel < Panels; panel++)
                {
                    if (!OnWall(owner, panel, p)) continue;
                    if (IsBroken(owner, panel)) return true;    // 该段已碎 → 该处敞开
                    if (owner == side) return true;             // 魔法屏障:自家自由穿行(无门洞)
                    return false;                               // 敌方:完好段被挡
                }
            }
            return true;                                          // 不在任何墙上
        }

        /// <summary>
        /// 带起点的通行判定:被 `SoftCollide`/`PushOutOfBuildings` 之类挤进薄壳的单位
        /// (这些位移**不过墙判定**)原本会永久卡死 —— 单帧步长只有 0.2~0.3 米、薄壳有 1.2 米宽,
        /// 之后每一步的落点都还在壳里 → 前进被挡、后退也被挡,一步都挪不出去。
        /// 这里放行"从壳内往外挪"的那一步(只判方向,不要求一步就出壳)。
        /// </summary>
        public static bool CanPass(Side side, SimVec2 from, SimVec2 to)
        {
            if (CanPass(side, to)) return true;
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == side) continue;
                var c = BaseCenter(owner);
                if (SimVec2.Distance(c, to) <= SimVec2.Distance(c, from)) continue;   // 只放行"向外"
                for (int panel = 0; panel < Panels; panel++)
                {
                    if (IsBroken(owner, panel)) continue;
                    if (OnWall(owner, panel, from)) return true;   // 起点已在壳内且这一步向外 → 放行
                }
            }
            return false;
        }

        /// <summary>城墙视野:点 p 是否被 owner 方任一完好段的"墙外 10 米"覆盖(段弧带外扩)。</summary>
        public static bool SeesThrough(Side owner, SimVec2 p)
        {
            var c = BaseCenter(owner);
            float r = SimVec2.Distance(c, p);
            for (int panel = 0; panel < Panels; panel++)
            {
                if (_broken[(int)owner][panel]) continue;
                if (Math.Abs(r - Radius) > Radius + VisionRadius) continue;
                if (Math.Abs(AngleTo(owner, panel, p)) <= PanelHalf(owner, panel) + VisionRadius / Radius) return true;
            }
            return false;
        }

        /// <summary>
        /// 攻击结算(2026-09-11 制作人裁决,不走摧毁/占领意图那套):
        /// 攻击者 e 对位置 p 出刀/开火时,若命中敌方完好墙段 → 直接扣墙血。
        /// 由 SimCore.ApplyAttack 在"范围内无敌方单位但撞着墙"时调用。
        /// 2026-09-11 重构:找段逻辑提取为 HitEnemyPanel(事件发射需要段号);行为不变。
        /// </summary>
        public static void ApplyHit(Entity e, SimVec2 p, float dmg)
        {
            var hit = HitEnemyPanel(e, p);
            if (hit != null) Damage(hit.Value.Owner, hit.Value.Panel, dmg);
        }

        /// <summary>攻击者是否顶在敌方完好墙段上(用于推进遇堵自动拆)。</summary>
        public static bool BlockedByWall(Entity e)
        {
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == e.Side) continue;
                for (int panel = 0; panel < Panels; panel++)
                {
                    if (IsBroken(owner, panel)) continue;
                    if (OnWall(owner, panel, e.PosF, WallHitMargin)) return true;   // 贴墙也算(见 OnWall margin)
                }
            }
            return false;
        }

        /// <summary>点击拾取:point 是否落在 player 的敌方完好墙段上(径向/角向都用点击容差,口径统一)。</summary>
        public static bool HitEnemyWall(SimCore sim, Side player, SimVec2 point)
        {
            if (sim == null || player == Side.Neutral) return false;
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == player) continue;
                for (int panel = 0; panel < Panels; panel++)
                {
                    if (IsBroken(owner, panel)) continue;
                    var c = BaseCenter(owner);
                    float r = SimVec2.Distance(c, point);
                    if (Math.Abs(r - Radius) > WallClickTolerance) continue;
                    if (Math.Abs(AngleTo(owner, panel, point)) <= PanelHalf(owner, panel) + WallClickTolerance / Radius)
                        return true;
                }
            }
            return false;
        }

        /// <summary>点击点吸附到最近可走的墙脚(行军目的地;沿半径方向退到墙皮外侧一点点)。
        /// 2026-09-14:原来退 6 米 —— 目的地在命中带外,军队走到那里只会站着,既不贴墙也打不到屏障。</summary>
        public static SimVec2 NearestWallPoint(Side attacker, SimVec2 point)
        {
            foreach (Side owner in new[] { Side.Blue, Side.Red })
            {
                if (owner == attacker) continue;
                var c = BaseCenter(owner);
                float len = SimVec2.Distance(c, point);
                if (len < 0.01f) continue;
                var dir = new SimVec2((point.X - c.X) / len, (point.Y - c.Y) / len);
                float rr = Radius + WallFootOffset;
                return new SimVec2(c.X + dir.X * rr, c.Y + dir.Y * rr);
            }
            return point;
        }
    }
}
