using UnityEngine;

public class PlanetGravity : MonoBehaviour
{
    public float maxGravity = 9.8f; // Strength at the surface (positive = pull toward planet)
    public float atmosphereHeight = 500f; // Distance where gravity becomes 0

    [SerializeField] Rigidbody playerRb;

    private void Awake()
    {
        if (playerRb == null)
        {
            // Stickman uses Rigidbody + CharacterMovement, not CharacterController
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
                playerRb = player.GetComponent<Rigidbody>();
        }
    }

    // Called by PlanetGenerator after all planets are spawned
    public void getPlanets() { }

    void FixedUpdate()
    {
        if (playerRb == null) return;

        // Skip while stuck/kinematic — placement or landing handles that pose
        if (playerRb.isKinematic) return;

        float dist = Vector3.Distance(playerRb.position, transform.position);
        float planetRadius = PlanetGenerator.GetPlanetRadius(gameObject);
        float gravityRange = planetRadius + atmosphereHeight;

        if (dist < gravityRange)
        {
            float gravityRatio = 1f - Mathf.Clamp01((dist - planetRadius) / Mathf.Max(atmosphereHeight, 0.01f));
            Vector3 gravityDir = (transform.position - playerRb.position).normalized;
            playerRb.AddForce(gravityDir * maxGravity * gravityRatio, ForceMode.Acceleration);

            Transform playerT = playerRb.transform;
            Quaternion targetRotation = Quaternion.FromToRotation(-playerT.up, gravityDir) * playerT.rotation;
            playerT.rotation = Quaternion.Slerp(playerT.rotation, targetRotation, Time.deltaTime * 2f);
        }
    }
}
