using UnityEngine;

/// <summary>
/// Trauma-based camera shake. CameraMovement reads Offset / EulerOffset each frame.
/// </summary>
public class CameraShake : MonoBehaviour
{
    public static CameraShake Instance { get; private set; }

    public float traumaDecay = 1.35f;
    public float maxOffset = 0.55f;
    public float maxAngle = 2.2f;
    public float noiseFrequency = 22f;
    public float kickForceTraumaScale = 0.008f;
    public float velocityTraumaScale = 0.035f;
    public float minTrauma = 0.12f;
    public float maxTrauma = 1f;

    float trauma, seed;

    public Vector3 Offset { get; private set; }
    public Vector3 EulerOffset { get; private set; }

    void Awake()
    {
        // Keep the shake on the real Camera if duplicates exist
        if (Instance != null && Instance != this)
        {
            bool mine = GetComponent<Camera>() != null;
            bool theirs = Instance.GetComponent<Camera>() != null;
            if (!mine && theirs) { Destroy(this); return; }
            Destroy(Instance);
        }
        Instance = this;
        seed = Random.value * 100f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void AddTrauma(float amount) =>
        trauma = Mathf.Clamp(trauma + amount, 0f, maxTrauma);

    public void ShakeFromKick(float kickForce, float launchSpeed)
    {
        float amount = kickForce * kickForceTraumaScale + launchSpeed * velocityTraumaScale;
        AddTrauma(Mathf.Clamp(amount, minTrauma, maxTrauma));
    }

    public void Shake(float duration, float magnitude) =>
        AddTrauma(Mathf.Clamp(magnitude * 0.85f + duration * 0.5f, minTrauma, maxTrauma));

    void LateUpdate()
    {
        if (trauma <= 0.001f)
        {
            trauma = 0f;
            Offset = EulerOffset = Vector3.zero;
            return;
        }

        float shake = trauma * trauma;
        float t = Time.time * noiseFrequency;

        Offset = new Vector3(
            (Mathf.PerlinNoise(seed, t) * 2f - 1f) * maxOffset * shake,
            (Mathf.PerlinNoise(seed + 1.7f, t) * 2f - 1f) * maxOffset * shake,
            (Mathf.PerlinNoise(seed + 3.1f, t) * 2f - 1f) * maxOffset * 0.45f * shake);

        EulerOffset = new Vector3(
            (Mathf.PerlinNoise(seed + 4.3f, t) * 2f - 1f) * maxAngle * shake,
            (Mathf.PerlinNoise(seed + 5.9f, t) * 2f - 1f) * maxAngle * shake,
            (Mathf.PerlinNoise(seed + 7.2f, t) * 2f - 1f) * maxAngle * 0.6f * shake);

        trauma = Mathf.Max(0f, trauma - traumaDecay * Time.deltaTime);
    }
}
