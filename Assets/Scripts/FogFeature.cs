using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class FogFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class FogSettings
    {
        public Material fogMaterial = null;
    }

    public FogSettings settings = new FogSettings();
    private FogPass customFogPass;

    public override void Create()
    {
        if (settings.fogMaterial == null)
        {
            Debug.LogWarning(
                "FogFeature: Fog Material is not assigned in the settings. Feature will not run."
            );
            return;
        }
        // customFogPass = new FogPass(settings.fogMaterial);
        // customFogPass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData
    )
    {
        if (settings.fogMaterial == null || customFogPass == null)
        {
            return;
        }

        if (customFogPass == null)
        {
            customFogPass = new FogPass(settings.fogMaterial);
        }

        renderer.EnqueuePass(customFogPass);
    }
}