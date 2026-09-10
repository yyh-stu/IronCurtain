using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 2026-09-10 逐风骑乘三段动画接入(骑坐/冲锋/前刺,hy-3d-motion 文生动作),
// 生成 RiderAnimator.controller:state 0=骑坐(循环) 1=冲锋(循环) 2=前刺(单次)。
// 用法:菜单 IronCurtain/Generate Rider Animator,或批处理 -executeMethod。
public static class GenerateRiderAnimator
{
    private const string AnimDir = "Assets/Resources/Models/Heroes/Anims";
    private const string CtrlPath = "Assets/Resources/Models/Heroes/RiderAnimator.controller";

    [MenuItem("IronCurtain/Generate Rider Animator")]
    public static void Run()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        foreach (var f in new[] { "RiderIdle.fbx", "RiderCharge.fbx", "RiderThrust.fbx" })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(AnimDir + "/" + f);
            if (importer == null) { Debug.LogWarning("[rider] missing " + f); continue; }
            importer.animationType = (ModelImporterAnimationType)3; // Humanoid(Unity6 枚举名变更,数字兼容)
            importer.SaveAndReimport();
        }
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(CtrlPath);
        ctrl.AddParameter("state", AnimatorControllerParameterType.Int);
        var idle = FirstClip(AnimDir + "/RiderIdle.fbx");
        var charge = FirstClip(AnimDir + "/RiderCharge.fbx");
        var thrust = FirstClip(AnimDir + "/RiderThrust.fbx");
        if (idle == null || charge == null || thrust == null)
        {
            Debug.LogError("[rider] clip missing idle=" + (idle != null) + " charge=" + (charge != null) + " thrust=" + (thrust != null));
            return;
        }
        Loop(idle, true);
        Loop(charge, true);
        var sIdle = ctrl.AddMotion(idle);
        var sCharge = ctrl.AddMotion(charge);
        var sThrust = ctrl.AddMotion(thrust);
        ctrl.layers[0].stateMachine.defaultState = sIdle;
        Trans(ctrl, sIdle, sCharge, 1, false);
        Trans(ctrl, sIdle, sThrust, 2, false);
        Trans(ctrl, sThrust, sIdle, -1, true);  // 前刺不循环:播完回骑坐
        Trans(ctrl, sThrust, sCharge, 1, false);
        Trans(ctrl, sCharge, sIdle, 0, false);
        Trans(ctrl, sCharge, sThrust, 2, false);
        AssetDatabase.SaveAssets();
        Debug.Log("[rider] done controller=" + CtrlPath);
        if (Application.isBatchMode) EditorApplication.Exit(0);
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
