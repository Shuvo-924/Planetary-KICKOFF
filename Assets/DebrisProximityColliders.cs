using UnityEngine;

/// <summary>
/// Enables asteroid MeshColliders only near the player (cheap hysteresis).
/// </summary>
public class DebrisProximityColliders : MonoBehaviour
{
    public static DebrisProximityColliders Instance { get; private set; }

    public float enableRadius = 160f;
    public float disableRadius = 220f;
    public float refreshInterval = 0.12f;

    Transform player;
    float nextRefresh;

    void Awake() => Instance = this;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start() => FindPlayer();

    void FindPlayer()
    {
        var go = GameObject.FindGameObjectWithTag("Player");
        if (go != null) player = go.transform;
    }

    void LateUpdate()
    {
        if (Time.time < nextRefresh) return;
        nextRefresh = Time.time + Mathf.Max(0.05f, refreshInterval);

        if (player == null)
        {
            FindPlayer();
            if (player == null) return;
        }

        Vector3 p = player.position;
        float enableSq = enableRadius * enableRadius;
        float disableSq = disableRadius * disableRadius;
        var all = AsteroidMotion.All;

        for (int i = 0; i < all.Count; i++)
        {
            AsteroidMotion rock = all[i];
            if (rock == null) continue;
            float sq = (rock.transform.position - p).sqrMagnitude;
            if (sq <= enableSq) rock.SetColliderActive(true);
            else if (sq >= disableSq) rock.SetColliderActive(false);
        }
    }
}
