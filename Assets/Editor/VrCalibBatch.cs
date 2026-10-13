using System.IO;
using System.Text;
using IronCurtain.Sim;
using IronCurtain.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace IronCurtain.EditorTools
{
    // VR02/VR03(2026-09-21 返工单)锚点标定:
    // ①在**已求值的待机姿态**下(Animator.Play(idle)+Update,非 MenuStage 静态采样)用 BakeMesh
    //   取手部蒙皮顶点世界包围盒 → 掌心语义锚点 → 换算手骨局部 SocketOffset 候选值;
    // ②磐石左前臂骨局部帧 + 盾候选档特写(挂骨从 LeftHand 改 LeftForeArm);
    // ③输出数值报告 + 候选 PNG,供配置表 HeroWeapons 填数与人工复核。
    // 用法:Unity.exe -batchmode -projectPath <proj> -executeMethod IronCurtain.EditorTools.VrCalibBatch.Run
    public static class VrCalibBatch
    {
        const string OutDir = "D:/DevTools/shots/vrcalib";
        const string Report = "Docs_VR/vrcalib_report.txt";

        public static void Run()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== VrCalibBatch " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ==");
            Directory.CreateDirectory(OutDir);

            // ---------- ① 镇岳:掌心锚点(已求值待机姿态) ----------
            {
                var inst = SpawnEvaluated("ZhenYue", "Anim");
                if (inst != null)
                {
                    try
                    {
                        var anim = inst.GetComponent<Animator>();
                        var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
                        var smr = inst.GetComponentInChildren<SkinnedMeshRenderer>();
                        int handIdx = System.Array.IndexOf(smr.bones, hand);
                        var baked = new Mesh();
                        smr.BakeMesh(baked);          // 当前(已求值)姿态的世界空间顶点(相对 SMR 物体)
                        var mv = baked.vertices;
                        var bw = smr.sharedMesh.boneWeights;
                        var b = new Bounds(Vector3.zero, Vector3.zero); bool any = false;
                        int n = 0;
                        for (int i = 0; i < mv.Length; i++)
                        {
                            float w = 0f; var x = bw[i];
                            if (x.boneIndex0 == handIdx) w += x.weight0;
                            if (x.boneIndex1 == handIdx) w += x.weight1;
                            if (x.boneIndex2 == handIdx) w += x.weight2;
                            if (x.boneIndex3 == handIdx) w += x.weight3;
                            if (w > 0.3f)
                            {
                                var wp = smr.transform.localToWorldMatrix.MultiplyPoint3x4(mv[i]);
                                if (!any) { b = new Bounds(wp, Vector3.zero); any = true; } else b.Encapsulate(wp);
                                n++;
                            }
                        }
                        Vector3 palmW = b.center;
                        Vector3 palmLocal = hand.InverseTransformPoint(palmW);
                        // 掌面法线候选:手部顶点平面 PCA —— 近似取包围盒最薄轴(世界系)
                        sb.AppendLine("[ZhenYue] 待机已求值 右手顶点=" + n + " 世界包围盒 c=" + b.center.ToString("F3") + " s=" + b.size.ToString("F3"));
                        sb.AppendLine("[ZhenYue] 掌心(世界)=" + palmW.ToString("F3"));
                        sb.AppendLine("[ZhenYue] ★SocketOffset(右手骨局部,建议直接填配置)=" + palmLocal.ToString("F4"));
                        sb.AppendLine("[ZhenYue] 手骨世界 pos=" + hand.position.ToString("F3") + " rot=" + hand.rotation.eulerAngles.ToString("F1"));
                        Object.DestroyImmediate(baked);
                    }
                    finally { Object.DestroyImmediate(inst); }
                }
            }

            // ---------- ② 磐石:左前臂局部帧 ----------
            {
                var inst = SpawnEvaluated("PanShi", "Anim");
                if (inst != null)
                {
                    try
                    {
                        var anim = inst.GetComponent<Animator>();
                        var fa = anim.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                        var hand = anim.GetBoneTransform(HumanBodyBones.LeftHand);
                        sb.AppendLine("[PanShi] 左前臂 pos=" + fa.position.ToString("F3") + " rot=" + fa.rotation.eulerAngles.ToString("F1"));
                        sb.AppendLine("[PanShi] 左前臂→左手 方向(世界)=" + (hand.position - fa.position).normalized.ToString("F3")
                            + " 长度=" + Vector3.Distance(fa.position, hand.position).ToString("F3"));
                        sb.AppendLine("[PanShi] 前臂局部帧轴:up=" + (fa.rotation * Vector3.up).normalized.ToString("F3")
                            + " fwd=" + (fa.rotation * Vector3.forward).normalized.ToString("F3"));
                        sb.AppendLine("[PanShi] 角色朝向=" + inst.transform.forward.ToString("F3") + " 上=" + Vector3.up.ToString("F1"));
                        // VR02:盾竖持 = 武器纵轴(socket +Y)对齐"世界竖直在前臂局部"的方向 → 等效欧拉
                        Vector3 vertLocal = Quaternion.Inverse(fa.rotation) * Vector3.up;
                        sb.AppendLine("[PanShi] 世界竖直(前臂局部)=" + vertLocal.ToString("F3"));
                        var qAlign = Quaternion.FromToRotation(Vector3.up, vertLocal.normalized);
                        sb.AppendLine("[PanShi] ★盾竖持 socket Rot 欧拉=" + qAlign.eulerAngles.ToString("F1"));
                        // 盾贴臂外侧面:前臂局部 X(外侧)方向(世界)
                        sb.AppendLine("[PanShi] 前臂局部 X(外侧方向,世界)=" + (fa.rotation * Vector3.right).ToString("F3"));
                    }
                    finally { Object.DestroyImmediate(inst); }
                }
            }

            // ---------- ②b 锤柄真实三维位置(VR03:锤头偏置 → 柄心≠包围盒截面中心) ----------
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Weapons/镇岳_大锤.fbx");
                if (prefab != null)
                {
                    var mf = prefab.GetComponentInChildren<MeshFilter>();
                    Mesh mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh == null)
                    {
                        // fbx 顶点在导入时不可直读:实例化后 BakeQuad
                        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                        var smr = tmp.GetComponentInChildren<SkinnedMeshRenderer>();
                        var fl = tmp.GetComponentInChildren<MeshFilter>();
                        if (smr != null) { mesh = new Mesh(); smr.BakeMesh(mesh); }
                        else if (fl != null) mesh = fl.sharedMesh;
                        Object.DestroyImmediate(tmp);
                    }
                    if (mesh != null)
                    {
                        var v = mesh.vertices;
                        var b = new Bounds(v[0], Vector3.zero);
                        foreach (var p in v) b.Encapsulate(p);
                        Vector3 size = b.size;
                        int ax = (size.x >= size.y && size.x >= size.z) ? 0 : ((size.y >= size.z) ? 1 : 2);
                        int a1 = (ax + 1) % 3, a2 = (ax + 2) % 3;
                        // 柄段 = 沿最长轴远离锤头的细端(顶点横截面最小):扫 8 段取"横截面半径最小"的连续 3 段
                        float bestR = float.MaxValue; float lo = 0f, hi = 0f;
                        for (int s = 0; s < 8; s++)
                        {
                            float l0 = Mathf.Lerp(0.05f, 0.60f, s / 8f), l1 = Mathf.Lerp(0.05f, 0.60f, (s + 1) / 8f);
                            float mn0 = float.MaxValue, mx0 = float.MinValue, mn1 = float.MaxValue, mx1 = float.MinValue; int n = 0;
                            for (int i = 0; i < v.Length; i++)
                            {
                                float t = (v[i][ax] - b.min[ax]) / size[ax];
                                if (t >= l0 && t < l1)
                                {
                                    mn0 = Mathf.Min(mn0, v[i][a1]); mx0 = Mathf.Max(mx0, v[i][a1]);
                                    mn1 = Mathf.Min(mn1, v[i][a2]); mx1 = Mathf.Max(mx1, v[i][a2]);
                                    n++;
                                }
                            }
                            if (n < 100) continue;
                            float r = (mx0 - mn0) + (mx1 - mn1);
                            if (r < bestR) { bestR = r; lo = l0; hi = l1; }
                        }
                        // 柄心 = 柄段顶点质心(三维,归一到包围盒比例)
                        Vector3 acc = Vector3.zero; int m = 0;
                        for (int i = 0; i < v.Length; i++)
                        {
                            float t = (v[i][ax] - b.min[ax]) / size[ax];
                            if (t >= lo && t < hi) { acc += v[i]; m++; }
                        }
                        Vector3 handle = acc / Mathf.Max(1, m);
                        Vector3 g = new Vector3(
                            (handle.x - b.min.x) / size.x, (handle.y - b.min.y) / size.y, (handle.z - b.min.z) / size.z);
                        sb.AppendLine("[镇岳_大锤] 最长轴=" + ax + " 包围盒 size=" + size.ToString("F3") + " 柄段[" + lo.ToString("F2") + "," + hi.ToString("F2") + "] 半径和=" + bestR.ToString("F3"));
                        sb.AppendLine("[镇岳_大锤] ★柄心 GripVec(包围盒比例)=" + g.ToString("F4"));
                    }
                }
            }

            // ---------- ⑤ 控制器 state 名枚举(动作采样前提:Play 的名字必须真实存在) ----------
            {
                var ctrl = EntityView.HeroAnimatorFor(HeroId.ZhenYue) as UnityEditor.Animations.AnimatorController;
                if (ctrl != null)
                {
                    var sm = ctrl.layers[0].stateMachine;
                    var names = new System.Text.StringBuilder();
                    foreach (var s in sm.states) names.Append(s.state.name).Append(" ");
                    sb.AppendLine("[Animator] HeroAnimator states: " + names);
                }
            }

            // ---------- ③ 候选特写(EntityView 正常链路 + 磐石盾挂前臂候选档) ----------
            if (!File.Exists("Assets/Scenes/Main.unity")) IronCurtainSceneTools.CreateMainScene();
            EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
            var bootGo = new GameObject("VrBoot");
            var boot = bootGo.AddComponent<GameBootstrap>();
            var awake = typeof(GameBootstrap).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (awake != null) awake.Invoke(boot, null);
            var cam = Camera.main;
            if (cam != null)
            {
                var warm = new RenderTexture(64, 64, 24);
                cam.targetTexture = warm; cam.Render(); Object.DestroyImmediate(warm); cam.targetTexture = null;

                // 镇岳:掌心 offset 直接采标定值(候选只扫 roll 微调)
                var zy = new Entity(92001, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                { Hero = HeroId.ZhenYue, State = EntityState.Idle, PosF = new SimVec2(20f, 8f), MaxHp = 100f };
                var goZy = new GameObject("Vr_ZhenYue");
                var vZy = goZy.AddComponent<EntityView>();
                vZy.Bind(zy, Side.Blue, HeroId.ZhenYue, null);
                // 特写也要已求值待机(返工单:不能拿绑定姿态当证据)
                var aZy = goZy.GetComponentInChildren<Animator>();
                if (aZy != null) { aZy.Play("Anim", 0, 0.25f); aZy.Update(0f); }
                var smrZy = goZy.GetComponentInChildren<SkinnedMeshRenderer>();
                sb.AppendLine("[特写确认] ZhenYue SMR mesh=" + (smrZy != null && smrZy.sharedMesh != null ? smrZy.sharedMesh.name : "(无)"));
                vZy.Tick(0.05f, cam);
                // VR03 接触报告:已求值骨骼后,socket 原点/掌心/锤柄的量化距离(判据≤身高1%≈2.3cm)
                var aZy2 = goZy.GetComponentInChildren<Animator>();
                var handZy = aZy2 != null ? aZy2.GetBoneTransform(HumanBodyBones.RightHand) : null;
                Transform wsZy = null, wpZy = null;
                foreach (var t in goZy.GetComponentsInChildren<Transform>())
                {
                    if (t.name.StartsWith("WS_镇岳")) wsZy = t;
                    if (t.name == "镇岳_大锤") wpZy = t;
                }
                if (handZy != null && wsZy != null && wpZy != null)
                {
                    Vector3 palm = handZy.TransformPoint(new Vector3(-0.0015f, 0.1238f, 0.0422f));
                    sb.AppendLine("[VR03接触] socket世界=" + wsZy.position.ToString("F3") + " 掌心世界=" + palm.ToString("F3")
                        + " 距离=" + Vector3.Distance(wsZy.position, palm).ToString("F4") + "m");
                    // 柄轴:武器局部纵轴上握点(0.55 比例,标定柄心 z)的世界位置
                    var bnds = wpZy.GetComponentInChildren<Renderer>().bounds;
                    sb.AppendLine("[VR03接触] 锤世界包围盒 c=" + bnds.center.ToString("F3") + " socket在包围盒内=" + bnds.Contains(wsZy.position));
                }
                Shot(cam, BoardView.WorldPos(zy.PosF), OutDir + "/zhenyue_grip_palm.png");
                Object.DestroyImmediate(goZy);

                // 磐石:盾挂前臂(Rot 用竖持对齐欧拉 × 滚转 4 档 × 外侧 offset 负号),逐档特写
                var ps = new Entity(92002, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                { Hero = HeroId.PanShi, State = EntityState.Idle, PosF = new SimVec2(22f, 8f), MaxHp = 100f };
                var alignEuler = new Vector3(314.8f, 45.3f, 270f);
                for (int roll = 0; roll < 4; roll++)
                {
                    var goPs = new GameObject("Vr_PanShi" + roll);
                    var vPs = goPs.AddComponent<EntityView>();
                    vPs.Bind(ps, Side.Blue, HeroId.PanShi, null);
                    var aPs = goPs.GetComponentInChildren<Animator>();
                    if (aPs != null) { aPs.Play("Anim", 0, 0.25f); aPs.Update(0f); }
                    Transform wsPs = null;
                    foreach (var t in goPs.GetComponentsInChildren<Transform>())
                        if (t.name.StartsWith("WS_磐石_盾")) wsPs = t;
                    if (wsPs != null)
                    {
                        // 外侧 = 前臂局部 -X(局部 +X 世界=(0.05,0.70,0.71) 朝上前方=身体内侧,推 -X 才向外)
                        wsPs.localPosition = new Vector3(-0.09f, 0.13f, 0f);
                        wsPs.localRotation = Quaternion.Euler(alignEuler) * Quaternion.AngleAxis(roll * 90f, Vector3.up);
                    }
                    vPs.Tick(0.05f, cam);
                    Shot(cam, BoardView.WorldPos(ps.PosF), OutDir + "/panshi_roll" + (roll * 90) + ".png");
                    Object.DestroyImmediate(goPs);
                }
                // 常规验证帧(配置原样,供对照)
                {
                    var goPs = new GameObject("Vr_PanShi_cfg");
                    var vPs = goPs.AddComponent<EntityView>();
                    vPs.Bind(ps, Side.Blue, HeroId.PanShi, null);
                    var aPs = goPs.GetComponentInChildren<Animator>();
                    if (aPs != null) { aPs.Play("Anim", 0, 0.25f); aPs.Update(0f); }
                    vPs.Tick(0.05f, cam);
                    Shot(cam, BoardView.WorldPos(ps.PosF), OutDir + "/panshi_shield_forearm.png");
                    Object.DestroyImmediate(goPs);
                }

                // ---------- ④ 动作期连续采样(VR02/VR03:走/攻击全时相,量化盾倾角+锤接触) ----------
                var states = new (string clip, float t, string tag)[] {
                    ("Anim 0", 0.00f, "walk0"), ("Anim 0", 0.33f, "walk1"), ("Anim 0", 0.66f, "walk2"),
                    ("Anim 1", 0.15f, "atk_windup"), ("Anim 1", 0.40f, "atk_hit"),
                    ("Anim 1", 0.70f, "atk_recover"), ("Anim 1", 0.95f, "atk_end"),
                };
                foreach (var heroId in new[] { HeroId.PanShi, HeroId.ZhenYue })
                {
                    string hname = heroId == HeroId.PanShi ? "panshi" : "zhenyue";
                    foreach (var st in states)
                    {
                        var ent = new Entity(92010 + (int)heroId, Side.Blue, EntityKind.Unit, new GridPos(2, 1), 100f)
                        { Hero = heroId, State = EntityState.Idle, PosF = new SimVec2(24f, 8f), MaxHp = 100f };
                        var go = new GameObject("Vr_act_" + hname);
                        var view = go.AddComponent<EntityView>();
                        view.Bind(ent, Side.Blue, heroId, null);
                        var a = go.GetComponentInChildren<Animator>();
                        SampleClipOn(go, st.clip, st.t);   // AnimationMode 直接采样(edit-mode 下 Play 不求值)
                        view.Tick(0.05f, cam);
                        // 量化:盾纵轴与竖直夹角;锤 socket-掌心距离
                        var an = go.GetComponentInChildren<Animator>();
                        if (heroId == HeroId.PanShi)
                        {
                            var fa = an != null ? an.GetBoneTransform(HumanBodyBones.LeftLowerArm) : null;
                            Transform ws = null;
                            foreach (var t in go.GetComponentsInChildren<Transform>())
                                if (t.name.StartsWith("WS_磐石_盾")) ws = t;
                            if (fa != null && ws != null)
                            {
                                float tilt = Vector3.Angle(ws.up, Vector3.up);
                                // 翻面检测:盾外侧方向(局部-X 系)= socket 的某轴;用盾位置-前臂位置方向近似朝外
                                Vector3 outward = (ws.position - fa.position).normalized;
                                sb.AppendLine("[动作] " + hname + "/" + st.tag + " 盾纵轴与竖直夹角=" + tilt.ToString("F1") + "° 前臂世界欧拉=" + fa.rotation.eulerAngles.ToString("F0"));
                            }
                        }
                        else
                        {
                            var hd = an != null ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;
                            Transform ws = null;
                            foreach (var t in go.GetComponentsInChildren<Transform>())
                                if (t.name.StartsWith("WS_镇岳")) ws = t;
                            if (hd != null && ws != null)
                                sb.AppendLine("[动作] " + hname + "/" + st.tag + " socket-掌心距离="
                                    + Vector3.Distance(ws.position, hd.TransformPoint(new Vector3(-0.0015f, 0.1238f, 0.0422f))).ToString("F4") + "m");
                        }
                        Shot(cam, BoardView.WorldPos(ent.PosF), OutDir + "/" + hname + "_" + st.tag + ".png");
                        Object.DestroyImmediate(go);
                    }
                }
            }
            Object.DestroyImmediate(bootGo);
            foreach (var v in Object.FindObjectsByType<EntityView>(FindObjectsSortMode.None))
                Object.DestroyImmediate(v.gameObject);

            Directory.CreateDirectory("Docs_VR");
            File.WriteAllText(Report, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("[vrcalib] 报告 Docs_VR/vrcalib_report.txt; 特写 " + OutDir);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        /// <summary>实例化英雄并求值待机姿态(真 Animator 求值,非静态绑定)。</summary>
        static GameObject SpawnEvaluated(string hero, string idleState)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Models/Heroes/" + hero + ".fbx");
            if (prefab == null) return null;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            var anim = inst.GetComponent<Animator>();
            if (anim == null) { Object.DestroyImmediate(inst); return null; }
            anim.runtimeAnimatorController = EntityView.HeroAnimatorFor(hero == "ZhenYue" ? HeroId.ZhenYue : HeroId.PanShi);
            anim.enabled = true;
            anim.Play(idleState, 0, 0.25f);
            anim.Update(0f);      // Update(0) 即求值当前时刻(旧版无 Animator.Sample)
            return inst;
        }


        /// <summary>edit-mode 下对实例直接采样动画 clip(Animator.Play 不驱动求值的替代路径)。</summary>
        static void SampleClipOn(GameObject go, string stateName, float t)
        {
            string fbx = stateName == "Anim" ? "IdleStand.fbx" : (stateName == "Anim 0" ? "HeroWalk.fbx" : "HeroAttack.fbx");
            var all = AssetDatabase.LoadAllAssetsAtPath("Assets/Resources/Models/Heroes/Anims/" + fbx);
            AnimationClip clip = null;
            foreach (var a2 in all) { var c = a2 as AnimationClip; if (c != null) { clip = c; break; } }
            if (clip == null) return;
            AnimationMode.StartAnimationMode();
            try { AnimationMode.SampleAnimationClip(go, clip, Mathf.Clamp01(t) * clip.length); }
            finally { AnimationMode.StopAnimationMode(); }
        }

        static void Shot(Camera cam, Vector3 worldPos, string path)
        {
            cam.transform.position = worldPos + new Vector3(-1.6f, 1.7f, -1.6f);
            cam.transform.LookAt(worldPos + Vector3.up * 1.25f);
            var rt = new RenderTexture(900, 900, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            RenderTexture.active = null; cam.targetTexture = null;
            Object.DestroyImmediate(tex); Object.DestroyImmediate(rt);
        }
    }
}
