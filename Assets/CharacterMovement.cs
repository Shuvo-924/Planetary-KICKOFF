using System.Collections;
using UnityEngine;

/// <summary>
/// Standable surfaces are found with raycasts. Rigidbody stays non-kinematic
/// so PlanetGravity keeps pulling; grounding only cancels motion into the surface.
/// </summary>
[DefaultExecutionOrder(-200)]
public class CharacterMovement : MonoBehaviour
{
    public Rigidbody rb;
    public Animator anim;
    public Transform cam;
    public Transform feet;

    [Header("Kick Settings")]
    public float kickForce = 1600f;
    public float energy = 100f;
    public float kickEnergyCost = 10f;
    public LayerMask kickableLayer;
    [Range(0.4f, 1f)]
    public float aimKickBlend = 0.85f;

    [Header("Ground Detection (raycast physics)")]
    public float groundProbeRadius = 0.45f;
    public float groundProbeDistance = 6f;
    [Tooltip("Extra lift above the collider surface")]
    public float groundStickOffset = 0.15f;
    [Tooltip("Distance from pivot to soles. 0 = auto. This stickman root is at the feet.")]
    public float standHeight = 0f;
    public float groundSnapStrength = 25f;
    public float approachRotateRadius = 55f;
    public float uprightRotateSpeed = 8f;

    [Header("Jets")]
    public int jetCharges = 3;
    public float jetImpulse = 280f;
    public float jetEnergyCost = 5f;
    public KeyCode jetKey = KeyCode.Space;

    bool isGrounded;
    float spikeCooldown;
    float landLockTimer;
    float spawnGraceTimer;
    Vector3 groundHitPoint;
    Vector3 groundHitNormal = Vector3.up;
    Collider groundHitCollider;
    Transform groundTransform;
    Vector3 groundLocalPoint;
    Vector3 lastGroundWorldPoint;
    bool hasGroundMemory;
    Vector3 gravityDown = Vector3.down;
    float strongestGravityPull;

    bool hasSpawned;

    public bool IsGrounded => isGrounded;
    public bool IsStuck => isGrounded;
    public bool InSpawnGrace => spawnGraceTimer > 0f;
    public int JetCharges => jetCharges;
    public Vector3 GravityDown => gravityDown;
    public Vector3 SurfaceNormal => isGrounded ? groundHitNormal : -gravityDown;

    public void NotifyGravityPull(Vector3 towardCenter, float strength)
    {
        if (!hasSpawned || InSpawnGrace) return;
        if (strength >= strongestGravityPull)
        {
            strongestGravityPull = strength;
            if (towardCenter.sqrMagnitude > 0.0001f)
                gravityDown = towardCenter.normalized;
        }
    }

    void Awake()
    {
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        // Only freeze if PlanetGenerator has not already parked us (Awake order is undefined)
        if (rb != null && !hasSpawned)
        {
            rb.isKinematic = true;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    /// <summary>
    /// Places the player on the planet outer shell (collider synced to mesh).
    /// </summary>
    public void SpawnOnSurface(GameObject planet, Vector3 outward, float clearance = 5f)
    {
        if (planet == null) return;
        if (rb == null)
            rb = GetComponent<Rigidbody>();

        transform.SetParent(null, true);
        Physics.SyncTransforms();

        SphereCollider sphere = planet.GetComponent<SphereCollider>();
        Vector3 center;
        float radius;

        if (sphere != null)
        {
            center = planet.transform.TransformPoint(sphere.center);
            radius = sphere.radius * MaxAbsScale(planet.transform);
        }
        else
        {
            center = PlanetGenerator.GetPlanetCenter(planet);
            radius = Mathf.Max(0.1f, PlanetGenerator.GetPlanetRadius(planet));
        }

        // Prefer renderer bounds if they stick out past the collider (visual shell)
        Renderer rend = planet.GetComponentInChildren<Renderer>();
        if (rend != null)
        {
            Vector3 e = rend.bounds.extents;
            float visualR = Mathf.Max(e.x, Mathf.Max(e.y, e.z));
            radius = Mathf.Max(radius, visualR);
            center = rend.bounds.center;
        }

        Vector3 normal = outward.sqrMagnitude > 0.0001f ? outward.normalized : Vector3.up;
        float pad = Mathf.Max(clearance, radius * 0.0005f, 1f);
        Vector3 surfacePoint = center + normal * radius;

        if (sphere != null)
        {
            Ray ray = new Ray(center + normal * (radius + Mathf.Max(50f, radius * 0.1f)), -normal);
            if (sphere.Raycast(ray, out RaycastHit hit, radius * 3f + 100f))
            {
                // If raycast hit is closer than analytic/visual radius, keep the farther surface
                float hitDist = Vector3.Distance(center, hit.point);
                if (hitDist >= radius - 0.01f)
                {
                    surfacePoint = hit.point;
                    normal = hit.normal.normalized;
                }
            }
        }

        float lift = ResolveStandHeight() + pad;
        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, normal);
        Quaternion rot = Quaternion.LookRotation(forward.normalized, normal);

        Vector3 rootPos = surfacePoint + normal * lift;
        Teleport(rootPos, rot);

        gravityDown = -normal;
        RememberGround(surfacePoint, normal, sphere != null ? sphere : planet.GetComponent<Collider>());
        isGrounded = true;
        landLockTimer = 1f;
        spawnGraceTimer = 1.5f;
        hasSpawned = true;
    }

    public void SpawnOnSurface(GameObject planet, Vector3 outward)
    {
        SpawnOnSurface(planet, outward, 5f);
    }

    void Teleport(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
        if (rb == null) return;

        rb.isKinematic = false;
        rb.useGravity = false;
        rb.position = position;
        rb.rotation = rotation;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
    }

    float ResolveStandHeight()
    {
        if (standHeight > 0.01f)
            return standHeight + groundStickOffset;

        // Feet helper with real offset
        if (feet != null && Mathf.Abs(feet.localPosition.y) > 0.01f)
            return Mathf.Abs(feet.localPosition.y) * Mathf.Abs(transform.lossyScale.y) + groundStickOffset;

        // Stickman6 Rig: root/hips ≈ feet (local Y ~ 0). Do NOT use scale*0.95 — that floats or misplaces.
        float s = Mathf.Max(transform.lossyScale.y, 1f);
        return groundStickOffset + s * 0.05f; // ~0.65 at scale 10 — just clear the crust
    }

    static float MaxAbsScale(Transform t)
    {
        Vector3 s = t.lossyScale;
        return Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
    }

    public void StickTo(Transform surface)
    {
        if (surface == null) return;
        Vector3 outward = (transform.position - surface.position).normalized;
        if (outward.sqrMagnitude < 0.01f)
            outward = transform.up;
        SpawnOnSurface(surface.gameObject, outward);
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0) && energy >= kickEnergyCost)
            TryKickoff();

        if (!isGrounded && Input.GetKeyDown(jetKey))
            TryFireJet();
    }

    void FixedUpdate()
    {
        if (!hasSpawned) return;

        if (spikeCooldown > 0f)
            spikeCooldown -= Time.fixedDeltaTime;
        if (landLockTimer > 0f)
            landLockTimer -= Time.fixedDeltaTime;
        if (spawnGraceTimer > 0f)
            spawnGraceTimer -= Time.fixedDeltaTime;

        strongestGravityPull *= 0.35f;
        if (isGrounded)
            gravityDown = -groundHitNormal;

        ProbeGround();

        if (isGrounded)
            StayOnGround();
        else if (!InSpawnGrace)
            UpdateAirborneUpright();
    }

    void ProbeGround()
    {
        if (rb == null) return;

        Vector3 down = gravityDown.sqrMagnitude > 0.001f ? gravityDown.normalized : -transform.up;
        Vector3 feetPos = GetFeetPosition();
        Vector3 origin = feetPos - down * (groundProbeRadius + 0.2f);
        float castDist = groundProbeDistance + groundProbeRadius + ResolveStandHeight() + 1f;

        if (!Physics.SphereCast(origin, groundProbeRadius, down, out RaycastHit hit, castDist, kickableLayer, QueryTriggerInteraction.Ignore))
        {
            // During spawn grace keep "grounded" on remembered surface even if cast misses one frame
            if (InSpawnGrace && hasGroundMemory)
                return;

            isGrounded = false;
            hasGroundMemory = false;
            groundTransform = null;
            return;
        }

        if (hit.collider.CompareTag("Spike"))
        {
            ApplySpikeHit();
            rb.AddForce(hit.normal * (kickForce * 0.12f), ForceMode.VelocityChange);
            isGrounded = false;
            return;
        }

        if (Vector3.Dot(hit.normal, -down) < 0.15f && landLockTimer <= 0f)
        {
            isGrounded = false;
            return;
        }

        bool wasGrounded = isGrounded;
        RememberGround(hit);
        isGrounded = true;

        if (!wasGrounded && landLockTimer <= 0f && anim != null)
            anim.SetTrigger("Land");
    }

    void StayOnGround()
    {
        if (rb == null || !isGrounded) return;

        Vector3 surfacePoint = groundHitPoint;
        Vector3 normal = groundHitNormal;

        if (groundTransform != null && hasGroundMemory)
        {
            Vector3 moved = groundTransform.TransformPoint(groundLocalPoint);
            lastGroundWorldPoint = moved;
            surfacePoint = moved;
        }

        PlaceFeetOnSurface(surfacePoint, normal);

        Vector3 vel = rb.linearVelocity;
        float intoGround = Vector3.Dot(vel, -normal);
        if (intoGround > 0f)
            vel += normal * intoGround;

        vel = Vector3.ProjectOnPlane(vel, normal);
        vel = Vector3.Lerp(vel, Vector3.zero, groundSnapStrength * 0.35f * Time.fixedDeltaTime);
        rb.linearVelocity = vel;
        rb.angularVelocity = Vector3.zero;

        groundHitPoint = surfacePoint;
        groundHitNormal = normal;
    }

    void UpdateAirborneUpright()
    {
        Vector3 up = -gravityDown;
        if (up.sqrMagnitude < 0.01f) return;

        AsteroidMotion near = FindNearestDebris(approachRotateRadius, out _);
        if (near != null && !near.isSpike)
        {
            Vector3 toRock = transform.position - near.WorldBounds.center;
            if (toRock.sqrMagnitude > 0.01f)
                up = toRock.normalized;
        }

        Quaternion target = Quaternion.FromToRotation(transform.up, up) * transform.rotation;
        transform.rotation = Quaternion.Slerp(transform.rotation, target, uprightRotateSpeed * Time.fixedDeltaTime);
    }

    void TryKickoff()
    {
        Vector3 down = gravityDown.sqrMagnitude > 0.001f ? gravityDown.normalized : -transform.up;
        Vector3 origin = GetFeetPosition() - down * 0.1f;
        if (!Physics.Raycast(origin, down, out RaycastHit hit, groundProbeDistance + ResolveStandHeight() + 2f, kickableLayer, QueryTriggerInteraction.Ignore))
            return;

        if (hit.collider.CompareTag("Spike"))
        {
            ApplySpikeHit();
            return;
        }

        if (anim != null)
            anim.SetTrigger("KickTrigger");

        isGrounded = false;
        hasGroundMemory = false;
        groundTransform = null;
        spawnGraceTimer = 0f;
        transform.SetParent(null, true);
        landLockTimer = 0.15f;

        if (rb != null)
            rb.isKinematic = false;

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
        if (jetCharges <= 0 || energy < jetEnergyCost || rb == null)
            return;

        Vector3 look = cam != null ? cam.forward : transform.forward;
        Vector3 boostDir = (look - gravityDown * 0.35f).normalized;

        rb.AddForce(boostDir * jetImpulse, ForceMode.Impulse);
        jetCharges--;
        energy -= jetEnergyCost;
        isGrounded = false;
        spawnGraceTimer = 0f;

        if (CameraShake.Instance != null)
            CameraShake.Instance.ShakeFromKick(jetImpulse * 0.35f, rb.linearVelocity.magnitude * 0.5f);
    }

    void PlaceFeetOnSurface(Vector3 surfacePoint, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;
        normal.Normalize();

        float height = ResolveStandHeight();
        Vector3 forward = Vector3.ProjectOnPlane(transform.forward, normal);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.forward, normal);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, normal);

        Quaternion rot = Quaternion.LookRotation(forward.normalized, normal);
        Vector3 rootPos = surfacePoint + normal * height;

        if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(rootPos);
            rb.MoveRotation(rot);
        }
        else
        {
            transform.SetPositionAndRotation(rootPos, rot);
        }
    }

    void RememberGround(RaycastHit hit)
    {
        RememberGround(hit.point, hit.normal, hit.collider);
    }

    void RememberGround(Vector3 point, Vector3 normal, Collider col)
    {
        groundHitPoint = point;
        groundHitNormal = normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        groundHitCollider = col;
        groundTransform = col != null ? col.transform : null;

        if (groundTransform != null)
        {
            groundLocalPoint = groundTransform.InverseTransformPoint(point);
            lastGroundWorldPoint = point;
            hasGroundMemory = true;
        }
        else
        {
            hasGroundMemory = false;
        }

        AsteroidMotion rock = col != null ? col.GetComponentInParent<AsteroidMotion>() : null;
        if (rock != null)
            rock.SetColliderActive(true);
    }

    Vector3 GetFeetPosition()
    {
        if (feet != null && feet.localPosition.sqrMagnitude > 0.001f)
            return feet.position;

        // Root ≈ feet on this rig
        return transform.position;
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

            Bounds b = rock.WorldBounds;
            float d = Mathf.Sqrt(sq) - Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            if (d < dist)
            {
                dist = d;
                best = rock;
            }
        }

        return best;
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

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Earth"))
            Debug.Log("Reached Earth — attach your Earth mesh under the Earth object.");
    }
}
