using UnityEngine;
using UnityEngine.UI;
using TMPro; // Required for TextMeshPro

public class KickAimUI : MonoBehaviour
{
    public static KickAimUI Instance { get; private set; }

    [Header("Settings")]
    public float defaultForce = 95f;
    public float minForce = 35f;
    public float maxForce = 140f;

    [Header("UI References (Drag from Prefab)")]
    public Slider forceSlider;
    public TextMeshProUGUI forceLabel;
    public GameObject distancePanel;
    public TextMeshProUGUI distancePanelText;
    public TextMeshProUGUI hoverLabel;
    public RectTransform hoverRect;
    public Image hoverBg;

    [Header("Internal Refs")]
    public CharacterMovement player;
    private AsteroidMotion locked;
    private Camera cam;

    [Header("Targeting UI")]
    public GameObject crosshair; // Drag a Crosshair Image here

    public void ToggleCrosshair(bool show)
    {
        if (crosshair != null && crosshair.activeSelf != show)
        {
            crosshair.SetActive(show);
        }
    }

    public float SelectedForce =>
        forceSlider != null ? forceSlider.value : Mathf.Clamp(defaultForce, minForce, maxForce);

    public static KickAimUI EnsureExists()
    {
        if (Instance != null) return Instance;
        Instance = FindAnyObjectByType<KickAimUI>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        if (forceSlider != null)
        {
            forceSlider.onValueChanged.AddListener(RefreshForceLabel);
        }
    }

    public void Bind(CharacterMovement movement)
    {
        player = movement;
        if (movement == null) return;

        minForce = movement.minKickForce;
        maxForce = movement.maxKickForce;
        defaultForce = Mathf.Clamp(movement.kickForce, minForce, maxForce);

        if (forceSlider != null)
        {
            forceSlider.minValue = minForce;
            forceSlider.maxValue = maxForce;
            forceSlider.SetValueWithoutNotify(defaultForce);
            RefreshForceLabel(defaultForce);
        }
    }

    public void SetLocked(AsteroidMotion rock) => locked = rock;
    public void ClearLock() => locked = null;

    public void ShowHoverDistance(AsteroidMotion rock, float distance)
    {
        if (rock == null) return;

        if (distancePanel != null)
        {
            distancePanel.SetActive(true);
            distancePanelText.text = locked == rock
                ? $"LOCKED  {distance:0} m"
                : $"Distance  {distance:0} m";
        }

        if (hoverLabel != null)
        {
            hoverLabel.gameObject.SetActive(true);
            if (hoverBg != null) hoverBg.gameObject.SetActive(true);
            hoverLabel.text = $"{distance:0} m";
            PositionHoverLabel(rock.WorldBounds.center);
        }
    }

    public void HideHoverDistance()
    {
        if (locked != null) return;
        if (distancePanel != null) distancePanel.SetActive(false);
        if (hoverLabel != null) hoverLabel.gameObject.SetActive(false);
        if (hoverBg != null) hoverBg.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;

        if (locked != null && player != null)
        {
            float dist = Vector3.Distance(player.transform.position, locked.WorldBounds.center);
            ShowHoverDistance(locked, dist);
        }
    }

    void PositionHoverLabel(Vector3 worldPos)
    {
        if (cam == null || hoverRect == null) return;

        Vector3 screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z < 0.5f)
        {
            screen = Input.mousePosition;
            screen.z = 1f;
        }

        screen.x = Mathf.Clamp(screen.x, 80f, Screen.width - 80f);
        screen.y = Mathf.Clamp(screen.y + 48f, 40f, Screen.height - 40f);
        hoverRect.position = screen;
    }

    void RefreshForceLabel(float value)
    {
        if (forceLabel != null)
            forceLabel.text = $"KICK FORCE\n<size=120%>{value:0}</size>";
    }
}