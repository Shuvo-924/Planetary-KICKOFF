using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

[Serializable]
public class Engine
{
    public float idleRPM = 800f;
    public float maxRPM = 8000f;
    public float peakPowerRPM = 7000f;
    public float[] gearRatios = { 4f, 2.6f, 1.8f, 1.3f, 1.0f, 0.8f, 0.6f };
    public float finalDriveRatio = 4f;
    public float gearChangeTime = 0.12f;

    private int currentGear = 0;
    private float rpm = 0f;
    private bool switchingGears = false;
    private float shiftDropFactor = 1f;
    public void UpdateRPM(float averageWheelRPM)
    {
        if (switchingGears)
            shiftDropFactor = Mathf.MoveTowards(shiftDropFactor, 0.65f, Time.deltaTime * 5f);
        else
            shiftDropFactor = Mathf.MoveTowards(shiftDropFactor, 1.0f, Time.deltaTime * 5f);

        float totalRatio = gearRatios[currentGear] * finalDriveRatio;
        rpm = Mathf.Clamp(averageWheelRPM * totalRatio, idleRPM, maxRPM + 500f);
    }

    public float GetRPM() => rpm * shiftDropFactor;
    public float GetPowerMultiplier()
    {
        if (switchingGears) return 0f;

        // This creates a "Power Curve"
        // Power increases until peakPowerRPM, then levels off or drops slightly toward maxRPM
        if (rpm < peakPowerRPM)
        {
            return Mathf.Lerp(0.5f, 1.0f, (rpm - idleRPM) / (peakPowerRPM - idleRPM));
        }
        else
        {
            // Power stays at 1.0 or drops slightly (0.9) to encourage shifting
            return Mathf.Lerp(1.0f, 0.9f, (rpm - peakPowerRPM) / (maxRPM - peakPowerRPM));
        }
    }

    public void CheckGears(MonoBehaviour context, AudioSource fxSource, AudioClip shiftClip)
    {
        if (switchingGears) return;
        if (rpm > maxRPM * 0.95f && currentGear < gearRatios.Length - 1)
            context.StartCoroutine(Shift(1, fxSource, shiftClip));
        else if (rpm < idleRPM * 1.6f && currentGear > 0)
            context.StartCoroutine(Shift(-1, fxSource, shiftClip));
    }

    private IEnumerator Shift(int dir, AudioSource source, AudioClip clip)
    {
        switchingGears = true;
        yield return new WaitForSeconds(gearChangeTime);
        currentGear = Mathf.Clamp(currentGear + dir, 0, gearRatios.Length - 1);
        switchingGears = false;
    }

    public float GetTotalRatio() => gearRatios[currentGear] * finalDriveRatio;
}

[Serializable]
public class WheelProperties
{
    public Transform localPosition;
    public float turnAngle = 30f;
    public float mass= 10f;
    public float suspensionLength = 0.45f;
    public float size = 0.35f;
    public float angularVelocity = 0;
    public float engineTorque = 1000f;
    public float brakeStrength = 6000f;
    public bool isFrontWheel = false;
    public bool isLeftWheel = false;

    [HideInInspector] public GameObject wheelObject;
    [HideInInspector] public TrailRenderer skidTrail;
    [HideInInspector] public float normalForce, currentRotation;
    [HideInInspector] public Quaternion visualOffset;
    [HideInInspector] public bool isSliding;
}

public class NewCar : MonoBehaviour
{
    public Engine engine;
    public Rigidbody rb;
    public GameObject wheelPrefab;
    public GameObject skidMarkPrefab;
    public WheelProperties[] wheels;

    [Header("UI Speedometer")]
    public RectTransform needle;
    public float minSpeedAngle = -1.45f;
    public float maxSpeedAngle = -220f;
    public float maxSpeedOnDial = 220f;

    [Header("Audio Settings")]
    public AudioSource engineSound;
    public AudioSource skidAudioSource; // Loop this one
    public AudioClip gearShiftClip;
    public AudioClip crashClip;
    public float minPitch = 0.5f;
    public float maxPitch = 2.5f;

    [Header("GTA SA Handling Settings")]
    public float wheelGripX = 65f;
    public float wheelGripZ = 2f;
    public float steerSpeed = 150f;
    public float downforce = 0.15f;
    public float dragCoefficient = 0.28f;


    [Header("Visuals")]
    public MeshRenderer[] brakeLightRenderers;
    [ColorUsage(true, true)] public Color brakeColorOn = Color.red * 5f;
    [ColorUsage(true, true)] public Color brakeColorOff = Color.red * 1.5f;
    public GameObject[] Headlights;

    public Vector2 input;
    private bool footBrake = false;
    private bool handBrake = false;
    private bool forwards = true;
    private float currentSteerAngle = 0f;
    private Color currentBrakeColor;
    private float totalWheelRPM = 0;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        rb.centerOfMass += new Vector3(0, -0.1f, 0.2f);

        foreach (var w in wheels)
        {
            if (wheelPrefab)
            {
                w.wheelObject = Instantiate(wheelPrefab, transform);
                w.wheelObject.transform.localPosition = w.localPosition.localPosition;
                w.visualOffset = wheelPrefab.transform.localRotation;
                if (w.isLeftWheel) w.visualOffset *= Quaternion.Euler(0, 180, 0);
            }
            if (skidMarkPrefab)
            {
                GameObject skidObj = Instantiate(skidMarkPrefab, transform);
                w.skidTrail = skidObj.GetComponent<TrailRenderer>();
                w.skidTrail.emitting = false;
            }
        }

        if (engineSound)
        {
            engineSound.volume = 0;
            engineSound.Play();
        }
        if (skidAudioSource) { skidAudioSource.loop = true; skidAudioSource.volume = 0; }
    }

    void Update()
    {
        input.x = Input.GetAxis("Horizontal");
        input.y = Input.GetAxis("Vertical");
        Vector3 worldCOM = transform.TransformPoint(rb.centerOfMass);

        Debug.DrawLine(worldCOM,
                       worldCOM + transform.up * 5f,
                       Color.green);

        float speedKmh = rb.linearVelocity.magnitude * 3.6f;

        // Steering Logic (Exact from your code)
        float speedFactor = 1f / (1f + (speedKmh * 0.015f));
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, input.x * wheels[0].turnAngle * speedFactor, steerSpeed * Time.deltaTime);

        footBrake = (input.y < -0.1f && forwards);
        handBrake = Input.GetKey(KeyCode.Space) | Input.GetKey(KeyCode.LeftShift);

        HandleAudio();
        HandleSpeedometer();
        UpdateVisuals();

        //if (Input.GetKeyDown(KeyCode.R)) ResetCar();
    }

    void HandleAudio()
    {
        // Engine Sound
        float rpmPercent = (engine.GetRPM() - engine.idleRPM) / (engine.maxRPM - engine.idleRPM);
        engineSound.pitch = Mathf.Lerp(minPitch, maxPitch, rpmPercent);
        engineSound.volume = Mathf.Lerp(0.3f, 0.6f, rpmPercent) * Time.timeScale;

        // Skid Sound
        bool anySliding = false;
        foreach (var w in wheels) if (w.isSliding) anySliding = true;
        float targetSkid = (anySliding && rb.linearVelocity.magnitude > 3f) ? 0.5f : 0f;
        skidAudioSource.volume = Mathf.Lerp(skidAudioSource.volume, targetSkid, Time.deltaTime * 10f) * Time.timeScale;
        if (targetSkid > 0f && !skidAudioSource.isPlaying) skidAudioSource.Play();
        else if (targetSkid == 0 && skidAudioSource.isPlaying) skidAudioSource.Stop(); 
    }

    void HandleSpeedometer()
    {
        if (needle == null) return;

        // Use the actual velocity of the car body, not the wheels
        float trueKmh = rb.linearVelocity.magnitude * 3.6f * 0.5f;

        // Map the true speed to the needle angle
        float speedPercent = Mathf.Clamp01(trueKmh / maxSpeedOnDial);
        needle.localEulerAngles = new Vector3(0, 0, Mathf.Lerp(minSpeedAngle, maxSpeedAngle, speedPercent));
    }

    // void OnCollisionEnter(Collision col)
    // {
    //     AiCarController ai = col.gameObject.GetComponent<AiCarController>();
    //     if (ai != null) ai.DisableCar();
    //     if (col.relativeVelocity.magnitude > 5f && engineSound && crashClip)
    //         skidAudioSource.PlayOneShot(crashClip, Mathf.Clamp01(col.relativeVelocity.magnitude / 50f));
    // }

    void UpdateVisuals()
    {
        // Brake light fade (Exact from your code)
        bool lightsTrigger = footBrake || handBrake;
        currentBrakeColor = Color.Lerp(currentBrakeColor, lightsTrigger ? brakeColorOn : brakeColorOff, Time.deltaTime * (lightsTrigger ? 20f : 8f));

        foreach (var r in brakeLightRenderers)
        {
            r.material.SetColor("_EmissionColor", currentBrakeColor);
            if (currentBrakeColor.maxColorComponent > 0.5f) r.material.EnableKeyword("_EMISSION");
            else r.material.DisableKeyword("_EMISSION");
        }
        //TurnLights();
    }

    // public void TurnLights()
    // {
    //     foreach (var h in Headlights) h.SetActive(LightsManager.lightsOn);
    // }

    void FixedUpdate()
    {
        float speed = rb.linearVelocity.magnitude;
        forwards = transform.InverseTransformDirection(rb.linearVelocity).z > -0.05f;

        // Aerodynamics 
        rb.AddForce(-rb.linearVelocity.normalized * speed * speed * dragCoefficient * 0.5f);
        rb.AddForce(-transform.up * speed * downforce * 100f);

        float currentAverageWheelRPM = 0;

        foreach (var w in wheels)
        {
            RaycastHit hit;
            // 1. Start ray 0.2 units above the helper to prevent it getting "swallowed" during nose-dives
            Vector3 rayOrigin = transform.TransformPoint(w.localPosition.localPosition + Vector3.up * 0.1f);
            float totalRayLen = 0.1f + w.suspensionLength + w.size;
            

            if (Physics.Raycast(rayOrigin, -transform.up, out hit, totalRayLen))
            {
                // --- 2. SUSPENSION (Spring + Damper) ---
                float compression = Mathf.Clamp(totalRayLen - hit.distance, 0f, w.suspensionLength);
                float damperForce = -Vector3.Dot(rb.GetPointVelocity(hit.point), transform.up) * 7000f; // Adjusted damper
                float t = compression / w.suspensionLength;
                float stiffness = Mathf.Lerp(35000f, 1200000f, t * t); // ramps up near full compression
                w.normalForce = Mathf.Max(0f, (compression * stiffness) + damperForce);
                rb.AddForceAtPosition(hit.normal * w.normalForce, hit.point);

                float steer = w.isFrontWheel ? currentSteerAngle : 0;
                Vector3 wForward = Quaternion.AngleAxis(steer, transform.up) * transform.forward;
                Vector3 wRight = Quaternion.AngleAxis(steer, transform.up) * transform.right;
                Vector3 vel = rb.GetPointVelocity(hit.point);
                float fwdVel = Vector3.Dot(vel, wForward);
                float sideVel = Vector3.Dot(vel, wRight);

                // Power/Brake (Integrated new Power Multiplier)
                float torque = 0;
                float brakeForce = 0;

                // Determine if THIS specific wheel should be braking
                bool shouldBrake = footBrake || (handBrake && !w.isFrontWheel);
                float slopeAngle = Vector3.Angle(hit.normal, Vector3.up);

                if (shouldBrake)
                {
                    float strength = handBrake && !w.isFrontWheel ? w.brakeStrength * 1.5f : 0;
                    if (footBrake) strength = w.brakeStrength;
                    brakeForce = strength * Mathf.Sign(fwdVel);
                    torque = 0;
                }
                else if (Mathf.Abs(input.y) > 0.05f)
                {
                    torque = input.y * w.engineTorque * engine.GetTotalRatio() * engine.GetPowerMultiplier();
                }

                if (speed < 0.05f && slopeAngle <= 45f && torque == 0)
                {
                    rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.fixedDeltaTime * 10f);
                    rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, Time.fixedDeltaTime * 10f);
                }

                rb.AddForceAtPosition(wForward * (torque - brakeForce - (fwdVel * wheelGripZ)), hit.point);
                rb.AddForceAtPosition(-wRight * (sideVel * wheelGripX * (w.normalForce / 100f)), hit.point);

                // Visual placement & Sliding state
                w.isSliding = Mathf.Abs(sideVel) > 3.5f || (shouldBrake && speed > 5f);
                w.wheelObject.transform.position = hit.point + transform.up * w.size;

                // FIXED WHEEL ROTATION: Combined Steering, Model Offset, and Rolling axis
                w.currentRotation += (fwdVel / w.size) * Time.fixedDeltaTime * Mathf.Rad2Deg;
                Quaternion steerRot = Quaternion.Euler(0, steer, 0);
                Quaternion rollRot = Quaternion.Euler(0, 0, w.currentRotation);
                w.wheelObject.transform.localRotation = steerRot * w.visualOffset * rollRot;

                totalWheelRPM = Mathf.Abs((fwdVel / (2 * Mathf.PI * w.size)) * 60f);
                HandleSkidMarks(w, sideVel, hit);
            }
            else
            {
                w.isSliding = false;
                w.wheelObject.transform.localPosition = w.localPosition.localPosition - Vector3.up * w.suspensionLength;
            }
        }

        engine.UpdateRPM(currentAverageWheelRPM / wheels.Length);
        engine.CheckGears(this, engineSound, gearShiftClip);
    }

    void HandleSkidMarks(WheelProperties w, float sideVel, RaycastHit hit)
    {
        if (w.isSliding && skidMarkPrefab)
        {
            w.skidTrail.transform.position = hit.point + (hit.normal * 0.02f);

            // 3. Rotate the emitter to match the ground surface normal
            w.skidTrail.transform.rotation = Quaternion.LookRotation(transform.forward, hit.normal);

            w.skidTrail.emitting = true;
        }
        else if (w.skidTrail)
        {
            w.skidTrail.emitting = false;
        }
    }

    // private void OnTriggerEnter(Collider other)
    // {
    //     if (other.name.Contains("Trigger"))
    //     {
    //         string[] s = other.name.Split(" ");
    //         int idx = int.Parse(s[s.Length - 1]);
    //         UnityEngine.Object.FindAnyObjectByType<ChaseManager>().chasePath.RemoveAt(idx);
    //         distanceTravelled++;
    //         other.gameObject.SetActive(false);
    //         Destroy(other.gameObject, 0.5f);
    //         Debug.Log($"Destroyed Collider {idx}");
    //     }
    // }

    void ResetCar()
    {
        transform.position += Vector3.up * 2;
        transform.rotation = Quaternion.LookRotation(transform.forward);
        rb.linearVelocity = rb.angularVelocity = Vector3.zero;
    }
}