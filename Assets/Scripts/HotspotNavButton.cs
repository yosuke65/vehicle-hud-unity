using UnityEngine;
using UnityEngine.UI;

// Runtime nav-bar button handler for the cockpit HMI. The actual Button.onClick
// event is wired in editor-time scene setup code via UnityEventTools so it
// persists into the built player; this script just exposes the Select() method
// that highlights its target hotspot and clears the others.
public class HotspotNavButton : MonoBehaviour
{
    public HotspotLabel target;

    public void Select()
    {
        if (target == null) return;

        var all = FindObjectsOfType<HotspotLabel>();
        foreach (var hotspot in all)
            hotspot.SetHighlighted(hotspot == target);
    }
}
