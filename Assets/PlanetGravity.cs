using UnityEngine;

public class PlanetGravity : MonoBehaviour
{
    public float maxGravity = 9.8f;
    public float atmosphereHeight = 500f;

    [SerializeField] Rigidbody playerRb;
    CharacterMovement playerMove;

    void Awake()
    {
        CachePlayer();
    }

    void CachePlayer()
    {
        if (playerRb != null && playerMove != null) return;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        if (playerRb == null)
            playerRb = player.GetComponent<Rigidbody>();
        playerMove = player.GetComponent<CharacterMovement>();
    }

    public void getPlanets() { }

    void FixedUpdate()
    {
        if (playerRb == null)
        {
            CachePlayer();
            if (playerRb == null) return;
        }

        if (playerRb.isKinematic) return;
        if (playerMove != null && playerMove.InSpawnGrace) return;

        Vector3 planetCenter = PlanetGenerator.GetPlanetCenter(gameObject);
        float dist = Vector3.Distance(playerRb.position, planetCenter);
        float planetRadius = PlanetGenerator.GetPlanetRadius(gameObject);
        float gravityRange = planetRadius + atmosphereHeight;
        if (dist >= gravityRange) return;

        float gravityRatio = 1f - Mathf.Clamp01((dist - planetRadius) / Mathf.Max(atmosphereHeight, 0.01f));
        if (playerMove != null && playerMove.IsGrounded)
            gravityRatio = Mathf.Max(gravityRatio, 0.65f);

        float strength = maxGravity * gravityRatio;
        Vector3 gravityDir = (planetCenter - playerRb.position).normalized;
        playerRb.AddForce(gravityDir * strength, ForceMode.Acceleration);

        if (playerMove != null)
            playerMove.NotifyGravityPull(gravityDir, strength);
    }
}
