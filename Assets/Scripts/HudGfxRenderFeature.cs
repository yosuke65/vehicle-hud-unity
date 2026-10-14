using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP version of the overlay hook. Camera.AddCommandBuffer doesn't run under
/// URP, so the plugin's overlay event (ID 1) is issued from a Render Graph
/// pass after the opaque geometry. It's an unsafe pass because the plugin
/// event needs a plain CommandBuffer (IssuePluginEvent); the pass binds the
/// camera's active color target first, so on Vulkan the plugin's
/// EnsureInside config draws into that target's render pass.
/// </summary>
public class HudGfxRenderFeature : ScriptableRendererFeature
{
    private const int OverlayEventId = 1;

    private class OverlayPass : ScriptableRenderPass
    {
        private class PassData
        {
            public IntPtr renderEvent;
            public TextureHandle target;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var resources = frameData.Get<UniversalResourceData>();
            using (var builder = renderGraph.AddUnsafePass<PassData>("HudGfx overlay (URP)", out var data))
            {
                data.renderEvent = HudGfxBridge.RenderEventFunc;
                data.target = resources.activeColorTexture;
                builder.UseTexture(data.target, AccessFlags.ReadWrite);
                // Render Graph culls passes whose output nobody reads; this
                // one only has a side effect in native code.
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData d, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    cmd.SetRenderTarget(d.target);
                    cmd.IssuePluginEvent(d.renderEvent, OverlayEventId);
                });
            }
        }
    }

    private OverlayPass pass;

    public override void Create()
    {
        pass = new OverlayPass { renderPassEvent = RenderPassEvent.AfterRenderingOpaques };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (HudGfxBridge.RenderEventFunc != IntPtr.Zero && renderingData.cameraData.cameraType == CameraType.Game)
        {
            renderer.EnqueuePass(pass);
        }
    }
}
