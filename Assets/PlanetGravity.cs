using UnityEngine;

public class PlanetGravity : MonoBehaviour
{
    public float maxGravity = 15f;
    public float atmosphereHeight = 400f;
    private CharacterMovement playerMove;

    void Start()
    {
        GameObject p = GameObject.FindGameObjectWithTag("Player");
        if (p != null) playerMove = p.GetComponent<CharacterMovement>();
    }

    void FixedUpdate()
    {
        if (playerMove == null) return;

        Vector3 center = PlanetGenerator.GetPlanetCenter(gameObject);
        Vector3 toPlanet = center - playerMove.transform.position;
        float dist = toPlanet.magnitude;
        float radius = PlanetGenerator.GetPlanetRadius(gameObject);

        if (dist < radius + atmosphereHeight)
        {
            float fade = 1f - Mathf.Clamp01((dist - radius) / atmosphereHeight);
            float accel = maxGravity * (radius / Mathf.Max(dist, radius)) * fade;
            playerMove.NotifyGravityPull(toPlanet.normalized, accel);
        }
    }
}