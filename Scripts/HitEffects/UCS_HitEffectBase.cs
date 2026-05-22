
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_HitEffectBase : UdonSharpBehaviour
{
    [Header("Hit Effect Settings")]
    [Tooltip("Time in seconds before the hit effect is returned to the pool")]
    [SerializeField] public float ReturnToPoolTime = 2f;

    // reference to the pool this hit effect belongs to
    private UCS_HitEffectsPool hitEffectsPool;

    // internal timer to track when to return to pool
    private float returnTimer = 0f;
    private bool isActive = false;

    public void Initialize(UCS_HitEffectsPool pool)
    {
        hitEffectsPool = pool;
        returnTimer = 0f;
        isActive = true;
        gameObject.SetActive(true);
    }

    private void Update()
    {
        if (!isActive) return;

        returnTimer += Time.deltaTime;
        if (returnTimer >= ReturnToPoolTime)
        {
            ReturnToPool();
        }
    }

    private void ReturnToPool()
    {
        if (hitEffectsPool != null)
        {
            gameObject.SetActive(false);
            isActive = false;
            returnTimer = 0f;
            // The pool will handle deactivating and reusing this hit effect instance
        }
    }
}
