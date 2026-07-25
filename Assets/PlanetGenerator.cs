using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the level: start planet → arc of asteroids → Earth.
/// Debris MeshColliders stay off until the player is near (DebrisProximityColliders).
/// </summary>
[DefaultExecutionOrder(-100)]
public class PlanetGenerator : MonoBehaviour
{
    [Header("Prefabs")]
    [SerializeField] GameObject startingPlanetPrefab;
    [SerializeField] GameObject[] nearbyPlanetPrefabs;
    [Tooltip("Safe landable asteroids")]
    [SerializeField] GameObject[] debrisPrefabs;
    [Tooltip("Hazard spikes — different meshes from debris. Falls back to debrisPrefabs if empty.")]
    [SerializeField] GameObject[] spikePrefabs;
    [SerializeField] GameObject earthPrefab;

    [Header("Planet size (world units)")]
    [Tooltip("Keep under ~1000 to avoid float jitter")]
    [SerializeField] float startingPlanetWorldRadius = 500f;
    [SerializeField] float nearbyPlanetRadiusMin = 180f;
    [SerializeField] float nearbyPlanetRadiusMax = 320f;
    [Tooltip("SphereSmooth mesh radius; 0.9875 matches mesh, 1 floats above")]
    [SerializeField] float planetMeshLocalRadius = 0.9875f;
    [SerializeField] float spawnClearance = 8f;
    [SerializeField] float planetAtmosphereHeight = 180f;

    [Header("Journey / Earth")]
    [SerializeField] Vector3 journeyDirection = new Vector3(0.35f, 0.15f, 1f);
    [Tooltip("How far past the start planet SURFACE Earth sits (world units)")]
    [SerializeField] float earthDistance = 2800f;
    [Tooltip("Earth visual / exclusion radius after spawn")]
    [SerializeField] float earthWorldRadius = 220f;
    [Tooltip("Extra gap flavor planets must keep outside Earth's surface")]
    [SerializeField] float earthPlanetClearance = 500f;
    [Tooltip("How far along the crust asteroids travel (capped so they don't wrap the globe)")]
    [SerializeField] float debrisArcLength = 900f;
    [SerializeField] Transform earthAnchor;

    [Header("Phases")]
    [SerializeField] int phaseCount = 3;
    [SerializeField] int steppingStonesPerPhase = 5;
    [SerializeField] int sideDebrisPerPhase = 2;
    [Range(0f, 0.5f)] [SerializeField] float spikeChance = 0.15f;
    [SerializeField] float spikeChancePerPhase = 0.08f;

    [Header("Kick spacing")]
    [SerializeField] float minKickGap = 40f;
    [SerializeField] float maxKickGap = 85f;
    [SerializeField] float pathWidth = 28f;
    [SerializeField] float debrisMinScale = 8f;
    [SerializeField] float debrisMaxScale = 16f;
    [Tooltip("Spikes use their own scale (usually much smaller than debris)")]
    [SerializeField] float spikeMinScale = 2.5f;
    [SerializeField] float spikeMaxScale = 5f;
    [SerializeField] float debrisAltitude = 22f;

    [Header("Asteroid drift")]
    [SerializeField] float minDriftSpeed = 0.02f;
    [SerializeField] float maxDriftSpeed = 0.12f;
    [SerializeField] float minSpinSpeed = 0.4f;
    [SerializeField] float maxSpinSpeed = 2.5f;
    [SerializeField] float spikeDriftMultiplier = 1.15f;

    [Header("Extra planets")]
    [SerializeField] int numberOfPlanets = 2;
    [SerializeField] float minPlanetSeparation = 1400f;

    [Header("Spawn / layers")]
    [SerializeField] Vector3 startSurfaceNormal = Vector3.up;
    [SerializeField] int kickableLayerIndex = 6;
    [SerializeField] int debrisLayerIndex = 8;

    [Header("Proximity colliders")]
    [SerializeField] float colliderEnableRadius = 140f;
    [SerializeField] float colliderDisableRadius = 200f;

    public Transform Earth { get; private set; }
    public Vector3[] PhaseCenters { get; private set; }

    readonly List<Vector3> planetPositions = new List<Vector3>();
    readonly List<float> planetRadii = new List<float>();
    readonly List<Vector3> debrisPositions = new List<Vector3>();
    float earthRadiusCached;

    // Arc frame on the starting planet crust
    Vector3 arcCenter, arcUp, arcTangent, arcBinormal;
    float arcRadius;
    bool done;

    void Awake() => Build();

    void Build()
    {
        if (done) return;
        done = true;

        Vector3 journey = journeyDirection.sqrMagnitude > 0.001f
            ? journeyDirection.normalized : Vector3.forward;

        // --- Start planet ---
        GameObject start = Instantiate(startingPlanetPrefab, Vector3.zero, Quaternion.identity);
        start.name = "StartingPlanet";
        start.tag = "Planet";
        SetPlanetWorldRadius(start, startingPlanetWorldRadius, planetMeshLocalRadius);
        SetAtmosphere(start, planetAtmosphereHeight);
        RegisterPlanet(Vector3.zero, startingPlanetWorldRadius);

        Vector3 up = startSurfaceNormal.sqrMagnitude > 0.001f
            ? startSurfaceNormal.normalized : Vector3.up;
        Physics.SyncTransforms();
        PlacePlayer(start, up);

        // Brighter ambient so dark rocks read
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.35f, 0.4f, 0.55f);
        RenderSettings.ambientIntensity = 1.15f;

        // --- Arc frame (tangent along journey, on the crust) ---
        arcCenter = start.transform.position;
        arcRadius = GetPlanetRadius(start);
        arcUp = up;
        arcTangent = Vector3.ProjectOnPlane(journey, up);
        if (arcTangent.sqrMagnitude < 0.01f)
            arcTangent = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (arcTangent.sqrMagnitude < 0.01f)
            arcTangent = Vector3.ProjectOnPlane(Vector3.right, up);
        arcTangent.Normalize();
        arcBinormal = Vector3.Cross(up, arcTangent).normalized;

        // Debris stay on a short crust arc; Earth sits far out in space beyond that
        float debrisPath = GetDebrisArcLength();
        Earth = PlaceEarth(EarthWorldPosition());
        PhaseCenters = new Vector3[Mathf.Max(1, phaseCount)];
        for (int i = 0; i < PhaseCenters.Length; i++)
            PhaseCenters[i] = OnArc(debrisPath * ((i + 1f) / (phaseCount + 1f)), 0f, debrisAltitude);

        SetupProximity();
        SpawnNearStart();

        for (int p = 0; p < PhaseCenters.Length; p++)
        {
            float from = p == 0 ? minKickGap : PhaseArc(p - 1);
            float to = PhaseArc(p);
            float spike = Mathf.Clamp01(spikeChance + spikeChancePerPhase * p);
            float hard = PhaseCenters.Length <= 1 ? 1f : p / (float)(PhaseCenters.Length - 1);
            SpawnCorridor(p, from, to, spike, hard);
        }

        SpawnCorridor(phaseCount, PhaseArc(phaseCount - 1), debrisPath,
            Mathf.Clamp01(spikeChance + spikeChancePerPhase * phaseCount), 1f);

        SpawnFlavorPlanets(arcRadius);
        StartCoroutine(Resnap(start, up));
    }

    float GetDebrisArcLength()
    {
        // Cap so asteroids don't wrap around the start planet multiple times
        float maxArc = arcRadius * Mathf.PI * 0.7f; // ~126° of crust
        float wanted = debrisArcLength > 1f ? debrisArcLength : earthDistance * 0.35f;
        return Mathf.Clamp(wanted, minKickGap * 4f, maxArc);
    }

    /// <summary>Earth far along the journey — past the surface, not glued to the crust.</summary>
    Vector3 EarthWorldPosition()
    {
        // Aim toward the end of the debris arc, then push out into deep space
        float debrisPath = GetDebrisArcLength();
        Vector3 shellHint = OnArc(debrisPath, 0f, debrisAltitude);
        Vector3 dir = (shellHint - arcCenter).normalized;
        if (dir.sqrMagnitude < 0.01f) dir = arcTangent;

        float gap = Mathf.Max(earthDistance, arcRadius + earthWorldRadius + 800f);
        return arcCenter + dir * (arcRadius + gap);
    }

    IEnumerator Resnap(GameObject planet, Vector3 up)
    {
        yield return new WaitForFixedUpdate();
        Physics.SyncTransforms();
        if (planet != null) PlacePlayer(planet, up);
    }

    float PhaseArc(int phaseIndex) =>
        GetDebrisArcLength() * ((phaseIndex + 1f) / (Mathf.Max(1, phaseCount) + 1f));

    /// <summary>Point above the crust along a surface arc (never through the planet).</summary>
    Vector3 OnArc(float arcDist, float lateral, float altitude)
    {
        float angleDeg = (arcDist / Mathf.Max(arcRadius, 1f)) * Mathf.Rad2Deg;
        Vector3 radial = (Quaternion.AngleAxis(angleDeg, arcBinormal) * arcUp).normalized;
        Vector3 right = Vector3.Cross(arcBinormal, radial);
        if (right.sqrMagnitude < 0.01f) right = arcTangent;
        else right.Normalize();

        Vector3 onShell = arcCenter + radial * arcRadius + right * lateral;
        Vector3 from = onShell - arcCenter;
        if (from.sqrMagnitude < 0.0001f) from = arcUp;
        return arcCenter + from.normalized * (arcRadius + Mathf.Max(0.5f, altitude));
    }

    Vector3 Lift(Vector3 pos, float altitude)
    {
        Vector3 d = pos - arcCenter;
        if (d.sqrMagnitude < 0.0001f) d = arcUp;
        return arcCenter + d.normalized * (arcRadius + Mathf.Max(altitude, debrisAltitude * 0.5f));
    }

    void SpawnNearStart()
    {
        if (debrisPrefabs == null || debrisPrefabs.Length == 0) return;
        float[] dist = { minKickGap * 0.5f, minKickGap, minKickGap * 1.4f, maxKickGap };
        float[] side = { -pathWidth * 0.25f, pathWidth * 0.2f, -pathWidth * 0.15f, pathWidth * 0.3f };
        for (int i = 0; i < dist.Length; i++)
        {
            Vector3 pos = OnArc(dist[i], side[i], debrisAltitude + Random.Range(-4f, 8f));
            if (RegisterDebris(pos, minKickGap * 0.25f))
                SpawnRock(pos, false);
        }
    }

    void SpawnCorridor(int phase, float fromArc, float toArc, float spikeRate, float difficulty)
    {
        bool hasDebris = debrisPrefabs != null && debrisPrefabs.Length > 0;
        bool hasSpikes = spikePrefabs != null && spikePrefabs.Length > 0;
        if (!hasDebris && !hasSpikes) return;

        float length = Mathf.Abs(toArc - fromArc);
        if (length < 1f) return;

        float gap = Mathf.Lerp(minKickGap, maxKickGap, Mathf.Clamp01(difficulty * 0.85f));
        int stones = Mathf.Min(steppingStonesPerPhase, Mathf.Max(2, Mathf.CeilToInt(length / gap)));

        for (int i = 1; i <= stones; i++)
        {
            float t = i / (float)(stones + 1);
            float arc = Mathf.Lerp(fromArc, toArc, t);
            float lateral = Mathf.Sin(t * Mathf.PI * 2f + phase) * pathWidth * 0.25f;
            float alt = debrisAltitude + Mathf.Cos(t * Mathf.PI * 3f + phase) * 6f;
            Vector3 pos = Lift(OnArc(arc, lateral, alt), alt);
            if (!RegisterDebris(pos, minKickGap * 0.3f)) continue;
            bool spike = hasSpikes && Random.value < spikeRate * 0.55f;
            if (spike || hasDebris) SpawnRock(pos, spike);
        }

        for (int i = 0; i < sideDebrisPerPhase; i++)
        {
            float arc = Mathf.Lerp(fromArc, toArc, Random.Range(0.1f, 0.9f));
            float lateral = Random.Range(-pathWidth, pathWidth);
            float alt = debrisAltitude + Random.Range(-3f, 10f);
            Vector3 pos = Lift(OnArc(arc, lateral, alt), alt);
            if (!RegisterDebris(pos, minKickGap * 0.35f)) continue;
            bool spike = hasSpikes && Random.value < spikeRate;
            if (spike || hasDebris) SpawnRock(pos, spike);
        }
    }

    bool RegisterDebris(Vector3 pos, float minSep)
    {
        for (int i = 0; i < debrisPositions.Count; i++)
            if (Vector3.Distance(pos, debrisPositions[i]) < minSep) return false;
        debrisPositions.Add(pos);
        return true;
    }

    void SpawnRock(Vector3 position, bool isSpike)
    {
        position = Lift(position, debrisAltitude);

        GameObject prefab = PickPrefab(isSpike);
        if (prefab == null) return;

        GameObject rock = Instantiate(prefab, position, Random.rotation, transform);

        float scale = isSpike
            ? Random.Range(spikeMinScale, spikeMaxScale)
            : Random.Range(debrisMinScale, debrisMaxScale);
        rock.transform.localScale = Vector3.one * scale;
        rock.layer = debrisLayerIndex;
        rock.tag = isSpike ? "Spike" : "Debris";
        rock.name = isSpike ? $"Spike_{rock.GetInstanceID()}" : $"Debris_{rock.GetInstanceID()}";

        Rigidbody rb = rock.GetComponent<Rigidbody>();
        if (rb == null) rb = rock.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        AsteroidMotion motion = rock.GetComponent<AsteroidMotion>();
        if (motion == null) motion = rock.AddComponent<AsteroidMotion>();
        float speed = Mathf.Lerp(minDriftSpeed, maxDriftSpeed, Random.value);
        if (isSpike) speed *= spikeDriftMultiplier;
        float spin = Mathf.Lerp(minSpinSpeed, maxSpinSpeed, Random.value);
        Vector3 drift = Vector3.Slerp(Random.onUnitSphere, arcTangent, 0.2f).normalized;
        motion.Init(drift, speed, spin, isSpike, scale * scale * scale * 2.5f);

        AsteroidVisual visual = rock.GetComponent<AsteroidVisual>();
        if (visual == null) visual = rock.AddComponent<AsteroidVisual>();
        visual.Setup(isSpike);
    }

    GameObject PickPrefab(bool isSpike)
    {
        if (isSpike && spikePrefabs != null && spikePrefabs.Length > 0)
            return spikePrefabs[Random.Range(0, spikePrefabs.Length)];
        if (debrisPrefabs != null && debrisPrefabs.Length > 0)
            return debrisPrefabs[Random.Range(0, debrisPrefabs.Length)];
        if (spikePrefabs != null && spikePrefabs.Length > 0)
            return spikePrefabs[Random.Range(0, spikePrefabs.Length)];
        return null;
    }

    void SpawnFlavorPlanets(float startRadius)
    {
        if (nearbyPlanetPrefabs == null || nearbyPlanetPrefabs.Length == 0) return;

        float minR = startRadius + nearbyPlanetRadiusMax + 500f;
        float earthDist = Earth != null
            ? Vector3.Distance(arcCenter, Earth.position)
            : (arcRadius + earthDistance);
        float maxR = earthDist - earthRadiusCached - earthPlanetClearance - nearbyPlanetRadiusMax;
        if (maxR < minR + 100f)
            maxR = minR + Mathf.Max(400f, startingPlanetWorldRadius);

        int spawned = 0, tries = 0;
        while (spawned < numberOfPlanets && tries++ < 120)
        {
            Vector3 pos = Random.onUnitSphere * Random.Range(minR, maxR);
            float radius = Random.Range(nearbyPlanetRadiusMin, nearbyPlanetRadiusMax);
            if (!FarEnough(pos, radius)) continue;

            GameObject planet = Instantiate(
                nearbyPlanetPrefabs[Random.Range(0, nearbyPlanetPrefabs.Length)],
                pos, Random.rotation, transform);
            SetPlanetWorldRadius(planet, radius, planetMeshLocalRadius);
            SetAtmosphere(planet, planetAtmosphereHeight * Random.Range(0.7f, 1.1f));
            if (planet.CompareTag("Untagged")) planet.tag = "Planet";
            RegisterPlanet(pos, radius);
            spawned++;
        }
    }

    Transform PlaceEarth(Vector3 pos)
    {
        GameObject go;
        if (earthAnchor != null)
        {
            earthAnchor.position = pos;
            earthAnchor.tag = "Earth";
            go = earthAnchor.gameObject;
        }
        else if (earthPrefab != null)
        {
            go = Instantiate(earthPrefab, pos, Quaternion.identity, transform);
        }
        else
        {
            go = new GameObject("Earth");
            go.transform.SetParent(transform);
            go.transform.position = pos;
        }

        go.name = "Earth";
        go.tag = "Earth";
        go.transform.position = pos;

        // EarthGlobe prefab is scale 10000 — force a sane size
        ResizeEarth(go, earthWorldRadius);
        earthRadiusCached = GetPlanetRadius(go);
        if (earthRadiusCached < 1f) earthRadiusCached = earthWorldRadius;
        RegisterPlanet(pos, earthRadiusCached);
        return go.transform;
    }

    void ResizeEarth(GameObject earth, float worldRadius)
    {
        if (earth == null || worldRadius <= 0.01f) return;

        var mf = earth.GetComponentInChildren<MeshFilter>();
        float localR = 0.5f;
        Vector3 localCenter = Vector3.zero;
        if (mf != null && mf.sharedMesh != null)
        {
            Bounds b = mf.sharedMesh.bounds;
            localCenter = b.center;
            localR = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            if (mf.transform != earth.transform)
            {
                // Mesh on child — approximate from world bounds after reset
                earth.transform.localScale = Vector3.one;
                Physics.SyncTransforms();
                var rend = earth.GetComponentInChildren<Renderer>();
                if (rend != null)
                {
                    Bounds wb = rend.bounds;
                    localCenter = earth.transform.InverseTransformPoint(wb.center);
                    localR = Mathf.Max(wb.extents.x, wb.extents.y, wb.extents.z);
                }
            }
        }

        earth.transform.localScale = Vector3.one * (worldRadius / Mathf.Max(localR, 0.001f));
        Physics.SyncTransforms();
    }

    void RegisterPlanet(Vector3 pos, float radius)
    {
        planetPositions.Add(pos);
        planetRadii.Add(Mathf.Max(1f, radius));
    }

    void SetupProximity()
    {
        var prox = GetComponent<DebrisProximityColliders>();
        if (prox == null) prox = gameObject.AddComponent<DebrisProximityColliders>();
        prox.enableRadius = colliderEnableRadius;
        prox.disableRadius = colliderDisableRadius;
    }

   void PlacePlayer(GameObject planet, Vector3 standUp)
{
    GameObject player = GameObject.FindGameObjectWithTag("Player");
    if (player == null || planet == null) return;

    var move = player.GetComponent<CharacterMovement>();
    if (move != null)
    {
        // This will now trigger the initialized Kinematic Anchor
        move.SpawnOnSurface(planet, standUp, 0f); 
    }
}

    static void SetAtmosphere(GameObject planet, float height)
    {
        var g = planet.GetComponent<PlanetGravity>();
        if (g != null) g.atmosphereHeight = height;
    }
bool FarEnough(Vector3 pos, float radius)
{
    // 1. Check against all previously spawned planets
    for (int i = 0; i < planetPositions.Count; i++)
    {
        // Distance must be > (MyRadius + TheirRadius + SafetyGap)
        float safetyGap = 200f; 
        float minAllowedDist = planetRadii[i] + radius + safetyGap;
        
        if (Vector3.Distance(pos, planetPositions[i]) < minAllowedDist)
            return false;
    }

    // 2. Check against the Debris Arc centers (prevent flavor planets spawning on the path)
    if (PhaseCenters != null)
    {
        foreach (Vector3 phasePt in PhaseCenters)
        {
            if (Vector3.Distance(pos, phasePt) < (radius + 300f)) 
                return false;
        }
    }

    // 3. Check against Earth
    if (Earth != null)
    {
        float earthSafety = earthRadiusCached + radius + earthPlanetClearance;
        if (Vector3.Distance(pos, Earth.position) < earthSafety)
            return false;
    }

    return true;
}

    // --- Shared planet math (used by PlanetGravity / CharacterMovement) ---

    // Inside PlanetGenerator.cs
public static void SetPlanetWorldRadius(GameObject planet, float worldRadius, float meshLocalRadius = 1f)
{
    MeshFilter mf = planet.GetComponentInChildren<MeshFilter>();
    if (mf == null) return;
    
    float actualRadius = mf.sharedMesh.bounds.extents.y;
    planet.transform.localScale = Vector3.one * (worldRadius / actualRadius);

    MeshCollider mc = planet.GetComponent<MeshCollider>();
    if (mc == null) mc = planet.AddComponent<MeshCollider>();
    mc.convex = false;
}

    public static Vector3 GetPlanetCenter(GameObject planet)
    {
        // For a sphere, the transform.position is the most reliable center
        return planet.transform.position;
    }

    public static float GetPlanetRadius(GameObject planet)
    {
        // Get the MeshFilter from the object or its children
        MeshFilter mf = planet.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            // Calculate radius: Local Extents * World Scale
            float localRadius = mf.sharedMesh.bounds.extents.y;
            float worldScale = Mathf.Max(planet.transform.lossyScale.x,
                                        planet.transform.lossyScale.y,
                                        planet.transform.lossyScale.z);
            return localRadius * worldScale;
        }
        return planet.transform.lossyScale.y;
    }
}
