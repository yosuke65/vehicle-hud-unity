using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Switches the project to URP and exports, in batch mode:
//   Unity -batchmode -quit -projectPath ... -executeMethod UrpSetup.ConfigureAndExport
// - a URP asset with MSAA off and HDR off: the plugin's overlay pipeline is
//   built for one sample (hudgfx.cpp), and the built-in version ran without
//   HDR on the Android quality level too
// - a Universal Renderer with HudGfxRenderFeature, which issues the plugin's
//   overlay event from a Render Graph pass after opaques
// - the scene rebuilt with URP Lit materials (SceneSetup picks the shader)
public static class UrpSetup
{
    private const string Dir = "Assets/Settings";
    private const string AssetPath = Dir + "/HudUrp.asset";
    private const string RendererPath = Dir + "/HudUrp_Renderer.asset";

    public static void ConfigureAndExport()
    {
        Configure();
        SceneSetup.BuildCockpitScene();
        SceneSetup.ConfigureAndroidExportVulkanAndBuild();
    }

    public static void Configure()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets", "Settings");

        var rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
        AssetDatabase.CreateAsset(rendererData, RendererPath);
        var feature = ScriptableObject.CreateInstance<HudGfxRenderFeature>();
        feature.name = "HudGfxRenderFeature";
        AssetDatabase.AddObjectToAsset(feature, rendererData);
        rendererData.rendererFeatures.Add(feature);
        EditorUtility.SetDirty(rendererData);

        var asset = UniversalRenderPipelineAsset.Create(rendererData);
        asset.msaaSampleCount = 1;
        asset.supportsHDR = false;
        AssetDatabase.CreateAsset(asset, AssetPath);
        AssetDatabase.SaveAssets();

        GraphicsSettings.defaultRenderPipeline = asset;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
        Debug.Log($"UrpSetup: URP asset {AssetPath}, MSAA {asset.msaaSampleCount}, HDR {asset.supportsHDR}, " +
                  $"features {rendererData.rendererFeatures.Count}");
    }
}
