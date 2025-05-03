using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class FogPass : ScriptableRenderPass
{
    private Material fogMaterial;
    private string profilerTag;
    private RTHandle source { get; set; }
    private RTHandle tempTextureHandle;

    private static readonly int FogColorID = Shader.PropertyToID("_FogColor");
    private static readonly int FogDensityID = Shader.PropertyToID("_FogDensity");
    private static readonly int FogStartDistanceID = Shader.PropertyToID("_FogStartDistance");
    private static readonly int FogEndDistanceID = Shader.PropertyToID("_FogEndDistance");
    private static readonly int HeightFogBaseID = Shader.PropertyToID("_HeightFogBase");
    private static readonly int HeightFogFalloffID = Shader.PropertyToID("_HeightFogFalloff");
    private const string LinearFogKeyword = "USE_LINEAR_FOG";

    public FogPass(Material material, string tag = "FogPass")
    {
        this.profilerTag = tag;
        this.fogMaterial = material;
        renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
    }

    public override void Configure(
        CommandBuffer cmd,
        RenderTextureDescriptor cameraTextureDescriptor
    )
    {
        ConfigureTarget(source);
        // ConfigureClear(ClearFlag.None, Color.clear);
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (fogMaterial == null)
        {
            Debug.LogError("FogPass: Fog Material is not assigned.");
            return;
        }

        ref CameraData cameraData = ref renderingData.cameraData;

        if (!cameraData.postProcessEnabled || cameraData.isSceneViewCamera)
            return;

        RTHandle sourceHandle = cameraData.renderer.cameraColorTargetHandle;

        if (sourceHandle == null)
        {
            // Debug.LogWarning($"FogPass: Invalid cameraColorTargetHandle for camera {cameraData.camera.name}. Skipping fog.");
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

        bool useLinear = fogMaterial.IsKeywordEnabled(LinearFogKeyword);
        CoreUtils.SetKeyword(fogMaterial, LinearFogKeyword, useLinear);

        Blitter.BlitCameraTexture(cmd, sourceHandle, sourceHandle, fogMaterial, 0);

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void FrameCleanup(CommandBuffer cmd) { }
}