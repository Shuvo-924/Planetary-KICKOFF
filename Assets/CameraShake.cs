using UnityEngine;

public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    [Header("Feel")]
    [Tooltip("How fast trauma fades back to 0")]
    public float traumaDecay = 1.35f;
    [Tooltip("Max positional shake in meters")]
    public float maxOffset = 0.55f;
    [Tooltip("Max rotational shake in degrees")]
    public float maxAngle = 2.2f;
    public float noiseFrequency = 22f;

    [Header("Kick Response")]
    [Tooltip("Trauma added per unit of kick force")]
    public float kickForceTraumaScale = 0.008f;
    [Tooltip("Trauma added per unit of launch speed")]
    public float velocityTraumaScale = 0.035f;
    public float minTrauma = 0.12f;
    public float maxTrauma = 1f;

    float trauma;
    float seed;

    /// <summary>World-space offset for CameraMovement to apply after follow.</summary>
    public Vector3 Offset { get; private set; }

    /// <summary>Euler offset applied after LookAt.</summary>
    public Vector3 EulerOffset { get; private set; }

    void Awake()
    {
        // Prefer the CameraShake on the actual Camera; drop duplicates on CamStand etc.
        if (Instance != null && Instance != this)
        {
            bool thisIsCamera = GetComponent<Camera>() != null;
            bool otherIsCamera = Instance.GetComponent<Camera>() != null;

            if (!thisIsCamera && otherIsCamera)
            {
                Destroy(this);
                return;
            }

            Destroy(Instance);
        }

        Instance = this;
        seed = Random.value * 100f;
        Offset = Vector3.zero;
        EulerOffset = Vector3.zero;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void AddTrauma(float amount)
    {
        trauma = Mathf.Clamp(trauma + amount, 0f, maxTrauma);
    }

    /// <summary>
    /// Scales shake from how hard you kicked and how fast you actually launched.
    /// </summary>
    public void ShakeFromKick(float kickForce, float launchSpeed)
    {
        float amount = kickForce * kickForceTraumaScale + launchSpeed * velocityTraumaScale;
        AddTrauma(Mathf.Clamp(amount, minTrauma, maxTrauma));
    }

    /// <summary>Legacy helper — maps duration/magnitude into trauma.</summary>
    public void Shake(float duration, float magnitude)
    {
        AddTrauma(Mathf.Clamp(magnitude * 0.85f + duration * 0.5f, minTrauma, maxTrauma));
    }

    void LateUpdate()
    {
        if (trauma <= 0.001f)
        {
            trauma = 0f;
            Offset = Vector3.zero;
            EulerOffset = Vector3.zero;
            return;
        }

        // Squared trauma = bigger kicks feel punchier
        float shake = trauma * trauma;
        float t = Time.time * noiseFrequency;

        Offset = new Vector3(
            (Mathf.PerlinNoise(seed, t) * 2f - 1f) * maxOffset * shake,
            (Mathf.PerlinNoise(seed + 1.7f, t) * 2f - 1f) * maxOffset * shake,
            (Mathf.PerlinNoise(seed + 3.1f, t) * 2f - 1f) * maxOffset * 0.45f * shake
        );

        EulerOffset = new Vector3(
            (Mathf.PerlinNoise(seed + 4.3f, t) * 2f - 1f) * maxAngle * shake,
            (Mathf.PerlinNoise(seed + 5.9f, t) * 2f - 1f) * maxAngle * shake,
            (Mathf.PerlinNoise(seed + 7.2f, t) * 2f - 1f) * maxAngle * 0.6f * shake
        );

        trauma = Mathf.Max(0f, trauma - traumaDecay * Time.deltaTime);
    }
}
