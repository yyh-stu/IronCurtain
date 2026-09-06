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
                new Vector3(LaneGrid.Cols, 0.25f, -27f),
                new Vector3(0f, 0.25f, -27f)
            };
            var uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };
            var mesh = new Mesh();
            mesh.vertices = verts;
            mesh.uv = uv;
            mesh.triangles = tris;

            // URP 17 Unlit 透明配方(已验证)
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.SetTexture("_BaseMap", _tex); m.SetColor("_BaseColor", Color.white);

            var fogGo = new GameObject("FogQuad");
            fogGo.transform.SetParent(parent, false);
            fogGo.AddComponent<MeshFilter>().sharedMesh = mesh;
            fogGo.AddComponent<MeshRenderer>().sharedMaterial = m;
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
