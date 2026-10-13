using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IronCurtain.EditorTools
{
    /// <summary>
    /// 启动守卫:无头批处理(构建/跑测试)会把"上次打开的场景"记录 `Library/LastSceneManagerSetup.txt`
    /// 写成空,下次**正常启动**工程就落在一个**无标题空场景**上 —— 制作人的原话是
    /// "桌面快捷方式打开项目后,连主菜单都不是"。
    ///
    /// 2026-09-14 修:旧判据要求"当前场景 0 个物体且未改动",而 Unity 的默认新场景自带
    /// Main Camera + Directional Light(rootCount=2)→ **永远判不出空工程**,守卫等于没生效。
    /// 现在改成看**记录文件本身**:记录里没有 `.unity` 路径 = 上次会话没记下场景 → 才介入,
    /// 这样既不会误伤"用户自己新建的无标题场景",也能覆盖批处理写空的真实情况。
    /// </summary>
    [InitializeOnLoad]
    public static class DefaultSceneOnLaunch
    {
        private const string MenuScene = "Assets/Scenes/Menu.unity";
        private const string Record = "Library/LastSceneManagerSetup.txt";

        private static double _deadline;
        private static bool _armed;

        static DefaultSceneOnLaunch()
        {
            if (Application.isBatchMode)
            {
                // 无头实例(构建/跑测试)会把"上次打开的场景"记录写空 —— 退出前把它写回主界面场景,
                // 让**任何**无头运行都自我修复(测试入口 `-runTests` 没有可挂的代码钩子,只能靠这一手)。
                EditorApplication.quitting += WriteRecordFile;
                return;
            }
            _deadline = EditorApplication.timeSinceStartup + 20.0;   // 只在启动窗口内介入
            _armed = true;
            EditorApplication.update += Watch;
        }

        private static void Watch()
        {
            if (!_armed) return;
            if (EditorApplication.timeSinceStartup > _deadline) { Disarm(); return; }
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Disarm(); return; }
            if (!System.IO.File.Exists(MenuScene)) { Disarm(); return; }
            if (!ShouldOpenMenu(RecordTextOrNull, SceneManager.GetActiveScene().path)) return;

            Disarm();
            EditorSceneManager.OpenScene(MenuScene);
            Debug.Log("[DefaultSceneOnLaunch] 上次打开的场景记录为空(无头批处理写空)→ 已打开 " + MenuScene);
        }

        /// <summary>
        /// 介入判据(纯函数,便于用批处理自检入口逐种情形验证,别靠"应该没问题"):
        /// ①当前已经开着真实场景 → 不介入;②记录里有场景 → 交给编辑器自己恢复;
        /// ③记录里没有任何 `.unity` 路径(无头批处理写空)且当前是无标题场景 → 打开主界面场景。
        /// </summary>
        public static bool ShouldOpenMenu(string recordText, string activeScenePath)
        {
            if (!string.IsNullOrEmpty(activeScenePath)) return false;
            if (recordText != null && recordText.IndexOf(".unity", System.StringComparison.Ordinal) >= 0) return false;
            return true;
        }

        private static string RecordTextOrNull
        {
            get
            {
                try { return System.IO.File.Exists(Record) ? System.IO.File.ReadAllText(Record) : null; }
                catch { return null; }
            }
        }

        /// <summary>
        /// 批处理自检入口:把"记录为空 / 记录有场景 / 已开真实场景"三种输入喂给判据并打印结论,
        /// 最后把启动场景修回主界面(顺带保证自检本身不把记录留坏)。
        /// 用法:`... -executeMethod IronCurtain.EditorTools.DefaultSceneOnLaunch.SelfCheck -quit`
        /// </summary>
        public static void SelfCheck()
        {
            Debug.Log("[DefaultSceneOnLaunch] 判据自检:"
                + " 记录为空+无标题场景→打开主界面=" + ShouldOpenMenu("sceneSetups: []", "")
                + " | 记录有场景→不介入=" + ShouldOpenMenu("sceneSetups:\n- path: Assets/Scenes/Menu.unity\n", "")
                + " | 已开真实场景→不介入=" + ShouldOpenMenu(null, "Assets/Scenes/Main.unity")
                + " | 记录为空但已开真实场景→不介入=" + ShouldOpenMenu("sceneSetups: []", "Assets/Scenes/Bp.unity"));
            RepairStartupScene();
        }

        private static void Disarm()
        {
            _armed = false;
            EditorApplication.update -= Watch;
        }

        /// <summary>
        /// 人工修复入口(批处理可直接调用):把"上次打开的场景"记录恢复成主界面场景 —— 退出时由编辑器
        /// 自己写入记录,所以这是"用 Unity 的格式写回去"的正规做法。
        /// 用法:`Unity.exe -batchmode -projectPath &lt;proj&gt; -executeMethod IronCurtain.EditorTools.DefaultSceneOnLaunch.RepairStartupScene -quit`
        /// </summary>
        public static void RepairStartupScene()
        {
            if (!System.IO.File.Exists(MenuScene))
            {
                Debug.LogError("[DefaultSceneOnLaunch] 找不到 " + MenuScene + ",无法修复启动场景");
                return;
            }
            EditorSceneManager.OpenScene(MenuScene);   // 本次会话也切到主界面场景
            WriteRecordFile();
        }

        /// <summary>
        /// 按 Unity 自己的格式把启动场景记录写成主界面场景。**直接写文件**,不依赖编辑器退出时的写入:
        /// 实测同一条批处理命令两次,一次留下 "Assets/Scenes/Menu.unity"、一次又留空 —— 退出写入时机不可靠。
        /// 无头实例在 `EditorApplication.quitting` 里也会调用本方法(任何无头运行都自我修复)。
        /// </summary>
        private static void WriteRecordFile()
        {
            try
            {
                if (!System.IO.File.Exists(MenuScene)) return;
                string guid = AssetDatabase.AssetPathToGUID(MenuScene);
                System.IO.File.WriteAllText(Record,
                    "sceneSetups:\n- path: " + MenuScene + "\n  guid: " + guid
                    + "\n  isLoaded: 1\n  isActive: 1\n  isSubScene: 0\n");
                Debug.Log("[DefaultSceneOnLaunch] 启动场景记录已写为 " + MenuScene + "(guid " + guid + ")");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[DefaultSceneOnLaunch] 写启动场景记录失败:" + e.Message);
            }
        }
    }
}
