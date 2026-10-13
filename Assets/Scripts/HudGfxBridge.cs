using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives the native rendering plugin (libhudgfx.so, source in
/// vehicle-hud/unity-plugin). Created automatically after the first scene
/// loads, so no scene edit is needed. Two plugin events per frame, both run
/// on Unity's render thread:
/// - event 2 (in Update, before the scene renders): the plugin draws the
///   speed dial into a RenderTexture, which a panel above the car shows.
/// - event 1 (at the end of the frame): the plugin draws a small dial on top
///   of Unity's frame.
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
#endif

    private const int OverlayEventId = 1;
    private const int TextureEventId = 2;
    private const int TextureSize = 512;

    private IntPtr renderEvent;
    private RenderTexture dialTexture;
    private Transform panel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
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
        panel = CreateDialPanel(dialTexture);

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
#endif
    }

    private void LateUpdate()
    {
        // Turn the panel toward the orbiting camera so the dial stays readable.
        var cam = Camera.main;
        if (panel == null || cam == null) return;
        panel.rotation = Quaternion.LookRotation(panel.position - cam.transform.position, Vector3.up);
    }

    // A world-space canvas floating above the car, showing the plugin's
    // texture with a RawImage (UI/Default shader, always in the build).
    private static Transform CreateDialPanel(Texture texture)
    {
        var car = GameObject.Find("CarPivot");
        var canvasObject = new GameObject("HudGfxDialPanel");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(TextureSize, TextureSize);
        rect.localScale = Vector3.one * (0.9f / TextureSize);
        rect.position = (car != null ? car.transform.position : Vector3.zero) + new Vector3(0f, 1.75f, 0f);

        var imageObject = new GameObject("Dial");
        imageObject.transform.SetParent(canvasObject.transform, false);
        var image = imageObject.AddComponent<RawImage>();
        image.texture = texture;
        var imageRect = imageObject.GetComponent<RectTransform>();
        imageRect.anchorMin = Vector2.zero;
        imageRect.anchorMax = Vector2.one;
        imageRect.offsetMin = Vector2.zero;
        imageRect.offsetMax = Vector2.zero;
        return canvasObject.transform;
    }
}
