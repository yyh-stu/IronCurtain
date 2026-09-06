using System.Collections.Generic;
using IronCurtain.Sim;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>M6.2 伤害飘字渲染:监听 DamageDealt → Model;Update 用对象池 TextMesh 上浮渐隐(缩放兜底)。</summary>
    public sealed class FloatingTextView : MonoBehaviour
    {
        private SimCore _sim;
        private Side _side;
        private BoardView _board;
        private readonly FloatingTextModel _model = new FloatingTextModel();
        private readonly List<TextMesh> _pool = new List<TextMesh>();

        public void Init(SimCore sim, Side playerSide, BoardView board)
        {
            _sim = sim;
            _side = playerSide;
            _board = board;
            sim.AddEventListener(OnEvent);
        }

        private void OnEvent(SimEvent ev)
        {
            if (ev.Type != SimEventType.DamageDealt) return;
            if (_model.TryAdd(_sim, _side, ev.SubjectId, ev.Amount))
                _board.GetView(ev.SubjectId)?.FlashHit();
        }

        /// <summary>当前存活飘字条数(截图验收程序化核对用)。</summary>
        public int ActiveCount => _model.Items.Count;

        private void Update() => RenderFrame(Time.deltaTime);

        /// <summary>渲染一帧(公开给 CaptureBatch 离屏截图喂 dt;Play 下由 Update 驱动)。</summary>
        public void RenderFrame(float dt)
        {
            _model.Advance(dt);
            for (int i = 0; i < _model.Items.Count; i++) Render(i, _model.Items[i]);
            for (int i = _model.Items.Count; i < _pool.Count; i++)
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
        }

        private void Render(int slot, FloatItem item)
        {
            TextMesh tm;
            if (slot < _pool.Count) tm = _pool[slot];
            else
            {
                var go = new GameObject("FloatText");
                go.transform.SetParent(transform, false);
                tm = go.AddComponent<TextMesh>();
                tm.font = ViewAssets.UiFont(14);
                tm.fontSize = 30;
                tm.characterSize = 0.16f;
                tm.fontStyle = FontStyle.Bold;
                tm.anchor = TextAnchor.MiddleCenter;
                tm.GetComponent<MeshRenderer>().sharedMaterial = ViewAssets.Flat(Color.white);
                _pool.Add(tm);
            }
            if (!tm.gameObject.activeSelf) tm.gameObject.SetActive(true);
            var view = _board.GetView(item.EntityId);
            float topY = view != null ? view.TextTopY : (_sim.GetEntity(item.EntityId) != null
                && _sim.GetEntity(item.EntityId).Kind == EntityKind.Building ? 13.0f : 2.9f);
            var pos = BoardView.WorldPos(item.Origin);
            tm.transform.position = new Vector3(pos.x, topY + FloatingTextModel.RiseOffset(item.Age), pos.z);
            float s = Mathf.Max(0.01f, FloatingTextModel.ScaleOf(item.Age));
            tm.transform.localScale = Vector3.one * s;
            tm.color = item.ColorKind == 0 ? new Color(1f, 0.35f, 0.3f) : new Color(1f, 0.9f, 0.3f);
            if (tm.text != item.Text) tm.text = item.Text;
        }
    }
}
