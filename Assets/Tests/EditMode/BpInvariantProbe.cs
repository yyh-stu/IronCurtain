using IronCurtain.View;
using NUnit.Framework;

namespace IronCurtain.Tests
{
    public class BpInvariantProbe
    {
        [Test]
        public void 纯超时自动路径_五百种子_AI角色覆盖不变量()
        {
            int bad = 0;
            for (int seed = 0; seed < 500; seed++)
            {
                var s = new BpSession(seed);
                int guard = 0;
                while (s.State != BpSession.Phase.Done && guard++ < 500) s.Tick(0.5f);   // 无玩家点击:全部超时自动
                var roles = new System.Collections.Generic.HashSet<string>();
                foreach (var h in s.Red) roles.Add(BpSession.RoleOf(h));
                if (roles.Count != 3 || s.Red.Count != 3) { bad++; if (bad <= 3) TestContext.Out.WriteLine("seed=" + seed + " red=" + string.Join(",", s.Red)); }
            }
            Assert.AreEqual(0, bad, "存在违反角色覆盖/去重的种子");
        }
    }
}
