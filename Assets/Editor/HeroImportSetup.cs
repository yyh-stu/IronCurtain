using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class HeroImportSetup
{
    private const string HeroDir = "Assets/Resources/Models/Heroes";
    private const string AnimDir = HeroDir + "/Anims";
    private const string CtrlPath = HeroDir + "/HeroAnimator.controller";

    [MenuItem("IronCurtain/Setup Hero Imports")]
    public static void Setup() { Run(); }

    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate); // 外部拷入的 fbx 先入资产库
        int count = 0;
        foreach (var dir in new[] { HeroDir, AnimDir })
        {
            foreach (var path in System.IO.Directory.GetFiles(dir, "*.fbx"))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path.Replace("\\", "/"));
                if (importer == null) continue;
                importer.animationType = (ModelImporterAnimationType)3; // Unity6 枚举名变更,数字=Humanoid
                importer.SaveAndReimport();
                count++;
            }
        }

        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
        ctrl.AddParameter("state", AnimatorControllerParameterType.Int);

        var idle = FirstClip(AnimDir + "/HeroIdle.fbx");
        var walk = FirstClip(AnimDir + "/HeroWalk.fbx");
        var attack = FirstClip(AnimDir + "/HeroAttack.fbx");
        if (idle != null) Loop(idle, true);
        if (walk != null) Loop(walk, true);

        var sIdle = ctrl.AddMotion(idle);
        var sWalk = ctrl.AddMotion(walk);
        var sAttack = ctrl.AddMotion(attack);
        ctrl.layers[0].stateMachine.defaultState = sIdle;

        Trans(ctrl, sIdle, sWalk, 1, false);
        Trans(ctrl, sIdle, sAttack, 2, false);
        Trans(ctrl, sAttack, sIdle, -1, true); // attack 不循环:ExitTime=1 回 idle
        Trans(ctrl, sAttack, sWalk, 1, false);  // 攻击中转走(替代 anyState)
        Trans(ctrl, sWalk, sIdle, 0, false);
        Trans(ctrl, sWalk, sAttack, 2, false);

        AssetDatabase.SaveAssets();
        Debug.Log("[HeroImportSetup] done files=" + count + " controller=" + CtrlPath);
    }

    private static AnimationClip FirstClip(string fbxPath)
    {
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>())
            if (clip != null && !clip.name.StartsWith("__preview__")) return clip;
        return null;
    }

    private static void Loop(AnimationClip clip, bool loop)
    {
        var s = AnimationUtility.GetAnimationClipSettings(clip);
        s.loopTime = loop;
        AnimationUtility.SetAnimationClipSettings(clip, s);
        EditorUtility.SetDirty(clip);
    }

    private static void Trans(AnimatorController ctrl, AnimatorState from, AnimatorState to, int cond, bool exitTime)
    {
        var t = from.AddTransition(to);
        t.duration = 0.1f;
        t.hasExitTime = exitTime;
        if (exitTime) t.exitTime = 1f;
        if (cond >= 0) t.AddCondition(AnimatorConditionMode.Equals, cond, "state");
    }
}
