using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>D34 地面战争迷雾遮罩:整图逐格暗化平面(y=0.25,不遮天空/单位)。</summary>
    public sealed class FogView : MonoBehaviour
    {
        private SimCore _sim;
        private Side _player;
        private Texture2D _tex;
        private float _timer;
        private GameObject _quad;

        public Texture2D Tex { get { return _tex; } }

        public void Init(SimCore sim, Side player, Transform parent)
        {
            _sim = sim;
            _player = player;
            _tex = new Texture2D(LaneGrid.Cols, LaneGrid.Rows, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // 地面四边形:row0 ↔ 世界 z=3,uv(0,0) 在 (0,·,3)
            var verts = new[]
            {
                new Vector3(0f, 0.25f, 3f),
                new Vector3(LaneGrid.Cols, 0.25f, 3f),
                new Vector3(LaneGrid.Cols, 0.25f, 3f - LaneGrid.Rows),
                new Vector3(0f, 0.25f, 3f - LaneGrid.Rows)
            };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris;

            // URP 17 Unlit 透明配方(已验证)
            var m = new Material(ViewAssets.FindShader("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", _tex); m.SetColor("_BaseColor", Color.white);

            var fogGo = new GameObject("FogQuad");
            _quad = fogGo;
            fogGo.transform.SetParent(parent, false);
            fogGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            var fogMr = fogGo.AddComponent<MeshRenderer>();
            fogMr.sharedMaterial = m;
            // 2026-09-11 铺路时发现:迷雾平面在投影——地图上出现一大块台阶状暗影,
            // 边缘与迷雾锯齿完全一致。迷雾是纯覆盖层,不投影也不收影。
            fogMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            fogMr.receiveShadows = false;
            RefreshNow();
        }

        public void RefreshNow()
        {
            if (_sim == null || _tex == null) return;
            int rows = LaneGrid.Rows, cols = LaneGrid.Cols;
            var px = new Color32[rows * cols];
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < cols; col++)
                {
                    byte a = FogRules.FogAlpha(_sim, _player, row, col);
                    px[row * cols + col] = new Color32(16, 20, 33, a);
                }
            _tex.SetPixels32(px);
            _tex.Apply();
        }

        /// <summary>
        /// 2026-09-14 ★制作人"频繁切换阵营时全地图异常变暗"的真因:
        /// 迷雾平面是自建的独立物体 **FogQuad**,而换边拆除只按名字删 Board/FloatingText/**FogView**,
        /// FogQuad 不在名单里 → **每换一次边就留下一整张半透明暗色平面**(alpha 160),叠得越多地图越暗。
        /// 现随组件一起销毁(与 WallHpBars 同一类修法)。
        /// </summary>
        private void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
            _quad = null;
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= 0.5f)
            {
                _timer = 0f;
                RefreshNow();
            }
        }
    }
}
