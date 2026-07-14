using System.Collections;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering.Universal;

public class CameraMovement : MonoBehaviour
{
    [Header("Targets")]
    public Transform carTransform;
    public LayerMask collisionLayers; // IMPORTANT: Set this to everything EXCEPT the car layer

    [Header("Offset Settings")]
    public float distance = 6.0f;
    public float height = 2.0f;
    public float lookAtHeight = 1.0f;

    [Header("Mouse Control")]
    public float mouseSensitivity = 3f;
    public float minVerticalAngle = -10f;
    public float maxVerticalAngle = 60f;

    [Header("Smoothness")]
    public float rotationSmoothTime = 0.12f; // Smooths the "lagging" rotation
    public float positionSmoothTime = 0.5f; // Smooths the follow distance

    private float mouseX;
    private float mouseY;
    private Vector3 posVelocity = Vector3.zero;
    private float rotVelocity = 0f;
    private float currentRotationAngle;
    private Vector3 previousShakeOffset;
    public UniversalRendererData urp;
    private NewCar playerCar;
    void Awake()
    {
        foreach (var feat in urp.rendererFeatures)
        {
            if (feat.name.Contains("FullScreenPass"))
            {
                feat.SetActive(false);
                break;
            }
        }
        Cursor.lockState = CursorLockMode.None;
        playerCar = Object.FindAnyObjectByType<NewCar>();
    }

    // Add this inside the CameraMovement class
    private void OnEnable()
    {
        // Sync the mouse input variables with the current rotation 
        // so the camera doesn't snap back to 0 when control starts
        Vector3 euler = transform.eulerAngles;
        mouseX = 0; // We keep mouseX as the offset from car rotation
        mouseY = 0;
        currentRotationAngle = euler.y;
        Vector3 targetPosition = carTransform.position - (Vector3.forward * distance) + (Vector3.up * height);
        transform.position = Vector3.SmoothDamp(transform.position, targetPosition, ref posVelocity, positionSmoothTime);
        transform.LookAt(carTransform.position + Vector3.up * lookAtHeight);
    }

    [Header("Acceleration Effect")]
    public float accelerationExtraDistance = 3.0f; // How much further to move back
    public float accelerationLerpSpeed = 2.0f;     // How fast the camera zooms out
    private float currentExtraDistance = 0f;       // Internal tracker

    void LateUpdate()
    {
        if (!carTransform) return;

        // 1. Capture Mouse Input (Same as before)
        if (Input.GetMouseButton(2))
        {
            mouseX += Input.GetAxis("Mouse X") * mouseSensitivity;
            mouseY -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            mouseY = Mathf.Clamp(mouseY, minVerticalAngle, maxVerticalAngle);
        }

        // 2. Smoothly calculate the Horizontal Rotation (Same as before)
        float targetRotationAngle = carTransform.eulerAngles.y + mouseX;
        currentRotationAngle = Mathf.SmoothDampAngle(currentRotationAngle, targetRotationAngle, ref rotVelocity, rotationSmoothTime);
        Quaternion rotation = Quaternion.Euler(mouseY, currentRotationAngle, 0);

        // 3. DYNAMIC DISTANCE LOGIC
        // Determine if we should be backed away or at default distance
        float targetExtra =  0f;
        // Smoothly lerp the extra distance value
        currentExtraDistance = Mathf.Lerp(currentExtraDistance, targetExtra, Time.deltaTime * accelerationLerpSpeed);

        float totalDistance = distance + currentExtraDistance;

        // 4. Calculate Target Position
        // We use the smoothed totalDistance here
        Vector3 targetPosition = carTransform.position - (rotation * Vector3.forward * totalDistance) + (Vector3.up * height);

        // 5. TERRAIN COLLISION
        Vector3 rayStart = carTransform.position + Vector3.up * lookAtHeight;
        Vector3 rayDirection = (targetPosition - rayStart).normalized;
        float rayDistance = Vector3.Distance(rayStart, targetPosition);

        RaycastHit hit;
        if (Physics.Raycast(rayStart, rayDirection, out hit, rayDistance, collisionLayers))
        {
            // Move the target position to the hit point (with a small offset)
            targetPosition = hit.point + hit.normal * 0.2f;
        }

        // 6. Final Smoothing (strip last shake so SmoothDamp doesn't fight trauma)
        Vector3 shakeOffset = CameraShake.Instance != null ? CameraShake.Instance.Offset : Vector3.zero;
        Vector3 unshakenPos = transform.position - previousShakeOffset;
        Vector3 smoothPos = Vector3.SmoothDamp(unshakenPos, targetPosition, ref posVelocity, positionSmoothTime);
        transform.position = smoothPos + shakeOffset;
        previousShakeOffset = shakeOffset;

        // 7. Look At the stickman, then layer rotational shake
        transform.LookAt(carTransform.position + Vector3.up * lookAtHeight);
        if (CameraShake.Instance != null)
            transform.rotation *= Quaternion.Euler(CameraShake.Instance.EulerOffset);
    }
}
