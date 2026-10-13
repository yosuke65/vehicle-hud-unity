using System.Globalization;
using UnityEngine;

// Spins a wheel around its local axle axis. Idle-spins at rest, same "still
// reads as alive at 0 kph" idea as RotateCube, and exposes the same
// SetSpeedKph(string) UnitySendMessage contract RotateCube does, so a future
// native-side telemetry link could drive real wheel rotation with zero
// changes here.
public class CarWheelSpin : MonoBehaviour
{
    private const float kIdleDegreesPerSecond = 40f;
    private const float kDegreesPerSecondPerKph = 4f;

    private float degreesPerSecond = kIdleDegreesPerSecond;

    private void Update()
    {
        transform.Rotate(Vector3.right, degreesPerSecond * Time.deltaTime, Space.Self);
    }

    public void SetSpeedKph(string value)
    {
        if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float speedKph))
        {
            degreesPerSecond = kIdleDegreesPerSecond + speedKph * kDegreesPerSecondPerKph;
        }
    }
}
