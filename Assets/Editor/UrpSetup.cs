using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace IronCurtain.EditorTools
{
    // 一键创建 URP 管线资产并切换 GraphicsSettings/QualitySettings
    public static class UrpSetup
    {
        public static void Create()
        {
            Directory.CreateDirectory("Assets/URP");
            var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
            AssetDatabase.CreateAsset(rendererData, "Assets/URP/UniversalRenderer.asset");
            var pipeline = UniversalRenderPipelineAsset.Create(rendererData);
            AssetDatabase.CreateAsset(pipeline, "Assets/URP/URPAsset.asset");
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            AssetDatabase.SaveAssets();
            Debug.Log("[URP] pipeline asset created and applied");
        }
    }
}
