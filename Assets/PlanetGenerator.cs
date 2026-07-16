using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a kick-reachable journey: Starting Planet → debris phases → Earth marker.
/// MeshColliders are added only near the player via DebrisProximityColliders.
/// </summary>
[DefaultExecutionOrder(-100)]
public class PlanetGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject startingPlanetPrefab;
    public GameObject[] nearbyPlanetPrefabs;
    public GameObject[] debrisPrefabs;
    [Tooltip("Optional. If empty, Earth is a tagged empty marker you can parent a mesh under later.")]
    public GameObject earthPrefab;

    [Header("Planet Size (world units — ignores tiny prefab scale)")]
    [Tooltip("Unity default sphere local radius is 0.5. Stickman ~scale 10 ≈ height ~10–20.")]
    public float startingPlanetWorldRadius = 10000f;
    public float nearbyPlanetRadiusMin = 4000f;
    public float nearbyPlanetRadiusMax = 9000f;
    [Tooltip("Fallback local mesh radius if mesh bounds can't be read. Prefab SphereSmooth ≈ 1.")]
    public float planetMeshLocalRadius = 1f;
    [Tooltip("Extra world units outside the surface when parking the player (avoids burying under mesh).")]
    public float spawnClearance = 5f;
    [Tooltip("Gravity falloff distance beyond the surface")]
    public float planetAtmosphereHeight = 2500f;

    [Header("Journey / Earth")]
    public Vector3 journeyDirection = new Vector3(0.35f, 0.15f, 1f);
    [Tooltip("Distance from starting-planet SURFACE to Earth")]
    public float earthDistance = 3500f;
    public Transform earthAnchor;

    [Header("Phases")]
    public int phaseCount = 4;
    public int steppingStonesPerPhase = 5;
    public int sideDebrisPerPhase = 6;
    [Range(0f, 0.5f)]
    public float spikeChance = 0.18f;
    public float spikeChancePerPhase = 0.05f;

    [Header("Kick Reachability")]
    [Tooltip("Max gap between stepping stones (tune with CharacterMovement.kickForce)")]
    public float maxKickGap = 130f;
    public float minKickGap = 70f;
    public float pathWidth = 45f;
    public float debrisMinScale = 8f;
    public float debrisMaxScale = 25f;

    [Header("Asteroid Drift")]
    public float driftRadius = 18f;
    [Tooltip("Slowest rocks")]
    public float minDriftSpeed = 0.15f;
    [Tooltip("Fastest rocks")]
    public float maxDriftSpeed = 1.6f;
    public float minSpinSpeed = 4f;
    public float maxSpinSpeed = 28f;
    public float spikeDriftMultiplier = 1.25f;

    [Header("Extra Flavor Planets")]
    public int numberOfPlanets = 3;
    float minDistanceBetweenPlanets = 20000f;

    [Header("Player Spawn")]
    [Tooltip("Player stands on this local up of the starting planet first")]
    public Vector3 startSurfaceNormal = Vector3.up;
    public int kickableLayerIndex = 6;

    [Header("Proximity Colliders")]
    public float colliderEnableRadius = 140f;
    public float colliderDisableRadius = 200f;

    public Transform Earth { get; private set; }
    public Vector3[] PhaseCenters { get; private set; }

    readonly List<Vector3> spawnedPositions = new List<Vector3>();
    readonly List<PlanetGravity> gravityBodies = new List<PlanetGravity>();

    bool worldGenerated;

    void Awake()
    {
        // Must run before first FixedUpdate — scene stickman starts at y≈6170 inside R=10000.
        GenerateWorld();
    }

    void GenerateWorld()
    {
        if (worldGenerated) return;
        worldGenerated = true;

        Vector3 dir = journeyDirection.sqrMagnitude > 0.001f
            ? journeyDirection.normalized
            : Vector3.forward;

        GameObject start = Instantiate(startingPlanetPrefab, Vector3.zero, Quaternion.identity);
        start.name = "StartingPlanet";
        // Prefabs are often unit-scale (1,1,1) — force a gigantic world radius here
        SetPlanetWorldRadius(start, startingPlanetWorldRadius, planetMeshLocalRadius);
        ConfigurePlanetGravity(start, planetAtmosphereHeight);
        if (string.IsNullOrEmpty(start.tag) || start.tag == "Untagged")
            start.tag = "Planet";
        spawnedPositions.Add(Vector3.zero);
        PlanetGravity startGravity = start.GetComponent<PlanetGravity>();
        if (startGravity != null)
            gravityBodies.Add(startGravity);

        float startRadius = GetPlanetRadius(start);
        Vector3 leaveDir = dir;

        // Path leaves from the same surface the player stands on, so first kicks aim at nearby debris
        Vector3 standUp = startSurfaceNormal.sqrMagnitude > 0.001f
            ? startSurfaceNormal.normalized
            : Vector3.up;
        // Blend journey into standing hemisphere so rocks appear in front of a standing player
        if (Vector3.Dot(leaveDir, standUp) < 0.35f)
            leaveDir = Vector3.Slerp(leaveDir, standUp, 0.55f).normalized;

        Physics.SyncTransforms();
        PlacePlayerOnPlanet(start, standUp);

        Vector3 pathOrigin = start.transform.position + leaveDir * (startRadius + minKickGap * 0.85f);
        Vector3 earthPos = start.transform.position + leaveDir * (startRadius + earthDistance);

        Earth = CreateOrPlaceEarth(earthPos);
        PhaseCenters = BuildPhaseCenters(pathOrigin, earthPos, phaseCount);

        EnsureProximitySystem();

        for (int phase = 0; phase < PhaseCenters.Length; phase++)
        {
            float phaseT = PhaseCenters.Length <= 1 ? 1f : phase / (float)(PhaseCenters.Length - 1);
            float phaseSpikeChance = Mathf.Clamp01(spikeChance + spikeChancePerPhase * phase);
            GeneratePhaseCorridor(
                phase,
                phase == 0 ? pathOrigin : PhaseCenters[phase - 1],
                PhaseCenters[phase],
                phaseSpikeChance,
                phaseT);
        }

        GeneratePhaseCorridor(
            phaseCount,
            PhaseCenters.Length > 0 ? PhaseCenters[PhaseCenters.Length - 1] : pathOrigin,
            earthPos,
            Mathf.Clamp01(spikeChance + spikeChancePerPhase * phaseCount),
            1f);

        SpawnFlavorPlanets(startRadius);

        foreach (PlanetGravity gra in gravityBodies)
        {
            if (gra != null)
                gra.getPlanets();
        }
        gravityBodies.Clear();

        // One more snap after debris/physics settle
        StartCoroutine(ResnapPlayerNextFrame(start, standUp));
    }

    IEnumerator ResnapPlayerNextFrame(GameObject planet, Vector3 standUp)
    {
        yield return new WaitForFixedUpdate();
        Physics.SyncTransforms();
        if (planet != null)
            PlacePlayerOnPlanet(planet, standUp);
    }

    void EnsureProximitySystem()
    {
        DebrisProximityColliders prox = GetComponent<DebrisProximityColliders>();
        if (prox == null)
            prox = gameObject.AddComponent<DebrisProximityColliders>();
        prox.enableRadius = colliderEnableRadius;
        prox.disableRadius = colliderDisableRadius;
    }

    Transform CreateOrPlaceEarth(Vector3 earthPos)
    {
        if (earthAnchor != null)
        {
            earthAnchor.position = earthPos;
            try { earthAnchor.tag = "Earth"; } catch { }
            return earthAnchor;
        }

        GameObject earthGo;
        if (earthPrefab != null)
        {
            earthGo = Instantiate(earthPrefab, earthPos, Quaternion.identity, transform);
        }
        else
        {
            earthGo = new GameObject("Earth_Target");
            earthGo.transform.SetParent(transform);
            earthGo.transform.position = earthPos;
            SphereCollider col = earthGo.AddComponent<SphereCollider>();
            col.isTrigger = true;
            // Readable target volume relative to planet-scale journey
            col.radius = Mathf.Max(200f, startingPlanetWorldRadius * 0.04f);
        }

        earthGo.name = "Earth";
        try { earthGo.tag = "Earth"; } catch { }
        return earthGo.transform;
    }

    static Vector3[] BuildPhaseCenters(Vector3 pathOrigin, Vector3 earthPos, int phases)
    {
        int count = Mathf.Max(1, phases);
        Vector3[] centers = new Vector3[count];
        for (int i = 0; i < count; i++)
            centers[i] = Vector3.Lerp(pathOrigin, earthPos, (i + 1f) / (count + 1f));
        return centers;
    }

    void GeneratePhaseCorridor(
        int phaseIndex,
        Vector3 from,
        Vector3 to,
        float phaseSpikeChance,
        float difficulty)
    {
        if (debrisPrefabs == null || debrisPrefabs.Length == 0) return;

        Vector3 span = to - from;
        float length = span.magnitude;
        if (length < 1f) return;

        Vector3 forward = span / length;
        Vector3 right = Vector3.Cross(forward, Vector3.up);
        if (right.sqrMagnitude < 0.01f)
            right = Vector3.Cross(forward, Vector3.right);
        right.Normalize();
        Vector3 up = Vector3.Cross(right, forward).normalized;

        // Wider spacing — fewer stones, longer kicks
        float gap = Mathf.Lerp(minKickGap, maxKickGap, Mathf.Clamp01(difficulty * 0.85f));
        int stones = Mathf.Max(2, Mathf.CeilToInt(length / gap));
        stones = Mathf.Min(stones, steppingStonesPerPhase);

        Vector3 prev = from;
        for (int i = 1; i <= stones; i++)
        {
            float t = i / (float)(stones + 1);
            Vector3 basePos = Vector3.Lerp(from, to, t);

            float wobble = Mathf.Sin(t * Mathf.PI * 2f + phaseIndex) * pathWidth * 0.2f;
            Vector3 lateral = right * wobble + up * (Mathf.Cos(t * Mathf.PI * 3f + phaseIndex) * pathWidth * 0.12f);
            Vector3 pos = basePos + lateral;

            float distFromPrev = Vector3.Distance(prev, pos);
            if (distFromPrev < minKickGap)
                pos = prev + (pos - prev).normalized * minKickGap;
            else if (distFromPrev > maxKickGap)
                pos = prev + (pos - prev).normalized * maxKickGap;

            bool isSpike = Random.value < phaseSpikeChance * 0.55f;
            SpawnDebris(pos, isSpike);
            prev = pos;
        }

        int sideCount = sideDebrisPerPhase;
        for (int i = 0; i < sideCount; i++)
        {
            float t = Random.Range(0.05f, 0.95f);
            Vector3 basePos = Vector3.Lerp(from, to, t);
            Vector3 offset =
                right * Random.Range(-pathWidth, pathWidth) +
                up * Random.Range(-pathWidth * 0.7f, pathWidth * 0.7f) +
                forward * Random.Range(-gap * 0.25f, gap * 0.25f);

            SpawnDebris(basePos + offset, Random.value < phaseSpikeChance);
        }
    }

    void SpawnDebris(Vector3 position, bool isSpike)
    {
        GameObject prefab = debrisPrefabs[Random.Range(0, debrisPrefabs.Length)];
        GameObject rock = Instantiate(prefab, position, Random.rotation, transform);

        float scale = Random.Range(debrisMinScale, debrisMaxScale);
        rock.transform.localScale = Vector3.one * scale;
        rock.layer = kickableLayerIndex;

        try { rock.tag = isSpike ? "Spike" : "Debris"; }
        catch { }

        // No MeshCollider here — DebrisProximityColliders adds them near the player only

        Rigidbody rb = rock.GetComponent<Rigidbody>();
        if (rb == null)
            rb = rock.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        AsteroidMotion motion = rock.GetComponent<AsteroidMotion>();
        if (motion == null)
            motion = rock.AddComponent<AsteroidMotion>();

        // Wide speed variety: many slow, few faster
        float speedT = Mathf.Pow(Random.value, 1.6f); // bias toward slower
        float speed = Mathf.Lerp(minDriftSpeed, maxDriftSpeed, speedT);
        if (isSpike)
            speed *= spikeDriftMultiplier;
        float spin = Mathf.Lerp(minSpinSpeed, maxSpinSpeed, Random.value);
        float radius = driftRadius * Random.Range(0.65f, 1.25f);

        motion.Init(position, radius, speed, spin, isSpike);
        rock.name = isSpike ? $"Spike_{rock.GetInstanceID()}" : $"Debris_{rock.GetInstanceID()}";
    }

    void SpawnFlavorPlanets(float startRadius)
    {
        if (nearbyPlanetPrefabs == null || nearbyPlanetPrefabs.Length == 0)
            return;

        // Keep other planets well clear of the starting world's body
        float minR = startRadius + Mathf.Max(nearbyPlanetRadiusMax, 10000f);
        float maxR = minR + Mathf.Max(startingPlanetWorldRadius, 50000f);

        int attempts = 0;
        int spawnedCount = 0;
        while (spawnedCount < numberOfPlanets && attempts < 80)
        {
            attempts++;
            Vector3 randomPos = Random.onUnitSphere * Random.Range(minR, maxR);
            if (!IsValidPosition(randomPos))
                continue;

            GameObject prefab = nearbyPlanetPrefabs[Random.Range(0, nearbyPlanetPrefabs.Length)];
            GameObject planet = Instantiate(prefab, randomPos, Random.rotation, transform);
            float worldRadius = Random.Range(nearbyPlanetRadiusMin, nearbyPlanetRadiusMax);
            SetPlanetWorldRadius(planet, worldRadius, planetMeshLocalRadius);
            ConfigurePlanetGravity(planet, planetAtmosphereHeight * Random.Range(0.7f, 1.1f));
            try { if (planet.CompareTag("Untagged")) planet.tag = "Planet"; } catch { }

            PlanetGravity g = planet.GetComponent<PlanetGravity>();
            if (g != null)
                gravityBodies.Add(g);

            spawnedPositions.Add(randomPos);
            spawnedCount++;
        }
    }

    /// <summary>
    /// Scales the planet so the rendered mesh outer shell reaches worldRadius,
    /// then syncs SphereCollider to that same local radius (never smaller than the mesh).
    /// </summary>
    public static void SetPlanetWorldRadius(GameObject planet, float worldRadius, float meshLocalRadius = 1f)
    {
        if (planet == null || worldRadius <= 0.01f) return;

        Vector3 localCenter;
        float localRadius = ResolveLocalMeshRadius(planet, meshLocalRadius, out localCenter);

        SphereCollider sphere = planet.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            sphere.center = localCenter;
            sphere.radius = localRadius;
        }

        float uniformScale = worldRadius / localRadius;
        planet.transform.localScale = Vector3.one * uniformScale;

        Physics.SyncTransforms();
    }

    /// <summary>
    /// Local radius that fully covers the visual mesh. Undersizing this buries the player under the shell.
    /// </summary>
    public static float ResolveLocalMeshRadius(GameObject planet, float fallback, out Vector3 localCenter)
    {
        localCenter = Vector3.zero;
        float best = 0f;

        MeshFilter mf = planet != null ? planet.GetComponentInChildren<MeshFilter>() : null;
        Mesh mesh = mf != null ? mf.sharedMesh : null;
        if (mesh != null)
        {
            Bounds b = mesh.bounds;
            localCenter = b.center;
            // Max axis extent ≈ sphere radius. Do NOT use extents.magnitude (that is R*√3).
            best = Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z));
        }

        SphereCollider sphere = planet != null ? planet.GetComponent<SphereCollider>() : null;
        if (sphere != null)
        {
            if (best < 0.01f)
                localCenter = sphere.center;
            // Collider must not be smaller than the mesh, or spawn sits under the visual shell
            best = Mathf.Max(best, sphere.radius);
        }

        if (fallback > 0.01f)
            best = Mathf.Max(best, fallback);

        if (best < 0.01f)
            best = 1f;

        if (localCenter.sqrMagnitude < 0.0001f)
            localCenter = Vector3.zero;

        return best;
    }

    static void ConfigurePlanetGravity(GameObject planet, float atmosphereHeight)
    {
        PlanetGravity g = planet.GetComponent<PlanetGravity>();
        if (g == null) return;
        g.atmosphereHeight = atmosphereHeight;
    }

    void PlacePlayerOnPlanet(GameObject planet, Vector3 standUp)
    {
        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null || planet == null) return;

        CharacterMovement movement = playerGo.GetComponent<CharacterMovement>();
        if (movement != null)
        {
            movement.SpawnOnSurface(planet, standUp, spawnClearance);
            return;
        }

        Transform player = playerGo.transform;
        Rigidbody rb = playerGo.GetComponent<Rigidbody>();
        float radius = GetPlanetRadius(planet);
        Vector3 center = GetPlanetCenter(planet);
        Vector3 dir = standUp.normalized;
        float clearance = Mathf.Max(spawnClearance, radius * 0.0005f);
        Vector3 point = center + dir * (radius + clearance);

        player.SetParent(null, true);
        player.rotation = Quaternion.FromToRotation(Vector3.up, dir);
        player.position = point;
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.useGravity = false;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.position = player.position;
            rb.rotation = player.rotation;
        }
    }

    public static Vector3 GetPlanetCenter(GameObject planet)
    {
        SphereCollider sphere = planet.GetComponent<SphereCollider>();
        if (sphere != null)
            return planet.transform.TransformPoint(sphere.center);

        Collider col = planet.GetComponent<Collider>();
        if (col != null)
            return col.bounds.center;

        Renderer r = planet.GetComponentInChildren<Renderer>();
        if (r != null)
            return r.bounds.center;

        return planet.transform.position;
    }

    public static float GetPlanetRadius(GameObject planet)
    {
        SphereCollider sphere = planet.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 s = planet.transform.lossyScale;
            float maxS = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
            return sphere.radius * maxS;
        }

        Renderer renderer = planet.GetComponentInChildren<Renderer>();
        if (renderer != null)
        {
            Vector3 e = renderer.bounds.extents;
            return Mathf.Max(e.x, e.y, e.z);
        }

        Collider col = planet.GetComponent<Collider>();
        if (col != null)
        {
            Vector3 e = col.bounds.extents;
            return Mathf.Max(e.x, e.y, e.z);
        }

        Vector3 ls = planet.transform.lossyScale;
        return Mathf.Max(Mathf.Abs(ls.x), Mathf.Abs(ls.y), Mathf.Abs(ls.z)) * 0.985f;
    }

    bool IsValidPosition(Vector3 pos)
    {
        foreach (Vector3 existingPos in spawnedPositions)
        {
            if (Vector3.Distance(pos, existingPos) < minDistanceBetweenPlanets)
                return false;
        }

        if (Earth != null && Vector3.Distance(pos, Earth.position) < minDistanceBetweenPlanets)
            return false;

        return true;
    }
}
