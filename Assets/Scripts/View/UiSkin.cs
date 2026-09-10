using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 2026-09-10 UI 去 demo 味·共享皮肤(制作人拍板第一步):
    /// 木框白底九宫格面板贴图(Resources/UI/panel_wood,AI 生成)+微软雅黑动态字体。
    /// 各 OnGUI 面板用 UiSkin.Panel/UiSkin.Button 替换默认灰盒。
    /// </summary>
    public static class UiSkin
    {
        private static GUIStyle _panel;
        public static GUIStyle Panel
        {
            get
            {
                if (_panel != null) return _panel;
                var tex = UnityEngine.Resources.Load<Texture2D>("UI/panel_wood");
                _panel = new GUIStyle(GUI.skin.box);
                if (tex != null)
                {
                    // 九宫格:border=边框厚(图宽 8%≈82px),中段拉伸边框不变形
                    _panel.normal.background = MakeReadable(tex);
                    _panel.border = new RectOffset(82, 82, 82, 82);
                    _panel.padding = new RectOffset(96, 96, 86, 86);
                }
                return _panel;
            }
        }

        private static GUIStyle _button;
        public static GUIStyle Button
        {
            get
            {
                if (_button != null) return _button;
                var tex = UnityEngine.Resources.Load<Texture2D>("UI/button_wood");
                _button = new GUIStyle(GUI.skin.button);
                if (tex != null)
                {
                    _button.normal.background = MakeReadable(tex);
                    _button.hover.background = MakeReadable(tex);
                    _button.active.background = MakeReadable(tex);
                    _button.focused.background = MakeReadable(tex);
                    _button.border = new RectOffset(60, 60, 60, 60);
                    _button.padding = new RectOffset(70, 70, 40, 40);
                    _button.normal.textColor = Color.white;
                    _button.hover.textColor = Color.white;
                    _button.active.textColor = new Color(1f, 0.95f, 0.8f);
                    _button.fontStyle = FontStyle.Bold;
                }
                return _button;
            }
        }

        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font != null) return _font;
                _font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei", 14); // Windows 自带,零资产
                return _font;
            }
        }

        private static Material _hpSlate;
        /// <summary>血条贴图槽底(灰度木框渐变条×暗灰 tint;缺贴图返回 null 走纯色兜底)</summary>
        public static Material HpSlate()
        {
            if (_hpSlateChecked) return _hpSlate;
            _hpSlateChecked = true;
            var tex = UnityEngine.Resources.Load<Texture2D>("UI/hpbar_gray");
            if (tex == null) return null;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", new Color(0.42f, 0.40f, 0.38f)); // 暗暖灰 tint
            _hpSlate = m;
            return _hpSlate;
        }
        private static bool _hpSlateChecked;

        // Resources 贴图默认不可读,复制为可读实例给 GUIStyle
        private static Texture2D MakeReadable(Texture2D src)
        {
            var rt = new RenderTexture(src.width, src.height, 0);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var t = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            t.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
            t.Apply();
            RenderTexture.active = prev;
            rt.Release();
            return t;
        }
    }
}
