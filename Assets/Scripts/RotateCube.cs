using System.Globalization;
using UnityEngine;

// Proof-of-life for the UAAL embed, and the receiving end of the unified
// cockpit's shared-telemetry link: if this cube's spin rate genuinely
// tracks live vehicle speed (via SetSpeedKph, called through
// UnityPlayer.UnitySendMessage from the native side), the same data is
// really driving both rendering engines, not just two things sharing a
// screen.
public class RotateCube : MonoBehaviour
{
    // Idle spin so the cube still reads as "alive" at 0 kph, scaled up
    // visibly with speed rather than a subtle change that's easy to miss.
    private const float kIdleDegreesPerSecond = 20f;
    private const float kDegreesPerSecondPerKph = 1.5f;

    private float degreesPerSecond = kIdleDegreesPerSecond;

    private void Update()
    {
        transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.World);
        transform.Rotate(Vector3.right, degreesPerSecond * 0.5f * Time.deltaTime, Space.World);
    }

    // UnitySendMessage target, called from EmbeddedUnityView.sendSpeedKph()
    // on the native side. Always delivers a single string argument, and
    // always runs on Unity's main thread (Unity queues SendMessage calls
    // internally), so no locking is needed here despite the caller being a
    // different native thread. InvariantCulture parsing matters: the
    // native side formats via Kotlin's Float.toString() (always "."
    // decimal separator), so parsing with the current/default culture
    // would silently break on any device set to a comma-decimal locale.
    public void SetSpeedKph(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speedKph))
        {
            degreesPerSecond = kIdleDegreesPerSecond + speedKph * kDegreesPerSecondPerKph;
        }
    }
}
