using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[DefaultExecutionOrder(100)]
public class CharacterMovement : MonoBehaviour
{
    private Vector3 moveInput;
    private Vector3 velocity; // Internal velocity for space flight
    public Rigidbody rb; // Set to Kinematic
    public Animator anim;
    public Transform cam;
    public Transform feet;

    [Header("Kick Settings")]
    public float kickForce = 95f;
    public float minKickForce = 35f;
    public float maxKickForce = 140f;
    public float energy = 100f;
    public float kickEnergyCost = 10f;
    public float kickAirLock = 0.2f;
    public float clickAimMaxDistance = 400f; // Fixed missing variable

    [Header("Kick Animation")]
    public int kickOffFrame = 30;
    public string kickStateName = "KickTrigger";
    public float aimHoldSeconds = 0.55f;

    [Header("Movement")]
    public float walkSpeed = 7f;
    public string walkingBoolName = "IsWalking";
    public float uprightRotateSpeed = 25f;

    [Header("Kinematic Stick Settings")]
    public float standHeight = 1.1f;
    public float surfacePad = 0.02f;
    private Transform currentGroundTf;
    private Quaternion relativeRot;
    private float aimHoldTimer;
    private float airLockTimer;

    [Header("Sensing")]
    public LayerMask planetLayer;
    public LayerMask hazardLayer;
    public float bodyProbeRadius = 0.4f;

    [Header("Jets")]
    public int jetCharges = 3;
    public float jetImpulse = 20f;
    public float jetEnergyCost = 5f;

    private bool isGrounded, hasSpawned, kickPending;
    private Vector3 groundNormal = Vector3.up;
    private Vector3 gravityDir = Vector3.down;
    private KickAimUI aimUI;
    private CameraMovement camStand;
    private AsteroidMotion hovered; // Fixed missing variable

    public bool IsGrounded => isGrounded;
    private Vector3 lastPosition; // For predictive landing checks
    private Transform lastRockKicked;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponentInChildren<Animator>();
        camStand = Object.FindAnyObjectByType<CameraMovement>();
        
        if (rb != null) rb.isKinematic = true; 
    }

    void Start()
    {
        aimUI = KickAimUI.EnsureExists();
        aimUI.Bind(this);
    }

    void Update()
    {
        UpdateHover();

        if (isGrounded && !kickPending)
        {
            moveInput = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            bool walking = moveInput.sqrMagnitude > 0.01f;
            if (anim != null) anim.SetBool(walkingBoolName, walking);
        }
        else
        {
            moveInput = Vector3.zero;
            if (anim != null) anim.SetBool(walkingBoolName, false);
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool isAiming = (camStand != null && camStand.isAiming);
        
        if (!overUI && !kickPending && isGrounded && Input.GetMouseButtonDown(0) && energy >= kickEnergyCost && isAiming)
            BeginKick();

        if (!isGrounded && !kickPending && Input.GetKeyDown(KeyCode.Space))
            Jet();
    }

    void FixedUpdate()
{
    if (!hasSpawned) return;

    if (airLockTimer > 0) airLockTimer -= Time.fixedDeltaTime;

    if (!isGrounded && airLockTimer <= 0)
    {
        CatchNearestAsteroid();
    }

    if (isGrounded) HandleGroundedState();
    else HandleFlightState();

    // RECORD POSITION FOR NEXT FRAME TRAJECTORY CHECK
    lastPosition = transform.position;

    if (camStand != null && aimUI != null) aimUI.ToggleCrosshair(camStand.isAiming);
}

   void CatchNearestAsteroid()
{
    Vector3 frameStart = lastPosition;
    Vector3 frameEnd = transform.position;
    
    // Safety check for first frame
    if (frameStart == Vector3.zero) return;

    Vector3 movementDir = (frameEnd - frameStart).normalized;
    float movementDist = Vector3.Distance(frameStart, frameEnd);

    foreach (var rock in AsteroidMotion.All)
    {
        // Ignore the rock we just left
        if (rock.transform == lastRockKicked) continue;

        // 1. Math to find the closest point on our flight path to the rock
        Vector3 playerToRock = rock.transform.position - frameStart;
        float projection = Vector3.Dot(playerToRock, movementDir);
        float closestPointOnLine = Mathf.Clamp(projection, 0, movementDist);
        Vector3 closestPoint = frameStart + (movementDir * closestPointOnLine);

        // 2. Check distance
        float distToRockCenter = Vector3.Distance(closestPoint, rock.transform.position);
        float catchThreshold = rock.LandRadius + bodyProbeRadius + 1.0f;

        if (distToRockCenter < catchThreshold)
        {
            // --- THE FIX: Declare and get the point/normal from the rock ---
            Vector3 pt;
            Vector3 nrm;
            float dummyDist;
            
            // Ask the rock where its surface is relative to our path
            rock.TryGetLanding(closestPoint, out pt, out nrm, out dummyDist);

            // Now we can call LandingLockManual with the correct data
            LandingLockManual(pt, nrm, rock.transform);
            
            lastRockKicked = null; 
            return;
        }
    }

    PredictiveLanding(); 
}

    void HandleGroundedState()
    {
        if (currentGroundTf == null) return;

        Vector3 camFwd = Vector3.ProjectOnPlane(cam.forward, groundNormal).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(cam.right, groundNormal).normalized;
        Vector3 walkDir = (camFwd * moveInput.z + camRight * moveInput.x).normalized;

        // Project the intended walk direction onto the surface normal
        Vector3 parallelWalkDir = Vector3.ProjectOnPlane(walkDir, groundNormal).normalized;
        Vector3 worldAnchorPos = currentGroundTf.TransformPoint(transform.position);
        Quaternion worldAnchorRot = relativeRot;

        Vector3 walkDelta = walkDir * walkSpeed * Time.fixedDeltaTime;
        

        bool isWalkingNow = moveInput.sqrMagnitude > 0.01f;
        if (isWalkingNow && walkDir.sqrMagnitude > 0.001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(walkDir, groundNormal);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, uprightRotateSpeed * Time.fixedDeltaTime);
            transform.position = Vector3.Slerp(transform.position, transform.position + parallelWalkDir, walkSpeed * Time.fixedDeltaTime);
        }
        else
        {
            transform.rotation = Quaternion.Slerp(transform.rotation, worldAnchorRot, uprightRotateSpeed * Time.fixedDeltaTime);
        }
        RaycastHit hit;
        if (Physics.Raycast(feet.position, (currentGroundTf.position - feet.position).normalized, out hit, 10f))
        {
            relativeRot = Quaternion.Euler(hit.normal);
            groundNormal = hit.normal;
        }
       
    }

    void HandleFlightState()
    {

        if (aimHoldTimer > 0)
        {
            aimHoldTimer -= Time.fixedDeltaTime;
        }
        else
        {
            Quaternion target = Quaternion.FromToRotation(transform.up, -gravityDir) * transform.rotation;
            transform.rotation = Quaternion.Slerp(transform.rotation, target, Time.fixedDeltaTime);
        }
    }

    void BeginKick()
    {
        kickPending = true;
        energy -= kickEnergyCost;
        if (anim != null) anim.SetTrigger(kickStateName);
        StartCoroutine(KickWait());
    }

    IEnumerator KickWait()
    {
        yield return new WaitForSeconds(kickOffFrame / 30f);
        ExecuteLaunch();
        kickPending = false;
    }

    void ExecuteLaunch()
    {
        lastRockKicked = currentGroundTf; 

        Vector3 launchDir = cam.forward;
        isGrounded = false;
        currentGroundTf = null;

        transform.up = launchDir;
        aimHoldTimer = aimHoldSeconds;

        float force = (aimUI != null) ? aimUI.SelectedForce : kickForce;
        velocity = launchDir * force;

        airLockTimer = kickAirLock;

        if (CameraShake.Instance != null) CameraShake.Instance.ShakeFromKick(force, force);
    }

    // Fixed missing Jet function
    void Jet()
    {
        if (jetCharges <= 0 || energy < jetEnergyCost) return;
        
        Vector3 jetDir = cam.forward;
        velocity += jetDir * jetImpulse;
        
        jetCharges--;
        energy -= jetEnergyCost;
        
        airLockTimer = 0.3f; // Brief protection so we don't re-stick
        isGrounded = false;
        if (CameraShake.Instance != null) CameraShake.Instance.AddTrauma(0.2f);
    }

    void PredictiveLanding()
    {
        if (velocity.sqrMagnitude < 0.1f) return;

        if (Physics.SphereCast(transform.position, bodyProbeRadius, velocity.normalized, out RaycastHit hit, (velocity.magnitude * Time.fixedDeltaTime) + 0.5f, hazardLayer | planetLayer))
        {
            LandingLock(hit);
        }
    }

    void LandingLock(RaycastHit hit)
    {
        isGrounded = true;
        airLockTimer = 0;
        velocity = Vector3.zero;
        currentGroundTf = hit.transform;

        transform.up = hit.normal;
        groundNormal = hit.normal;
        
        Vector3 targetPos = hit.point + hit.normal * standHeight;
        transform.position = targetPos;

        relativeRot = Quaternion.Inverse(currentGroundTf.rotation) * transform.rotation;

        if (anim != null) anim.SetTrigger("Land");
    }

    void LandingLockManual(Vector3 pt, Vector3 nrm, Transform tf)
{
    isGrounded = true;
    airLockTimer = 0;
    velocity = Vector3.zero;
    currentGroundTf = tf;

    // 1. INSTANT ORIENTATION
    transform.up = nrm;
    groundNormal = nrm;

    // 2. POSITIONING
    transform.position = pt + nrm * standHeight;

    // 3. ANCHORING
    relativeRot = Quaternion.Inverse(currentGroundTf.rotation) * transform.rotation;

    if (anim != null) anim.SetTrigger("Land");
    Debug.Log("Proximity Catch on: " + tf.name);
}

    public void SpawnOnSurface(GameObject planet, Vector3 outward, float clearance = 0f)
    {
        currentGroundTf = planet.transform;
        Vector3 center = PlanetGenerator.GetPlanetCenter(planet);
        float radius = PlanetGenerator.GetPlanetRadius(planet);
        Vector3 normal = outward.normalized;
        Vector3 spawnPos = center + normal * (radius + standHeight + surfacePad);
        
        transform.position = spawnPos;
        transform.up = normal;
        groundNormal = normal;

        relativeRot = Quaternion.Inverse(currentGroundTf.rotation) * transform.rotation;

        isGrounded = true;
        hasSpawned = true;
    }

    public void NotifyGravityPull(Vector3 dir, float accel)
    {
        gravityDir = dir;
        if (!isGrounded) velocity += dir * accel * Time.fixedDeltaTime;
    }

    void UpdateHover() {
        if (kickPending) return;
        Ray ray = new Ray(cam.position, cam.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, clickAimMaxDistance, hazardLayer)) {
            AsteroidMotion rock = hit.collider.GetComponent<AsteroidMotion>();
            if (hovered != rock) {
                if (hovered != null) hovered.GetComponent<AsteroidVisual>()?.SetHighlighted(false);
                hovered = rock;
                if (hovered != null) hovered.GetComponent<AsteroidVisual>()?.SetHighlighted(true);
            }
        } else {
            if (hovered != null) hovered.GetComponent<AsteroidVisual>()?.SetHighlighted(false);
            hovered = null;
        }
    }
}