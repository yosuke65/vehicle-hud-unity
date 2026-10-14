using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

// Batch-mode capture from the phone, no Editor window needed:
//   adb forward tcp:55000 localabstract:Unity-dev.yosukesakurai.vehiclehud
//   (DirectIPConnect tries ports 55000-55063, 35000 and 4600 on that IP)
//   HUD_CAPTURE_DIR=... Unity -batchmode -projectPath ... -executeMethod HudCapture.Run
// Connects to the development player on 127.0.0.1, records the Profiler for
// about 10 s, saves the capture (.data, opens in the Profiler window) and
// writes the render thread's markers and the Frame Debugger's event list as
// text. The Frame Debugger API is internal, so it is reached by reflection.
public static class HudCapture
{
    static string outDir;
    static double start, phaseStart, lastTry;
    static int phase;
    static readonly StringBuilder log = new StringBuilder();
    static Type fdu;

    public static void Run()
    {
        outDir = Environment.GetEnvironmentVariable("HUD_CAPTURE_DIR") ?? "Builds/Capture";
        Directory.CreateDirectory(outDir);
        fdu = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility"))
            .FirstOrDefault(t => t != null);
        Note($"FrameDebuggerUtility type: {fdu}");
        ProfilerDriver.DirectIPConnect("127.0.0.1");
        start = phaseStart = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
    }

    static void Note(string s) { Debug.Log("HudCapture: " + s); log.AppendLine(s); }

    static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now - start > 180) { Note("timeout"); Finish(2); return; }
        switch (phase)
        {
            case 0: // connect, retrying: the forwarded port can come up after the Editor
                if (ProfilerDriver.connectedProfiler == -1 || ProfilerDriver.connectedProfiler == 0)
                {
                    if (now - lastTry > 3) { ProfilerDriver.DirectIPConnect("127.0.0.1"); lastTry = now; }
                    if (now - phaseStart < 60) break;
                }
                {
                    Note($"connected profiler id {ProfilerDriver.connectedProfiler}: {ProfilerDriver.GetConnectionIdentifier(ProfilerDriver.connectedProfiler)}");
                    ProfilerDriver.ClearAllFrames();
                    ProfilerDriver.enabled = true;
                    phase = 1; phaseStart = now;
                }
                break;
            case 1: // record ~10 s
                if (now - phaseStart > 10)
                {
                    ProfilerDriver.enabled = false;
                    Note($"profiler frames {ProfilerDriver.firstFrameIndex}..{ProfilerDriver.lastFrameIndex}");
                    DumpProfiler();
                    if (fdu != null) { Call("SetEnabled", true, ProfilerDriver.connectedProfiler); }
                    phase = 2; phaseStart = now;
                }
                break;
            case 2: // wait for the Frame Debugger's event list from the player
                if (now - phaseStart > 25 || now - phaseStart > 4 && fdu != null && (int)Prop("count") > 0 && !(bool)Prop("receivingRemoteFrameEventData"))
                {
                    DumpFrameDebugger();
                    if (fdu != null) { Call("SetEnabled", false, ProfilerDriver.connectedProfiler); }
                    phase = 3; phaseStart = now;
                }
                break;
            case 3:
                if (now - phaseStart > 2) Finish(0);
                break;
        }
    }

    static void DumpProfiler()
    {
        int last = ProfilerDriver.lastFrameIndex, first = ProfilerDriver.firstFrameIndex;
        if (last < 0) { Note("no profiler frames"); return; }
        ProfilerDriver.SaveProfile(Path.Combine(outDir, "hud_profile.data"));
        int frame = Math.Max(first, last - 30);

        // Every thread in one frame, and any plugin-related sample in any frame.
        var sb = new StringBuilder();
        for (int t = 0; ; t++)
        {
            using (var v = ProfilerDriver.GetRawFrameDataView(frame, t))
            {
                if (v == null || !v.valid) break;
                sb.AppendLine($"thread {t}: {v.threadGroupName}/{v.threadName}, {v.sampleCount} samples, frame {frame} {v.frameTimeMs:F2} ms");
                if (v.threadName.Contains("Render") || v.threadName.Contains("Main") || v.threadName.Contains("Gfx"))
                    DumpHierarchy(v, sb);
            }
        }
        File.WriteAllText(Path.Combine(outDir, "profiler_frame.txt"), sb.ToString());

        var hits = new StringBuilder();
        for (int f = Math.Max(first, last - 60); f <= last; f++)
            for (int t = 0; t < 64; t++)
                using (var v = ProfilerDriver.GetRawFrameDataView(f, t))
                {
                    if (v == null || !v.valid) break;
                    for (int i = 0; i < v.sampleCount; i++)
                    {
                        string n = v.GetSampleName(i) ?? "";
                        if (n.IndexOf("HudGfx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            n.IndexOf("Plugin", StringComparison.OrdinalIgnoreCase) >= 0)
                            hits.AppendLine($"frame {f} {v.threadName}: {n} {v.GetSampleTimeMs(i):F3} ms");
                    }
                }
        File.WriteAllText(Path.Combine(outDir, "profiler_plugin_samples.txt"), hits.ToString());
        Note($"profiler: frame {frame} dumped, {hits.ToString().Split('\n').Length - 1} plugin-related samples");
    }

    // Raw samples are stored depth-first; children counts give the tree.
    static void DumpHierarchy(RawFrameDataView v, StringBuilder sb)
    {
        var remaining = new System.Collections.Generic.Stack<int>();
        for (int i = 0; i < v.sampleCount; i++)
        {
            while (remaining.Count > 0 && remaining.Peek() == 0) remaining.Pop();
            sb.AppendLine($"{new string(' ', 2 * (remaining.Count + 1))}{v.GetSampleName(i)}  {v.GetSampleTimeMs(i):F3} ms");
            if (remaining.Count > 0) remaining.Push(remaining.Pop() - 1);
            remaining.Push(v.GetSampleChildrenCount(i));
        }
    }

    static object Call(string name, params object[] args)
    {
        var m = fdu.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == args.Length);
        if (m == null) { Note($"no method {name}/{args.Length}"); return null; }
        try { return m.Invoke(null, args); } catch (Exception e) { Note($"{name} failed: {e.InnerException?.Message ?? e.Message}"); return null; }
    }

    static object Prop(string name)
    {
        var p = fdu.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        return p?.GetValue(null);
    }

    static void DumpFrameDebugger()
    {
        if (fdu == null) { Note("no FrameDebuggerUtility"); return; }
        var sb = new StringBuilder();
        foreach (var m in fdu.GetMembers(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            sb.AppendLine("// member: " + m);
        sb.AppendLine($"IsRemoteEnabled={Call("IsRemoteEnabled")} count={Prop("count")} receiving={Prop("receivingRemoteFrameEventData")}");
        var events = Call("GetFrameEvents") as Array;
        int n = events?.Length ?? 0;
        sb.AppendLine($"events: {n}");
        for (int i = 0; i < n; i++)
        {
            var e = events.GetValue(i);
            string fields = string.Join(", ", e.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(f => $"{f.Name}={f.GetValue(e)}"));
            sb.AppendLine($"{i,3}: {Call("GetFrameEventInfoName", i)} | {fields}");
        }
        File.WriteAllText(Path.Combine(outDir, "frame_debugger_events.txt"), sb.ToString());
        Note($"frame debugger: {n} events");
    }

    static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        File.WriteAllText(Path.Combine(outDir, "capture_log.txt"), log.ToString());
        EditorApplication.Exit(code);
    }
}
