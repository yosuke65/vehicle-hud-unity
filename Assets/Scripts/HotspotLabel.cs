using UnityEngine;
using UnityEngine.UI;

// World-anchor-to-screen-space label for the digital-cockpit HMI. Each
// hotspot is a 3D point on the car (e.g., engine bay, driver's door) that
// projects to a 2D UI label with a dot and a connector line. The label
// offset is fixed in screen space so it never overlaps the 3D model, and
// highlighting is toggled from the bottom nav bar.
[RequireComponent(typeof(RectTransform))]
public class HotspotLabel : MonoBehaviour
{
    [Header("World anchor")]
    public Transform worldAnchor;
    public Camera targetCamera;

    [Header("Screen-space layout")]
    public Vector2 labelOffsetPixels = new Vector2(96f, 64f);
    public float dotSizePixels = 16f;
    public float lineWidthPixels = 2f;

    [Header("Visuals")]
    public Color normalColor = new Color(0.75f, 0.85f, 0.95f, 1f);
    public Color highlightColor = new Color(0.2f, 0.8f, 1f, 1f);
    public Sprite dotSprite;

    private RectTransform canvasRect;
    private RectTransform dotRect;
    private RectTransform lineRect;
    private Image dotImage;
    private Image lineImage;
    private Text labelText;
    private bool highlighted;

    private void Awake()
    {
        canvasRect = GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
        if (canvasRect == null)
        {
            Debug.LogError("HotspotLabel must live under a Canvas.", this);
            enabled = false;
            return;
        }

        if (targetCamera == null)
            targetCamera = Camera.main;

        BuildVisuals();
    }

    private void BuildVisuals()
    {
        RectTransform self = (RectTransform)transform;
        self.sizeDelta = Vector2.zero;
        self.anchoredPosition = Vector2.zero;

        // Dot: a small circle at the projected anchor position.
        GameObject dotGo = new GameObject("Dot", typeof(RectTransform), typeof(Image));
        dotGo.transform.SetParent(transform, false);
        dotRect = (RectTransform)dotGo.transform;
        dotRect.sizeDelta = Vector2.one * dotSizePixels;
        dotImage = dotGo.GetComponent<Image>();
        dotImage.sprite = dotSprite;
        dotImage.type = Image.Type.Simple;
        dotImage.color = normalColor;

        // Connector line between dot and label.
        GameObject lineGo = new GameObject("Line", typeof(RectTransform), typeof(Image));
        lineGo.transform.SetParent(transform, false);
        lineRect = (RectTransform)lineGo.transform;
        lineRect.sizeDelta = new Vector2(lineWidthPixels, 1f);
        lineImage = lineGo.GetComponent<Image>();
        lineImage.color = normalColor;
        lineImage.sprite = dotSprite; // reused as a simple white pixel fallback

        // Label text.
        GameObject labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGo.transform.SetParent(transform, false);
        RectTransform labelRect = (RectTransform)labelGo.transform;
        labelRect.sizeDelta = new Vector2(160f, 40f);
        labelText = labelGo.GetComponent<Text>();
        labelText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        labelText.fontSize = 22;
        labelText.color = normalColor;
        labelText.alignment = TextAnchor.MiddleLeft;
        labelText.horizontalOverflow = HorizontalWrapMode.Overflow;
        labelText.verticalOverflow = VerticalWrapMode.Overflow;
        labelText.text = gameObject.name.Replace("Hotspot", "").Replace("_", " ");
    }

    private void LateUpdate()
    {
        if (worldAnchor == null || targetCamera == null || canvasRect == null)
            return;

        Vector3 worldPos = worldAnchor.position;
        Vector3 screenPos = targetCamera.WorldToScreenPoint(worldPos);

        // Hide when behind the camera or off-screen by a wide margin.
        bool visible = screenPos.z > 0f
            && screenPos.x > -200f && screenPos.x < Screen.width + 200f
            && screenPos.y > -200f && screenPos.y < Screen.height + 200f;

        dotRect.gameObject.SetActive(visible);
        lineRect.gameObject.SetActive(visible);
        labelText.gameObject.SetActive(visible);
        if (!visible) return;

        // Convert screen pixels to canvas-local anchored positions.
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, screenPos, null, out Vector2 canvasLocal);

        // Keep label on the same side of the anchor it started on, flipping
        // only when it would push off-screen.
        Vector2 offset = labelOffsetPixels;
        if (screenPos.x + labelOffsetPixels.x + 160f > Screen.width)
            offset.x = -labelOffsetPixels.x - 160f;
        if (screenPos.y + labelOffsetPixels.y + 40f > Screen.height)
            offset.y = -labelOffsetPixels.y - 40f;

        Vector2 labelPos = canvasLocal + offset;

        dotRect.anchoredPosition = canvasLocal;
        labelText.rectTransform.anchoredPosition = labelPos;

        // Stretch/rotate the line from dot to label.
        Vector2 delta = labelPos - canvasLocal;
        float length = delta.magnitude;
        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        lineRect.anchoredPosition = canvasLocal + delta * 0.5f;
        lineRect.sizeDelta = new Vector2(lineWidthPixels, length);
        lineRect.rotation = Quaternion.Euler(0f, 0f, angle - 90f);
    }

    public void SetHighlighted(bool on)
    {
        highlighted = on;
        Color c = highlighted ? highlightColor : normalColor;
        dotImage.color = c;
        lineImage.color = c;
        labelText.color = c;
    }
}
