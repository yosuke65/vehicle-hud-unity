using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Drives the native rendering plugin (libhudgfx.so, source in
/// vehicle-hud/unity-plugin). Created automatically after the first scene
/// loads, so no scene edit is needed. Two plugin events per frame, both run
/// on Unity's render thread:
/// - event 2 (in Update, before the scene renders): the plugin draws the
///   speed dial into a RenderTexture, which a panel above the car shows.
/// - event 1: the plugin draws a small dial into the camera's frame. By
///   default it's in a CommandBuffer on the main camera, right after opaque
///   geometry (UseCameraCommandBuffer); otherwise at the end of the frame.
/// - events 3 and 4: the plugin creates its own texture (event 3), C# wraps
///   it with Texture2D.CreateExternalTexture, and the plugin draws into it
///   every frame (event 4). Shown on the left panel.
/// Also a dial drawn by a Unity ComputeShader (Resources/HudDialCompute),
/// dispatched from C# each frame, on the right panel. The middle panel is
/// the plugin drawing into a Unity RenderTexture (event 2).
/// The needle's speed doesn't come from here: the host app pushes it straight
/// into the plugin over JNI.
/// </summary>
public class HudGfxBridge : MonoBehaviour
{
#if UNITY_ANDROID && !UNITY_EDITOR
    [DllImport("hudgfx")]
    private static extern IntPtr HudGfx_GetRenderEventFunc();

    [DllImport("hudgfx")]
    private static extern int HudGfx_CurrentThreadId();

    [DllImport("hudgfx")]
    private static extern void HudGfx_SetScreenSize(int width, int height);

    [DllImport("hudgfx")]
    private static extern void HudGfx_SetDialTexture(IntPtr texture, int width, int height);

    [DllImport("hudgfx")]
    private static extern IntPtr HudGfx_GetExternalTexture();

    [DllImport("hudgfx")]
    private static extern void HudGfx_SetExternalTexture(IntPtr texture);

    [DllImport("hudgfx")]
    private static extern float HudGfx_GetSpeedKph();
#endif

    private const int OverlayEventId = 1;
    private const int TextureEventId = 2;
    private const int TextureSize = 512;
    private const int CreateExternalEventId = 3;
    private const int ExternalEventId = 4;
    private const int ExternalSize = 256;  // kExternalSize in hudgfx.cpp
    private const int ComputeSize = 256;
    private const float MaxSpeedKph = 180f;  // same full scale as the plugin
    private const float NeedleTauSeconds = 0.12f;
    private const float PanelSize = 0.6f;
    private const float PanelSpacing = 0.7f;

    // A cluster redraws a moving needle, so render at the display's 60 Hz.
    // Without this Unity uses its Android default, 30 fps (measured as about
    // 30 render events a second before 2026-10-14).
    private const int TargetFrameRate = 60;

    // true: issue the overlay event from a CommandBuffer at
    // CameraEvent.AfterForwardOpaque, so it runs inside the camera's own
    // render pass, before transparents and UI. false: after
    // WaitForEndOfFrame, on top of everything (Unity's NativeRenderingPlugin
    // sample does this).
    private const bool UseCameraCommandBuffer = true;

    private IntPtr renderEvent;
    private RenderTexture dialTexture;
    private Transform panels;
    private Texture2D externalTexture;
    private ComputeShader dialCompute;
    private int dialKernel;
    private RenderTexture computeTexture;
    private float computeValue;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        Application.targetFrameRate = TargetFrameRate;
        var go = new GameObject("HudGfxBridge");
        DontDestroyOnLoad(go);
        go.AddComponent<HudGfxBridge>();
    }

    private IEnumerator Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Debug.Log($"hudgfx: Unity main thread {HudGfx_CurrentThreadId()}, graphics API {SystemInfo.graphicsDeviceType}");
        renderEvent = HudGfx_GetRenderEventFunc();

        dialTexture = new RenderTexture(TextureSize, TextureSize, 0, RenderTextureFormat.ARGB32);
        dialTexture.name = "HudGfxDial";
        // On Vulkan the plugin writes the texture from a compute shader, so it
        // needs storage usage. GLES draws through an FBO and doesn't: the
        // Android Automotive emulator's GLES rejects random writes for RGBA8
        // ("format unsupported for random writes"), and the texture failed.
        dialTexture.enableRandomWrite =
            SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Vulkan;
        dialTexture.Create();
        // A GL texture name on GLES, a VkImage* on Vulkan.
        HudGfx_SetDialTexture(dialTexture.GetNativeTexturePtr(), dialTexture.width, dialTexture.height);
        panels = CreatePanelRoot();
        CreateDialPanel(dialTexture, panels, 0f);

        // Unity's own compute path: the same dial from an HLSL ComputeShader.
        dialCompute = Resources.Load<ComputeShader>("HudDialCompute");
        if (dialCompute != null && SystemInfo.supportsComputeShaders)
        {
            dialKernel = dialCompute.FindKernel("DrawDial");
            computeTexture = new RenderTexture(ComputeSize, ComputeSize, 0, RenderTextureFormat.ARGB32)
            {
                name = "HudDialCompute", enableRandomWrite = true,
            };
            computeTexture.Create();
            CreateDialPanel(computeTexture, panels, PanelSpacing);
            Debug.Log($"hudgfx: Unity ComputeShader dial, kernel {dialKernel}, {ComputeSize}x{ComputeSize}");
        }

        // The plugin's own texture: created on the render thread by event 3,
        // then wrapped. Wait a few frames for it.
        GL.IssuePluginEvent(renderEvent, CreateExternalEventId);
        for (int i = 0; i < 30 && HudGfx_GetExternalTexture() == IntPtr.Zero; i++) yield return null;
        IntPtr native = HudGfx_GetExternalTexture();
        if (native != IntPtr.Zero)
        {
            // A VkImage* on Vulkan, a GL texture name on GLES. RGBA8 UNORM,
            // no mips; "linear" because the data isn't sRGB-encoded.
            externalTexture = Texture2D.CreateExternalTexture(ExternalSize, ExternalSize, TextureFormat.RGBA32,
                false, true, native);
            externalTexture.name = "HudGfxExternal";
            HudGfx_SetExternalTexture(externalTexture.GetNativeTexturePtr());
            CreateDialPanel(externalTexture, panels, -PanelSpacing);
            Debug.Log($"hudgfx: wrapped the plugin's texture {native} with CreateExternalTexture, Unity pointer {externalTexture.GetNativeTexturePtr()}");
        }
        else
        {
            Debug.Log("hudgfx: the plugin's external texture didn't appear");
        }

        var cam = Camera.main;
        if (UseCameraCommandBuffer && cam != null)
        {
            // Recorded once; Unity runs it every time this camera renders.
            var overlay = new CommandBuffer { name = "HudGfx overlay" };
            overlay.IssuePluginEvent(renderEvent, OverlayEventId);
            cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, overlay);
            Debug.Log("hudgfx: overlay event in a CommandBuffer at CameraEvent.AfterForwardOpaque");
            yield break;
        }

        var endOfFrame = new WaitForEndOfFrame();
        while (true)
        {
            yield return endOfFrame;
            GL.IssuePluginEvent(renderEvent, OverlayEventId);
        }
#else
        yield break;
#endif
    }

    private void Update()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (renderEvent == IntPtr.Zero) return;
        HudGfx_SetScreenSize(Screen.width, Screen.height);
        // Queued before this frame's cameras render, so the panel shows this
        // frame's dial.
        GL.IssuePluginEvent(renderEvent, TextureEventId);
        if (externalTexture != null) GL.IssuePluginEvent(renderEvent, ExternalEventId);

        if (computeTexture != null)
        {
            // Eased toward the latest speed, like the plugin's needle.
            float target = Mathf.Clamp01(HudGfx_GetSpeedKph() / MaxSpeedKph);
            computeValue += (target - computeValue) * (1f - Mathf.Exp(-Time.deltaTime / NeedleTauSeconds));
            dialCompute.SetFloat("_Value", computeValue);
            dialCompute.SetFloat("_Radius", 0.9f);
            dialCompute.SetTexture(dialKernel, "_Dial", computeTexture);
            dialCompute.Dispatch(dialKernel, (ComputeSize + 7) / 8, (ComputeSize + 7) / 8, 1);
        }
#endif
    }

    private void LateUpdate()
    {
        // Turn the panel toward the orbiting camera so the dial stays readable.
        var cam = Camera.main;
        if (panels == null || cam == null) return;
        panels.rotation = Quaternion.LookRotation(panels.position - cam.transform.position, Vector3.up);
    }

    // Panels float above the car and turn together toward the camera.
    private static Transform CreatePanelRoot()
    {
        var car = GameObject.Find("CarPivot");
        var root = new GameObject("HudGfxPanels").transform;
        root.position = (car != null ? car.transform.position : Vector3.zero) + new Vector3(0f, 1.75f, 0f);
        return root;
    }

    // A world-space canvas showing one texture with a RawImage (UI/Default
    // shader, always in the build), placed along the root's x axis.
    private static void CreateDialPanel(Texture texture, Transform root, float x)
    {
        var canvasObject = new GameObject("HudGfxDialPanel " + texture.name);
        canvasObject.transform.SetParent(root, false);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(TextureSize, TextureSize);
        rect.localScale = Vector3.one * (PanelSize / TextureSize);
        rect.localPosition = new Vector3(x, 0f, 0f);

        var imageObject = new GameObject("Dial");
        imageObject.transform.SetParent(canvasObject.transform, false);
        var image = imageObject.AddComponent<RawImage>();
        image.texture = texture;
        var imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
    }
}
