using NUnit.Framework;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEngine;

namespace IronCurtain.Tests
{
    /// <summary>特效公共基础 M1.1 定向测试:双时钟纯函数、对象池借还/超量/销毁、对局根会话与拆除。</summary>
    public sealed class VfxTests
    {
        [Test]
        public void 战斗钟_倍速缩放且暂停冻结()
        {
            Assert.AreEqual(0.1f, VfxDirector.ComputeBattleDelta(0.1f, false, 1f), 1e-4f, "×1 恒等");
            Assert.AreEqual(0.2f, VfxDirector.ComputeBattleDelta(0.1f, false, 2f), 1e-4f, "×2");
            Assert.AreEqual(0.8f, VfxDirector.ComputeBattleDelta(0.1f, false, 8f), 1e-4f, "×8");
            Assert.AreEqual(0f, VfxDirector.ComputeBattleDelta(0.1f, true, 8f), 1e-6f, "暂停冻结(倍速无效)");
            Assert.AreEqual(0.3f, VfxDirector.ComputeBattleDelta(0.1f, false, 3f), 1e-4f, "非档位倍速(截图脚本 3)同样成立");
        }

        [Test]
        public void 双时钟_暂停时战斗钟冻结而UI钟继续走()
        {
            // 审核意见 1-1:走 TickClock 真路径(显参注入),证明"暂停冻结"不是"倍速=0 的等价实现"
            var go = new GameObject("vfxClock");
            var vfx = go.AddComponent<VfxDirector>();
            vfx.Init(null);
            vfx.TickClockForTest(0.1f, 0.1f, paused: false, speed: 4f);
            Assert.AreEqual(0.4f, vfx.BattleDelta, 1e-4f, "未暂停:战斗钟=帧时×4");
            Assert.AreEqual(0.4f, vfx.BattleTime, 1e-4f, "战斗钟累计");
            Assert.AreEqual(0.1f, vfx.UiDelta, 1e-4f, "UI 钟 unscaled 原值");
            // 审核意见 M1-B1-R2 建议3:锁定显参重载与纯函数同形,防漂移
            Assert.AreEqual(VfxDirector.ComputeBattleDelta(0.1f, false, 4f), vfx.BattleDelta, 1e-6f,
                "TickClockForTest 与 ComputeBattleDelta 同形(漂移即失败)");
            vfx.TickClockForTest(0.1f, 0.1f, paused: true, speed: 4f);
            Assert.AreEqual(VfxDirector.ComputeBattleDelta(0.1f, true, 4f), vfx.BattleDelta, 1e-6f,
                "暂停分支同样与纯函数同形");
            Assert.AreEqual(0f, vfx.BattleDelta, 1e-6f, "暂停:战斗钟增量冻结");
            Assert.AreEqual(0.4f, vfx.BattleTime, 1e-4f, "暂停:战斗钟累计不再推进");
            Assert.AreEqual(0.1f, vfx.UiDelta, 1e-4f, "暂停:UI 钟仍在走(对偶行为)");
            vfx.Dispose();
            Object.DestroyImmediate(go);
        }

        [Test]
        public void 会话过期时在借对象随拆除安全回收()
        {
            // 审核意见 M1-B1 建议 4-2 的 EditMode 覆盖:标记在借(未完成淡出)时拆除,
            // 不抛异常、静态口清空 —— 与运行时 OnDisable→Dispose 同一条 Teardown 路径。
            var sim = new SimCore(new SimConfig(), new SeededRng(3))
                { Game = GameConfigParser.FromJson(Resources.Load<TextAsset>("gameconfig").text) };
            AppFlow.PlayerSide = Side.Blue;
            var go = new GameObject("vfxLife");
            var vfx = go.AddComponent<VfxDirector>();
            try
            {
                vfx.Init(null);
                var victim = sim.SpawnEntity(Side.Blue, EntityKind.Unit, new GridPos(80, 10));
                sim.Emit(new SimEvent(0, SimEventType.EntityDied, victim.Id));   // 己方单位:可见性闸放行 → 标记在借
                Assert.IsNotNull(vfx.Root, "拆除前根存在");
                vfx.Dispose();   // 会话拆除:在借标记随 Root/池安全回收(不抛 = 通过)
                Assert.IsNull(VfxDirector.Current, "静态口清空");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void 池_借还配对与超量降级()
        {
            int made = 0;
            var pool = new VfxPool(2, () => { made++; return new GameObject("p" + made); });
            var parent = new GameObject("poolParent").transform;
            try
            {
                var a = pool.Borrow(parent);
                var b = pool.Borrow(parent);
                Assert.IsNotNull(a);
                Assert.IsNotNull(b);
                Assert.AreEqual(2, made, "两次借出两次新建");
                Assert.AreEqual(2, pool.ActiveCount);

                var c = pool.Borrow(parent);   // 容量 2 且无归还可复用
                Assert.IsNull(c, "超容量应返回 null(降级路径),不排队不阻塞");

                pool.Return(a);
                Assert.AreEqual(1, pool.ActiveCount, "归还后活跃数下降");
                var d = pool.Borrow(parent);
                Assert.AreSame(a, d, "复用同一实例(不新建)");
                Assert.AreEqual(2, made, "复用不触发工厂");
                Assert.IsTrue(d.activeSelf, "借出即激活");

                pool.Return(b);
                pool.Return(d);
                Assert.AreEqual(0, pool.ActiveCount);
            }
            finally { Object.DestroyImmediate(parent.gameObject); pool.DisposeAll(); }
        }

        [Test]
        public void 对局根_会话自增与拆除清空()
        {
            var go = new GameObject("vfx1");
            var vfx = go.AddComponent<VfxDirector>();
            vfx.Init(null);   // 无 bootstrap/无 Sim:仅根/会话/时钟路径(战斗钟=×1 不暂停)
            long s1 = vfx.SessionId;
            Assert.IsNotNull(VfxDirector.Current, "静态口挂上");
            Assert.IsNotNull(vfx.Root, "对局根建立");
            vfx.TickClock(0.1f, 0.05f);
            Assert.AreEqual(0.1f, vfx.BattleDelta, 1e-4f, "无引导时 ×1");
            Assert.AreEqual(0.05f, vfx.UiDelta, 1e-4f, "UI 钟 unscaled 原值");

            var go2 = new GameObject("vfx2");
            var vfx2 = go2.AddComponent<VfxDirector>();
            vfx2.Init(null);
            Assert.AreEqual(s1 + 1, vfx2.SessionId, "会话代号严格自增(同卡/跨局句柄防复用的地基)");

            vfx.Dispose();   // EditMode 的 DestroyImmediate 不走生命周期回调,拆除须显式
            Assert.AreEqual(VfxDirector.Current, vfx2, "旧实例销毁不动新会话的静态口");
            vfx2.Dispose();
            Assert.IsNull(VfxDirector.Current, "最后一个实例拆除后静态口清空");
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
        }
    }
}
