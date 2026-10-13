using System.Collections.Generic;
using UnityEngine;

namespace IronCurtain.View
{
    /// <summary>
    /// 按效果族的最小对象池(M1.1)。借还配对、超量返回 null(= 调用方走降级路径,静默丢弃装饰级效果)。
    /// 归还时重置缩放/父级;材质与 MPB 由效果方自理(常驻禁令:命中不新建材质 → 用 MaterialPropertyBlock)。
    /// 纯 C# 结构,不依赖 Time,可定向测试。容量按 README §7 初始预算由调用方声明,池不私扩。
    /// </summary>
    public sealed class VfxPool
    {
        private readonly Stack<GameObject> _idle = new Stack<GameObject>();
        private readonly System.Func<GameObject> _factory;
        public int Capacity { get; }
        public int ActiveCount { get; private set; }
        public int CreatedCount { get; private set; }

        public VfxPool(int capacity, System.Func<GameObject> factory)
        {
            Capacity = capacity;
            _factory = factory;
        }

        /// <summary>借出一个实例;超容量且无可复用 → null(调用方降级,不排队不阻塞)。</summary>
        public GameObject Borrow(Transform parent)
        {
            GameObject go;
            if (_idle.Count > 0)
            {
                go = _idle.Pop();
                if (go == null) return Borrow(parent);   // 被 Unity 销毁的残片,弹下一个
            }
            else if (CreatedCount < Capacity)
            {
                go = _factory();
                CreatedCount++;
            }
            else return null;
            go.transform.SetParent(parent, false);
            go.SetActive(true);
            ActiveCount++;
            return go;
        }

        /// <summary>归还:缩放复位、离树收起。</summary>
        public void Return(GameObject go)
        {
            if (go == null) return;
            go.transform.localScale = Vector3.one;
            go.transform.SetParent(null, false);
            go.SetActive(false);
            ActiveCount--;
            _idle.Push(go);
        }

        /// <summary>整池销毁(换边/重开调用;预热容量允许保留的口径在 M2 按预算定,当前先全清)。
        /// EditMode 下用 DestroyImmediate(Destroy 在编辑器会报错,见定向测试)。</summary>
        public void DisposeAll()
        {
            while (_idle.Count > 0)
            {
                var go = _idle.Pop();
                if (go != null)
                {
                    if (Application.isPlaying) Object.Destroy(go);
                    else Object.DestroyImmediate(go);
                }
            }
            ActiveCount = 0;
            CreatedCount = 0;
        }
    }
}
