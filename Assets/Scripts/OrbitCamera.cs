using UnityEngine;

// Touch-drag orbit camera for the digital-cockpit HMI scene: one-finger drag
// orbits around the car's pivot, matching the "explore the vehicle" gesture
// real automotive diagnostics/status screens use. Idle-auto-orbits when
// nothing is touching the screen so the scene never reads as a static image.
public class OrbitCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 4.5f;
    public float minPitchDeg = 10f;
    public float maxPitchDeg = 80f;
    public float dragDegPerPixel = 0.25f;
    public float idleDegPerSecond = 6f;

    private float yawDeg = 35f;
    private float pitchDeg = 18f;
    private Vector2 lastPointerPos;
    private bool dragging;

    private void LateUpdate()
    {
        if (target == null) return;

        bool gotInput = false;

        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            gotInput = true;
            if (touch.phase == TouchPhase.Began)
            {
                dragging = true;
                lastPointerPos = touch.position;
            }
            else if (touch.phase == TouchPhase.Moved && dragging)
            {
                ApplyDrag(touch.position);
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                dragging = false;
            }
        }
        else if (Input.GetMouseButton(0))
        {
            // Editor/desktop fallback so this is testable without a touch device.
            gotInput = true;
            if (Input.GetMouseButtonDown(0)) lastPointerPos = Input.mousePosition;
            ApplyDrag(Input.mousePosition);
        }

        if (!gotInput)
        {
            yawDeg += idleDegPerSecond * Time.deltaTime;
        }

        Quaternion rotation = Quaternion.Euler(pitchDeg, yawDeg, 0f);
        Vector3 offset = rotation * new Vector3(0f, 0f, -distance);
        transform.position = target.position + offset;
        transform.LookAt(target.position + Vector3.up * 0.4f);
    }

    private void ApplyDrag(Vector2 currentPos)
    {
        Vector2 delta = currentPos - lastPointerPos;
        yawDeg += delta.x * dragDegPerPixel;
        pitchDeg = Mathf.Clamp(pitchDeg - delta.y * dragDegPerPixel, minPitchDeg, maxPitchDeg);
        lastPointerPos = currentPos;
    }
}
