using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Batch-mode scene/build setup for the UAAL Phase 4 embed — invoked via
// `-executeMethod` rather than the Editor GUI, since this is scripted from
// the vehicle-hud native project's build tooling, not authored by hand.
public static class SceneSetup
{
    private const string ScenePath = "Assets/Scenes/Main.unity";

    public static void BuildScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "RotatingCube";
        cube.AddComponent<RotateCube>();

        // Same green used throughout the native vehicle-hud HUD/needle,
        // for visual consistency between the two rendering paths.
        var material = new Material(Shader.Find("Standard"));
        material.color = new Color(0.20f, 0.85f, 0.55f);
        cube.GetComponent<Renderer>().sharedMaterial = material;

        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, 1.2f, -4f);
        cameraObject.transform.LookAt(cube.transform);
        camera.clearFlags = CameraClearFlags.SolidColor;
        // Same near-black clear color as the native GLES/Vulkan renderers.
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.09f);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

        EditorBuildSettings.scenes = new[] {
            new EditorBuildSettingsScene(ScenePath, true),
        };

        Debug.Log("SceneSetup.BuildScene complete: " + ScenePath);
    }

    public static void BuildCockpitScene()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Car pivot is the orbit target.
        var carPivot = new GameObject("CarPivot");
        carPivot.transform.position = new Vector3(0f, 0.55f, 0f);

        var carRoot = new GameObject("Car");
        carRoot.transform.SetParent(carPivot.transform, false);

        var bodyMat = new Material(Shader.Find("Standard"));
        bodyMat.color = new Color(0.15f, 0.18f, 0.22f);
        var cabinMat = new Material(Shader.Find("Standard"));
        cabinMat.color = new Color(0.12f, 0.14f, 0.16f);
        var wheelMat = new Material(Shader.Find("Standard"));
        wheelMat.color = new Color(0.08f, 0.08f, 0.08f);
        var glassMat = new Material(Shader.Find("Standard"));
        glassMat.color = new Color(0.35f, 0.55f, 0.75f, 0.6f);
        glassMat.SetFloat("_Mode", 3f); // transparent

        // Chassis.
        var chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chassis.name = "Chassis";
        chassis.transform.SetParent(carRoot.transform, false);
        chassis.transform.localPosition = new Vector3(0f, 0.35f, 0f);
        chassis.transform.localScale = new Vector3(1.6f, 0.55f, 3.6f);
        chassis.GetComponent<Renderer>().sharedMaterial = bodyMat;

        // Cabin.
        var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "Cabin";
        cabin.transform.SetParent(carRoot.transform, false);
        cabin.transform.localPosition = new Vector3(0f, 0.82f, -0.25f);
        cabin.transform.localScale = new Vector3(1.3f, 0.5f, 1.7f);
        cabin.GetComponent<Renderer>().sharedMaterial = cabinMat;

        // Windshield as a thin, tilted plane.
        var windshield = GameObject.CreatePrimitive(PrimitiveType.Cube);
        windshield.name = "Windshield";
        windshield.transform.SetParent(carRoot.transform, false);
        windshield.transform.localPosition = new Vector3(0f, 0.9f, 0.78f);
        windshield.transform.localRotation = Quaternion.Euler(-25f, 0f, 0f);
        windshield.transform.localScale = new Vector3(1.2f, 0.05f, 0.65f);
        windshield.GetComponent<Renderer>().sharedMaterial = glassMat;

        // Wheels.
        var wheelPositions = new[] {
            new Vector3(-0.85f, 0f, 1.1f),
            new Vector3(0.85f, 0f, 1.1f),
            new Vector3(-0.85f, 0f, -1.1f),
            new Vector3(0.85f, 0f, -1.1f),
        };
        foreach (var pos in wheelPositions)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = "Wheel";
            wheel.transform.SetParent(carRoot.transform, false);
            wheel.transform.localPosition = pos;
            wheel.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheel.transform.localScale = new Vector3(0.55f, 0.18f, 0.55f);
            wheel.GetComponent<Renderer>().sharedMaterial = wheelMat;
            wheel.AddComponent<CarWheelSpin>();
        }

        // Camera + orbit controller.
        var cameraObject = new GameObject("Main Camera");
        var camera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.05f, 0.07f, 0.09f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        var orbit = cameraObject.AddComponent<OrbitCamera>();
        orbit.target = carPivot.transform;
        orbit.distance = 5.5f;
        orbit.minPitchDeg = 8f;
        orbit.maxPitchDeg = 75f;

        // Directional light.
        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightObject.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        // EventSystem for touch input.
        var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
        eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

        // Optional multi-display diagnostic: logs how many displays Unity sees
        // and activates display index 1 when available. Mostly a no-op on a
        // single-display device; useful when the host launches Unity on a
        // secondary display via ActivityOptions.setLaunchDisplayId.
        var multiDisplay = new GameObject("MultiDisplayActivator");
        multiDisplay.AddComponent<MultiDisplayActivator>();

        // Hotspot anchors in world space.
        var hotspotDefs = new[] {
            ("Engine", new Vector3(0f, 0.65f, 1.85f)),
            ("Driver", new Vector3(-0.65f, 0.85f, 0.35f)),
            ("Doors", new Vector3(-0.85f, 0.55f, -0.2f)),
            ("Safety", new Vector3(0f, 1.15f, -0.25f)),
            ("Suspension", new Vector3(0.85f, 0.15f, 1.1f)),
        };

        var hotspotCanvas = CreateCanvas("HotspotCanvas", 0);
        var hotspotLabels = new HotspotLabel[hotspotDefs.Length];
        for (int i = 0; i < hotspotDefs.Length; i++)
        {
            var (name, localPos) = hotspotDefs[i];
            var anchor = new GameObject(name + "Anchor");
            anchor.transform.SetParent(carRoot.transform, false);
            anchor.transform.localPosition = localPos;

            var labelGo = new GameObject("Hotspot" + name, typeof(RectTransform));
            labelGo.transform.SetParent(hotspotCanvas.transform, false);
            var label = labelGo.AddComponent<HotspotLabel>();
            label.worldAnchor = anchor.transform;
            label.targetCamera = camera;
            label.labelOffsetPixels = new Vector2(i % 2 == 0 ? 110f : -260f, 60f + i * 18f);
            hotspotLabels[i] = label;
        }

        // Bottom nav bar.
        var navCanvas = CreateCanvas("NavBarCanvas", 1);
        var navPanel = new GameObject("NavPanel", typeof(RectTransform));
        navPanel.transform.SetParent(navCanvas.transform, false);
        var navRect = (RectTransform)navPanel.transform;
        navRect.anchorMin = Vector2.zero;
        navRect.anchorMax = Vector2.right;
        navRect.pivot = new Vector2(0.5f, 0f);
        navRect.anchoredPosition = new Vector2(0f, 24f);
        navRect.sizeDelta = new Vector2(-48f, 72f);
        var navImage = navPanel.AddComponent<Image>();
        navImage.color = new Color(0.08f, 0.10f, 0.12f, 0.92f);

        var navGroup = navPanel.AddComponent<HorizontalLayoutGroup>();
        navGroup.spacing = 16f;
        navGroup.padding = new RectOffset(16, 16, 12, 12);
        navGroup.childAlignment = TextAnchor.MiddleCenter;
        navGroup.childControlWidth = true;
        navGroup.childControlHeight = true;
        navGroup.childForceExpandWidth = true;

        var navSprites = new[] { "Engine", "Driver", "Doors", "Safety", "Suspension" };
        for (int i = 0; i < navSprites.Length; i++)
        {
            var btn = CreateNavButton(navPanel.transform, navSprites[i]);
            var button = btn.GetComponent<Button>();
            var navBtn = btn.GetComponent<HotspotNavButton>();
            navBtn.target = hotspotLabels[i];
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, navBtn.Select);
        }

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

        Debug.Log("SceneSetup.BuildCockpitScene complete: " + ScenePath);
    }

    private static Canvas CreateCanvas(string name, int sortOrder)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    private static GameObject CreateNavButton(Transform parent, string label)
    {
        var go = new GameObject("Btn" + label, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(140f, 48f);

        var image = go.AddComponent<Image>();
        image.color = new Color(0.18f, 0.22f, 0.26f, 1f);

        var button = go.AddComponent<Button>();
        button.targetGraphic = image;

        var labelGo = new GameObject("Text", typeof(RectTransform));
        labelGo.transform.SetParent(go.transform, false);
        var labelRect = (RectTransform)labelGo.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        var text = labelGo.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 22;
        text.color = new Color(0.85f, 0.90f, 0.95f, 1f);
        text.alignment = TextAnchor.MiddleCenter;
        text.text = label;

        go.AddComponent<HotspotNavButton>();
        return go;
    }

    public static void BuildCockpitAndExport()
    {
        BuildCockpitScene();
        ConfigureAndroidExportAndBuild();
    }

    // Same export, but with Vulkan as the only graphics API, so the native
    // plugin's IUnityGraphicsVulkan path runs on the Pixel 6a. Unity keeps
    // Pixel 6 devices off Vulkan by default, so Automatic picks OpenGL ES 3
    // there, and Vulkan-only fails with "Unable to initialize the Unity
    // Engine Graphics API" unless the device is on the allow list.
    // Puts Automatic back afterwards. The allow-list entries stay in
    // ProjectSettings (they only match the Pixel 6a and the Android
    // Automotive emulator on this Mac, whose Vulkan device is the host GPU).
    public static void ConfigureAndroidExportVulkanAndBuild()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        PlayerSettings.Android.androidVulkanAllowFilterList = new[] {
            new AndroidDeviceFilterData {
                brandName = "google",
                productName = "bluejay",
                deviceName = "Mali-G78",
            },
            new AndroidDeviceFilterData {
                brandName = "google",
                productName = "sdk_gcar_arm64",
                deviceName = "Apple M5",
            },
        };
        try {
            ConfigureAndroidExportAndBuild();
        } finally {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, true);
        }
    }

    // Same export with OpenGL ES 3 as the only graphics API, to test the
    // plugin's GLES path on a device that's on the Vulkan allow list.
    public static void ConfigureAndroidExportGlesAndBuild()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        try {
            ConfigureAndroidExportAndBuild();
        } finally {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, true);
        }
    }

    public static void ConfigureAndroidExportAndBuild()
    {
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);

        // The whole point of this phase: export a Gradle project/library,
        // not build an APK directly.
        EditorUserBuildSettings.exportAsGoogleAndroidProject = true;

        // No Unity splash screen: an HMI should show its first real frame as
        // soon as it can, and startup is measured to that frame. With the
        // splash on (the default), the plugin's first event came about 2 s
        // after Unity's "Fully drawn" on the Pixel 6a (2026-10-13).
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SplashScreen.showUnityLogo = false;

        PlayerSettings.applicationIdentifier = "dev.yosukesakurai.vehiclehud.unitylib";
        // Unity 6's minimum supported Android API is 26 — AndroidApiLevel24
        // is obsolete as of this Editor version. vehicle-hud's own
        // app/build.gradle.kts minSdk (currently 24) will need bumping to
        // 26 to match once this library is actually merged in.
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
        // Tried adding AndroidArchitecture.X86_64 here to run on Cuttlefish
        // (x86_64 virtual hardware) after a real on-device
        // UnsatisfiedLinkError for libgame.so on the AAOS build. Confirmed
        // via a real batch-mode build that this Unity Editor version
        // (6000.5.9f1) has fully deprecated/removed X86_64 Android support
        // (CS0618: "X86_64 is no longer supported") — the flag is a silent
        // no-op, so IL2CPP output stays ARM64-only regardless. Not
        // something fixable from this project; Unity itself dropped it.

        var options = new BuildPlayerOptions {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/AndroidExport",
            target = BuildTarget.Android,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        Debug.Log("Android export build result: " + report.summary.result);
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded) {
            EditorApplication.Exit(1);
        }
    }
}
