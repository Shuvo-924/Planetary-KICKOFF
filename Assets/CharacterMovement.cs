using System.Collections;
using UnityEngine;

public class CharacterMovement : MonoBehaviour
{
    public Rigidbody rb;
    public Animator anim;
    public Transform cam;
    public Transform feet;

    [Header("Kick Settings")]
    [Tooltip("Impulse strength — keep in sync with PlanetGenerator.maxKickGap")]
    public float kickForce = 1600f;
    public float energy = 100f;
    public float kickEnergyCost = 10f;
    public LayerMask kickableLayer;
    [Tooltip("How much camera aim vs surface-normal push when kicking off")]
    [Range(0.4f, 1f)]
    public float aimKickBlend = 0.85f;

    [Header("Approach / Auto-stand on debris")]
    [Tooltip("Start rotating upright toward a debris inside this range")]
    public float approachRotateRadius = 55f;
    [Tooltip("Snap-stand on debris inside this range (uses bounds, collider optional)")]
    public float autoStandRadius = 12f;
    public float uprightRotateSpeed = 6f;
    public float landProbeRadius = 2.5f;
    public float landProbeDistance = 4f;

    [Header("Jets — mid-air trajectory (limited charges)")]
    public int jetCharges = 3;
    public float jetImpulse = 280f;
    public float jetEnergyCost = 5f;
    public KeyCode jetKey = KeyCode.Space;

    bool isStuck = true;
    float spikeCooldown;
    AsteroidMotion pendingStandTarget;

    public bool IsStuck => isStuck;
    public int JetCharges => jetCharges;

    public void StickTo(Transform surface)
    {
        isStuck = true;
        pendingStandTarget = null;
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (surface != null)
            transform.SetParent(surface, true);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && energy >= kickEnergyCost)
            StartKickoff();

        if (!isStuck && Input.GetKeyDown(jetKey))
            TryFireJet();
    }

    void FixedUpdate()
    {
        if (spikeCooldown > 0f)
            spikeCooldown -= Time.fixedDeltaTime;

        if (!isStuck)
        {
            UpdateApproachUpright();
            TryProximityLand();
        }
    }

    void StartKickoff()
    {
        Vector3 origin = feet != null ? feet.position : transform.position;
        if (!Physics.Raycast(origin, -transform.up, out RaycastHit hit, 8f, kickableLayer))
            return;

        if (hit.collider.CompareTag("Spike"))
        {
            ApplySpikeHit();
            return;
        }

        if (anim != null)
            anim.SetTrigger("KickTrigger");

        isStuck = false;
        pendingStandTarget = null;
        if (rb != null)
            rb.isKinematic = false;
        transform.SetParent(null);

        // Aim with camera toward a debris, while still pushing off the surface
        Vector3 away = hit.normal;
        Vector3 aim = cam != null ? cam.forward : transform.forward;
        Vector3 launch = Vector3.Slerp(away, aim.normalized, aimKickBlend).normalized;
        ApplyKickForce(launch);
    }

    void ApplyKickForce(Vector3 direction)
    {
        if (rb != null)
            rb.AddForce(direction * kickForce, ForceMode.Impulse);
        energy -= kickEnergyCost;
        StartCoroutine(ShakeAfterKick());
    }

    void TryFireJet()
    {
        if (jetCharges <= 0 || energy < jetEnergyCost || rb == null || rb.isKinematic)
            return;

        Vector3 look = cam != null ? cam.forward : transform.forward;
        Vector3 boostDir = (look + transform.up * 0.35f).normalized;

        rb.AddForce(boostDir * jetImpulse, ForceMode.Impulse);
        jetCharges--;
        energy -= jetEnergyCost;

        if (CameraShake.Instance != null)
            CameraShake.Instance.ShakeFromKick(jetImpulse * 0.35f, rb.linearVelocity.magnitude * 0.5f);
    }

    /// <summary>
    /// While flying near debris, rotate upright to that rock; close enough → stand on it.
    /// Uses renderer bounds so it works before a MeshCollider is spawned.
    /// </summary>
    void UpdateApproachUpright()
    {
        AsteroidMotion nearest = FindNearestDebris(approachRotateRadius, out float dist);
        if (nearest == null)
        {
            pendingStandTarget = null;
            return;
        }

        if (nearest.isSpike)
            return;

        Bounds bounds = nearest.WorldBounds;
        Vector3 up = (transform.position - bounds.center).normalized;
        if (up.sqrMagnitude < 0.01f)
            up = -rb.linearVelocity.normalized;

        Quaternion targetRot = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, uprightRotateSpeed * Time.fixedDeltaTime);

        pendingStandTarget = nearest;

        if (dist <= autoStandRadius)
            StandOnDebris(nearest);
    }

    void StandOnDebris(AsteroidMotion rock)
    {
        if (rock == null || rock.isSpike) return;

        // Ensure collider exists for future kickoffs from this rock
        rock.SetColliderActive(true);

        Bounds bounds = rock.WorldBounds;
        Vector3 up = (transform.position - bounds.center).normalized;
        if (up.sqrMagnitude < 0.01f)
            up = transform.up;

        Vector3 surfacePoint = bounds.center + up * ApproximateRadius(bounds);

        StickTo(rock.transform);
        transform.rotation = Quaternion.FromToRotation(Vector3.up, up);

        AlignFeetToPoint(surfacePoint, up);

        if (anim != null)
            anim.SetTrigger("Land");
    }

    static float ApproximateRadius(Bounds bounds)
    {
        return Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
    }

    void AlignFeetToPoint(Vector3 surfacePoint, Vector3 up)
    {
        if (feet != null)
        {
            transform.position += surfacePoint - feet.position;
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            transform.position = surfacePoint;
            return;
        }

        transform.position = surfacePoint;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            b.Encapsulate(renderers[i].bounds);

        float minAlong = float.MaxValue;
        Vector3 c = b.center;
        Vector3 e = b.extents;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            float along = Vector3.Dot(c + Vector3.Scale(e, new Vector3(x, y, z)) - transform.position, up);
            if (along < minAlong) minAlong = along;
        }
        transform.position = surfacePoint - up * minAlong;
    }

    AsteroidMotion FindNearestDebris(float radius, out float dist)
    {
        dist = float.MaxValue;
        AsteroidMotion best = null;
        Vector3 p = transform.position;
        float rSq = radius * radius;

        for (int i = 0; i < AsteroidMotion.All.Count; i++)
        {
            AsteroidMotion rock = AsteroidMotion.All[i];
            if (rock == null) continue;

            float sq = (rock.transform.position - p).sqrMagnitude;
            if (sq > rSq) continue;

            // Prefer distance to surface (approx)
            float d = Mathf.Sqrt(sq) - ApproximateRadius(rock.WorldBounds);
            if (d < dist)
            {
                dist = d;
                best = rock;
            }
        }

        return best;
    }

    void TryProximityLand()
    {
        if (rb == null || isStuck) return;

        // Prefer the auto-stand target if we're already near it
        if (pendingStandTarget != null)
        {
            float d = Vector3.Distance(transform.position, pendingStandTarget.transform.position)
                      - ApproximateRadius(pendingStandTarget.WorldBounds);
            if (d <= autoStandRadius)
            {
                StandOnDebris(pendingStandTarget);
                return;
            }
        }

        Vector3 origin = feet != null ? feet.position : transform.position;
        if (Physics.SphereCast(origin, landProbeRadius, -transform.up, out RaycastHit hit, landProbeDistance, kickableLayer))
            HandleSurfaceContact(hit.collider, hit.normal, hit.point);
    }

    void HandleSurfaceContact(Collider col, Vector3 normal, Vector3 contactPoint)
    {
        if (col == null || isStuck) return;

        if (col.CompareTag("Spike"))
        {
            ApplySpikeHit();
            if (rb != null && !rb.isKinematic)
                rb.AddForce(normal * (kickForce * 0.15f), ForceMode.Impulse);
            return;
        }

        if (col.CompareTag("Earth"))
        {
            StickTo(col.transform);
            AlignToNormal(normal);
            return;
        }

        AsteroidMotion rock = col.GetComponentInParent<AsteroidMotion>();
        if (rock != null)
        {
            StandOnDebris(rock);
            return;
        }

        StickTo(col.transform);
        AlignToNormal(normal);
        if (feet != null)
            transform.position += contactPoint - feet.position;

        if (anim != null)
            anim.SetTrigger("Land");
    }

    void AlignToNormal(Vector3 surfaceNormal)
    {
        if (surfaceNormal.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.FromToRotation(transform.up, surfaceNormal) * transform.rotation;
    }

    void ApplySpikeHit()
    {
        if (spikeCooldown > 0f) return;
        spikeCooldown = 0.4f;
        energy = Mathf.Max(0f, energy - 50f);

        if (CameraShake.Instance != null)
            CameraShake.Instance.AddTrauma(0.45f);
    }

    IEnumerator ShakeAfterKick()
    {
        yield return new WaitForFixedUpdate();
        if (CameraShake.Instance == null || rb == null) yield break;
        CameraShake.Instance.ShakeFromKick(kickForce, rb.linearVelocity.magnitude);
    }

    void OnCollisionEnter(Collision collision)
    {
        ContactPoint contact = collision.GetContact(0);
        HandleSurfaceContact(collision.collider, contact.normal, contact.point);
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Earth"))
        {
            StickTo(other.transform);
            Debug.Log("Reached Earth — attach your Earth mesh under the Earth object.");
        }
    }
}
