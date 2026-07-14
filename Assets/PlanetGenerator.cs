using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a kick-reachable journey: Starting Planet → debris phases → Earth marker.
/// MeshColliders are added only near the player via DebrisProximityColliders.
/// </summary>
public class PlanetGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject startingPlanetPrefab;
    public GameObject[] nearbyPlanetPrefabs;
    public GameObject[] debrisPrefabs;
    [Tooltip("Optional. If empty, Earth is a tagged empty marker you can parent a mesh under later.")]
    public GameObject earthPrefab;

    [Header("Journey / Earth")]
    public Vector3 journeyDirection = new Vector3(0.35f, 0.15f, 1f);
    [Tooltip("Distance from starting-planet SURFACE to Earth")]
    public float earthDistance = 1200f;
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
    public float debrisMinScale = 4f;
    public float debrisMaxScale = 12f;

    [Header("Asteroid Drift")]
    public float driftRadius = 14f;
    [Tooltip("Slowest rocks")]
    public float minDriftSpeed = 0.15f;
    [Tooltip("Fastest rocks")]
    public float maxDriftSpeed = 1.6f;
    public float minSpinSpeed = 4f;
    public float maxSpinSpeed = 28f;
    public float spikeDriftMultiplier = 1.25f;

    [Header("Legacy Planet Field (optional flavor)")]
    public int numberOfPlanets = 3;
    public float minSpawnRadius = 1200f;
    public float maxSpawnRadius = 2200f;
    public float minDistanceBetweenPlanets = 400f;
    public float minScale = 0.5f;
    public float maxScale = 2.0f;

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

    void Start()
    {
        GenerateWorld();
    }

    void GenerateWorld()
    {
        Vector3 dir = journeyDirection.sqrMagnitude > 0.001f
            ? journeyDirection.normalized
            : Vector3.forward;

        GameObject start = Instantiate(startingPlanetPrefab, Vector3.zero, Quaternion.identity);
        start.name = "StartingPlanet";
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

        // Stand on the planet first — aiming at debris is the player's job before kickoff
        PlacePlayerOnPlanet(start, standUp);
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
            col.radius = 80f;
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

        float minR = Mathf.Max(minSpawnRadius, startRadius + 250f);
        float maxR = Mathf.Max(maxSpawnRadius, minR + 400f);

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
            planet.transform.localScale = Vector3.one * Random.Range(minScale, maxScale);
            try { if (planet.CompareTag("Untagged")) planet.tag = "Planet"; } catch { }

            PlanetGravity g = planet.GetComponent<PlanetGravity>();
            if (g != null)
                gravityBodies.Add(g);

            spawnedPositions.Add(randomPos);
            spawnedCount++;
        }
    }

    void PlacePlayerOnPlanet(GameObject planet, Vector3 standUp)
    {
        GameObject playerGo = GameObject.FindGameObjectWithTag("Player");
        if (playerGo == null || planet == null) return;

        CharacterMovement movement = playerGo.GetComponent<CharacterMovement>();
        Transform player = playerGo.transform;
        Rigidbody rb = movement != null && movement.rb != null
            ? movement.rb
            : playerGo.GetComponent<Rigidbody>();

        float radius = GetPlanetRadius(planet);
        Vector3 surfacePoint = planet.transform.position + standUp * radius;

        player.rotation = Quaternion.FromToRotation(Vector3.up, standUp);
        AlignBottomToSurface(player, surfacePoint, standUp);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        player.SetParent(planet.transform, true);
        if (movement != null)
            movement.StickTo(planet.transform);
    }

    static void AlignBottomToSurface(Transform player, Vector3 surfacePoint, Vector3 up)
    {
        Renderer[] renderers = player.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            player.position = surfacePoint;
            return;
        }

        player.position = surfacePoint;

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        float minAlongUp = float.MaxValue;
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = c + Vector3.Scale(e, new Vector3(x, y, z));
            float along = Vector3.Dot(corner - player.position, up);
            if (along < minAlongUp)
                minAlongUp = along;
        }

        player.position = surfacePoint - up * minAlongUp;
    }

    public static float GetPlanetRadius(GameObject planet)
    {
        SphereCollider sphere = planet.GetComponent<SphereCollider>();
        if (sphere != null)
        {
            Vector3 scale = planet.transform.lossyScale;
            return sphere.radius * Mathf.Max(scale.x, scale.y, scale.z);
        }

        Renderer renderer = planet.GetComponent<Renderer>();
        if (renderer != null)
            return Mathf.Max(renderer.bounds.extents.x, renderer.bounds.extents.y, renderer.bounds.extents.z);

        return Mathf.Max(planet.transform.lossyScale.x, planet.transform.lossyScale.y, planet.transform.lossyScale.z) * 0.5f;
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
