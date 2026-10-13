using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// 2026-09-12 英雄动作用齐:站立待机(替掉六英雄共用的镇岳跪姿)+ 远程专属攻击(坠日拉弓/星落举杖)。
// 生成三份 controller(state: 0=待机 1=行走 2=攻击,与 EntityView.SetInteger 口径一致):
//   HeroAnimator(近战共用,攻击=镇岳挥砸) / ZhuiRiAnimator(拉弓) / XingLuoAnimator(举杖)
// 用法:Unity.exe -batchmode -projectPath <proj> -executeMethod IronCurtain.EditorTools.GenerateHeroAnimators.Run
namespace IronCurtain.EditorTools
{
    public static class GenerateHeroAnimators
    {
        private const string Dir = "Assets/Resources/Models/Heroes/Anims";
        private const string CtrlDir = "Assets/Resources/Models/Heroes";

        public static void Run()
        {
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            foreach (var f in new[] { "IdleStand.fbx", "HeroWalk.fbx", "HeroAttack.fbx", "ZhuiRiAttack.fbx", "XingLuoAttack.fbx" })
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(Dir + "/" + f);
                if (importer == null) { Debug.LogWarning("[heroanim] missing " + f); continue; }
                importer.animationType = (ModelImporterAnimationType)3;   // Humanoid(枚举名变更,数字兼容)
                importer.SaveAndReimport();
            }
            var idle = FirstClip(Dir + "/IdleStand.fbx");
            var walk = FirstClip(Dir + "/HeroWalk.fbx");
            var melee = FirstClip(Dir + "/HeroAttack.fbx");
            var bow = FirstClip(Dir + "/ZhuiRiAttack.fbx");
            var staff = FirstClip(Dir + "/XingLuoAttack.fbx");
            if (idle == null || walk == null || melee == null || bow == null || staff == null)
            {
                Debug.LogError("[heroanim] clip missing idle=" + (idle != null) + " walk=" + (walk != null)
                    + " melee=" + (melee != null) + " bow=" + (bow != null) + " staff=" + (staff != null));
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            // A02 修复(2026-09-18 GPT 审查):攻击不再 loop=true —— idle/walk 循环合理,
            // 攻击应该是"一击一周期"(进入→接触→收招)。非循环 + 攻击态自转换(exit time)重启,
            // 每次攻击有清晰起始帧,不再像持续摆手。
            Loop(idle, true); Loop(walk, true);
            Loop(melee, false); Loop(bow, false); Loop(staff, false);
            Build("HeroAnimator", idle, walk, melee);
            Build("ZhuiRiAnimator", idle, walk, bow);
            Build("XingLuoAnimator", idle, walk, staff);
            AssetDatabase.SaveAssets();
            Debug.Log("[heroanim] done: HeroAnimator(站立待机/行走/挥砸) + ZhuiRiAnimator(拉弓) + XingLuoAnimator(举杖)");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }

        private static void Build(string name, AnimationClip idle, AnimationClip walk, AnimationClip attack)
        {
            string path = CtrlDir + "/" + name + ".controller";
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(path);
            ctrl.AddParameter("state", AnimatorControllerParameterType.Int);
            ctrl.AddParameter("attack", AnimatorControllerParameterType.Trigger);   // A01:Sim 攻击同步
            var sIdle = ctrl.AddMotion(idle);
            var sWalk = ctrl.AddMotion(walk);
            var sAtk = ctrl.AddMotion(attack);
            ctrl.layers[0].stateMachine.defaultState = sIdle;
            // 三态互转(条件 = state 等于目标),全部循环
            Trans(ctrl, sIdle, sWalk, 1);
            Trans(ctrl, sIdle, sAtk, 2);
            Trans(ctrl, sWalk, sIdle, 0);
            Trans(ctrl, sWalk, sAtk, 2);
            Trans(ctrl, sAtk, sIdle, 0);
            Trans(ctrl, sAtk, sWalk, 1);
            // A02:攻击态自转换(非循环 clip 播完后即重启,保持连续攻击有清晰起始帧)
            var atkSelf = sAtk.AddTransition(sAtk);
            atkSelf.hasExitTime = true;
            atkSelf.exitTime = 1.0f;
            atkSelf.duration = 0f;
            // A01:攻击 Trigger 自转换(Sim 攻击冷却重置时 EntityView Fire → 立即从第一帧重播)
            var atkTrig = sAtk.AddTransition(sAtk);
            atkTrig.hasExitTime = false;
            atkTrig.duration = 0f;
            atkTrig.AddCondition(AnimatorConditionMode.If, 0f, "attack");
            Debug.Log("[heroanim] built " + path);
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

        private static void Trans(AnimatorController ctrl, AnimatorState from, AnimatorState to, int cond)
        {
            var t = from.AddTransition(to);
            t.duration = 0.12f;
            t.hasExitTime = false;
            t.AddCondition(AnimatorConditionMode.Equals, cond, "state");
        }
    }
}
