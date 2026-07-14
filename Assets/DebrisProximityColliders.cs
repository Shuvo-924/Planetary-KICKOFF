using UnityEngine;

/// <summary>
/// Turns MeshColliders on only for debris near the player.
/// </summary>
public class DebrisProximityColliders : MonoBehaviour
{
    public static DebrisProximityColliders Instance { get; private set; }

    [Tooltip("Colliders enable inside this radius")]
    public float enableRadius = 140f;
    [Tooltip("Colliders disable outside this radius (hysteresis)")]
    public float disableRadius = 200f;

    Transform player;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        CachePlayer();
    }

    void CachePlayer()
    {
        GameObject go = GameObject.FindGameObjectWithTag("Player");
        if (go != null)
            player = go.transform;
    }

    void LateUpdate()
    {
        if (player == null)
        {
            CachePlayer();
            if (player == null) return;
        }

        Vector3 p = player.position;
        float enableSq = enableRadius * enableRadius;
        float disableSq = disableRadius * disableRadius;

        for (int i = 0; i < AsteroidMotion.All.Count; i++)
        {
            AsteroidMotion rock = AsteroidMotion.All[i];
            if (rock == null) continue;

            float sq = (rock.transform.position - p).sqrMagnitude;
            if (sq <= enableSq)
                rock.SetColliderActive(true);
            else if (sq >= disableSq)
                rock.SetColliderActive(false);
        }
    }
}
