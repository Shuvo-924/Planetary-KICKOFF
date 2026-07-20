using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slow drift + tumble. Uses a SphereCollider for reliable landing (mesh hulls fail often).
/// </summary>
public class AsteroidMotion : MonoBehaviour
{
    public static readonly List<AsteroidMotion> All = new List<AsteroidMotion>();

    [HideInInspector] public bool isSpike;
    public float recoilDamping = 0.35f;

    public Vector3 CurrentVelocity => driftDir * driftSpeed + recoil;

    Vector3 driftDir = Vector3.forward;
    float driftSpeed = 0.05f;
    Vector3 spinAxis = Vector3.up;
    float spinSpeed = 1f;
    Vector3 recoil;
    float mass = 80f;
    SphereCollider landCol;
    float landRadius = 1f;

    public Bounds WorldBounds
    {
        get
        {
            var r = GetComponentInChildren<Renderer>();
            if (r != null) return r.bounds;
            float s = Mathf.Max(transform.lossyScale.x, 1f) * 0.5f;
            return new Bounds(transform.position, Vector3.one * s);
        }
    }

    /// <summary>World-space landing radius (sphere approx of the rock).</summary>
   public float LandRadius
{
    get
    {
        // Use the Renderer's bounds as the most accurate world-space size
        var r = GetComponent<SphereCollider>();
        if (r != null)
        {
            // We take the largest extent to ensure a safe "Catch Sphere"
            Vector3 extents = r.radius * Vector3.one;
            return Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) * 1.3f;
        }
        return transform.lossyScale.y * 1.3f;
    }
}

    public bool HasActiveCollider => landCol != null && landCol.enabled;

    public void Init(Vector3 direction, float moveSpeed, float spinDegPerSec, bool spike, float rockMass)
    {
        driftDir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Random.onUnitSphere;
        driftSpeed = Mathf.Max(0.01f, moveSpeed);
        spinAxis = Random.onUnitSphere.normalized;
        spinSpeed = Mathf.Max(0.1f, spinDegPerSec);
        isSpike = spike;
        mass = Mathf.Max(1f, rockMass);
        recoil = Vector3.zero;
        EnsureLandCollider();
        SetColliderActive(true);
    }

    public void ApplyKickRecoil(Vector3 impulse)
    {
        if (impulse.sqrMagnitude < 0.0001f) return;
        recoil += impulse / mass;
    }

    public void SetColliderActive(bool on)
    {
        EnsureLandCollider();
        if (landCol != null) landCol.enabled = on;
    }

    /// <summary>Closest surface point + outward normal for landing.</summary>
    public bool TryGetLanding(Vector3 from, out Vector3 point, out Vector3 normal, out float distance)
    {
        EnsureLandCollider();
        Vector3 center = landCol != null
            ? transform.TransformPoint(landCol.center)
            : transform.position;
        float r = LandRadius;

        Vector3 to = from - center;
        float dist = to.magnitude;

        if (dist < 0.0001f)
        {
            normal = Vector3.up;
            point = center + normal * r;
            distance = -r;
            return true;
        }

        normal = to / dist;
        point = center + normal * r;
        distance = dist - r; // negative = inside
        return true;
    }

    void EnsureLandCollider()
    {
        if (landCol != null) return;

        foreach (var mc in GetComponents<MeshCollider>())
            Destroy(mc);

        Vector3 localCenter = Vector3.zero;
        float localR = 0.5f;

        var mf = GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            // Mesh bounds are in mesh space; if filter is on this object, use directly
            if (mf.transform == transform)
            {
                localCenter = b.center;
                localR = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            }
            else
            {
                // Approximate from world renderer bounds → local
                var rend = GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    Bounds wb = rend.bounds;
                    localCenter = transform.InverseTransformPoint(wb.center);
                    float worldR = Mathf.Max(wb.extents.x, wb.extents.y, wb.extents.z);
                    float scale = MaxAbsScale(transform);
                    localR = worldR / Mathf.Max(scale, 0.0001f);
                }
            }
        }

        landRadius = Mathf.Max(0.2f, localR);
        landCol = gameObject.GetComponent<SphereCollider>();
        if (landCol == null) landCol = gameObject.AddComponent<SphereCollider>();
        landCol.center = localCenter;
        landCol.radius = landRadius;
        landCol.isTrigger = false;
    }

    void OnEnable() { if (!All.Contains(this)) All.Add(this); }
    void OnDisable() => All.Remove(this);

    void Awake()
    {
        if (driftDir.sqrMagnitude < 0.0001f) driftDir = Random.onUnitSphere;
        if (spinAxis.sqrMagnitude < 0.0001f) spinAxis = Vector3.up;
        EnsureLandCollider();
    }

    void Update()
    {
        if (recoilDamping > 0f)
            recoil = Vector3.Lerp(recoil, Vector3.zero, recoilDamping * Time.deltaTime);

        transform.position += (driftDir * driftSpeed + recoil) * Time.deltaTime;
        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.Self);
    }

    static float MaxAbsScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }
}
