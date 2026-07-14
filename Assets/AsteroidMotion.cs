using UnityEngine;

/// <summary>
/// Drift + optional near-player MeshCollider. Speeds vary per rock.
/// </summary>
public class AsteroidMotion : MonoBehaviour
{
    public static readonly System.Collections.Generic.List<AsteroidMotion> All =
        new System.Collections.Generic.List<AsteroidMotion>();

    [HideInInspector] public Vector3 anchor;
    public float driftRadius = 12f;
    public float driftSpeed = 1f;
    public float spinSpeed = 12f;
    public bool isSpike;

    Vector3 velocity;
    Vector3 spinAxis;
    MeshCollider meshCollider;
    MeshFilter meshFilter;
    bool colliderWanted;

    public Bounds WorldBounds
    {
        get
        {
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null) return r.bounds;
            float s = Mathf.Max(transform.lossyScale.x, 1f) * 0.5f;
            return new Bounds(transform.position, Vector3.one * s);
        }
    }

    public void Init(Vector3 orbitAnchor, float radius, float moveSpeed, float spinDegPerSec, bool spike)
    {
        anchor = orbitAnchor;
        driftRadius = Mathf.Max(1f, radius);
        driftSpeed = Mathf.Max(0.05f, moveSpeed);
        spinSpeed = spinDegPerSec;
        isSpike = spike;
        velocity = Random.onUnitSphere * driftSpeed;
        spinAxis = Random.onUnitSphere.normalized;
    }

    void OnEnable()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    void OnDisable()
    {
        All.Remove(this);
    }

    void Awake()
    {
        meshFilter = GetComponentInChildren<MeshFilter>();
        if (anchor == Vector3.zero)
            anchor = transform.position;
        if (velocity.sqrMagnitude < 0.0001f)
        {
            velocity = Random.onUnitSphere * Mathf.Max(0.05f, driftSpeed);
            spinAxis = Random.onUnitSphere.normalized;
        }
    }

    void Update()
    {
        transform.position += velocity * Time.deltaTime;

        Vector3 offset = transform.position - anchor;
        float dist = offset.magnitude;
        if (dist > driftRadius && dist > 0.001f)
        {
            Vector3 normal = offset / dist;
            velocity = Vector3.Reflect(velocity, normal);
            velocity = Vector3.Lerp(velocity, -normal * driftSpeed, 0.25f);
            // Keep speed consistent with this rock's own pace
            velocity = velocity.normalized * driftSpeed;
            transform.position = anchor + normal * driftRadius;
        }

        transform.Rotate(spinAxis, spinSpeed * Time.deltaTime, Space.World);
    }

    /// <summary>Enable MeshCollider only while the player is nearby.</summary>
    public void SetColliderActive(bool active)
    {
        colliderWanted = active;
        if (!active)
        {
            if (meshCollider != null)
                meshCollider.enabled = false;
            return;
        }

        if (meshCollider == null)
        {
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
            meshCollider.convex = true;
        }

        meshCollider.enabled = true;
    }

    public bool HasActiveCollider => meshCollider != null && meshCollider.enabled;
}
