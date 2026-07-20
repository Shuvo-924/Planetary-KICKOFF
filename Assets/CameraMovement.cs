using UnityEngine;

public class CameraMovement : MonoBehaviour
{
    [Header("Targeting")]
    public Transform playerTf;
    public LayerMask collisionLayers;

    [Header("Orbit")]
    public float distance = 12f;
    public float mouseSensitivity = 3f;
    public float orbitDamping = 12f;
    public float lookAtOffset = 2f;

    [Header("Aiming / ADS")]
    public bool isAiming;
    public float aimDistance = 4f;
    public float aimFOV = 40f;
    public float transitionSpeed = 10f;
    public float aimSideOffset = 1.2f;

    private float yaw, pitch = 20f, currentDistance, defaultFOV;
    private Camera cam;

    void Start()
    {
        cam = GetComponentInChildren<Camera>();
        defaultFOV = cam.fieldOfView;
        currentDistance = distance;
        Cursor.lockState = CursorLockMode.Locked;
        yaw = playerTf.eulerAngles.y;
    }

    void LateUpdate()
    {
        if (playerTf == null) return;

        isAiming = Input.GetMouseButton(1);
        
        yaw += Input.GetAxis("Mouse X") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - Input.GetAxis("Mouse Y") * mouseSensitivity, -89f, 89f);

        float targetDist = isAiming ? aimDistance : distance;
        float targetFOV = isAiming ? aimFOV : defaultFOV;
        float targetSide = isAiming ? aimSideOffset : 0f;

        currentDistance = Mathf.Lerp(currentDistance, targetDist, Time.deltaTime * transitionSpeed);
        cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, targetFOV, Time.deltaTime * transitionSpeed);

        Quaternion finalRotation = Quaternion.FromToRotation(Vector3.up, playerTf.up) * Quaternion.Euler(pitch, yaw, 0);
        Vector3 focusPoint = playerTf.position + (playerTf.up * lookAtOffset);
        Vector3 rightOffset = finalRotation * Vector3.right * targetSide;
        Vector3 direction = finalRotation * Vector3.forward;
        Vector3 targetPos = focusPoint + rightOffset - (direction * currentDistance);

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * orbitDamping);
        transform.LookAt(isAiming ? focusPoint + direction * 50f : focusPoint, playerTf.up);
    }
}