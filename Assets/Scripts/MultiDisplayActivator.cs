using UnityEngine;

/// <summary>
/// Logs the number of displays Unity sees and activates display index 1 when
/// available. In a UAAL/host-launched Activity this is mostly a diagnostic:
/// real secondary-display rendering depends on the host moving the Unity
/// Activity to the secondary display via ActivityOptions.setLaunchDisplayId.
/// </summary>
public class MultiDisplayActivator : MonoBehaviour
{
    void Start()
    {
        Debug.Log($"Unity sees {Display.displays.Length} display(s)");
        if (Display.displays.Length > 1)
        {
            Display.displays[1].Activate();
            Debug.Log("Activated Unity display 1");
        }
        else
        {
            Debug.Log("Only one Unity display available; multi-display rendering is a no-op.");
        }
    }
}
