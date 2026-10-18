
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_HitEffectsPool : UdonSharpBehaviour
{
    [Header("Prefab / Parent")]
    [SerializeField] public GameObject Prefab;
    [SerializeField] public Transform PoolParent;

    [Header("Pool Settings")]
    [Tooltip("Initial number of instances to create for the pool")]
    [SerializeField] public int InitialPoolSize = 4;

    // object pool
    private GameObject[] Pool;

    private void Start()
    {
        if (Prefab == null) return;

        int initial = Mathf.Max(1, InitialPoolSize);
        Transform parent = PoolParent != null ? PoolParent : this.transform;

        EnsurePoolInitialized(ref Pool, Prefab, parent, initial);
    }

    private void EnsurePoolInitialized(ref GameObject[] pool, GameObject prefab, Transform parent, int initialSize)
    {
        if (prefab == null) return;
        if (pool != null && pool.Length >= initialSize) return;
        GameObject[] newPool = new GameObject[Mathf.Max(initialSize, (pool == null ? 0 : pool.Length))];
        int start = 0;
        if (pool != null)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                newPool[i] = pool[i];
            }
            start = pool.Length;
        }
        for (int i = start; i < newPool.Length; i++)
        {
            GameObject go = (GameObject)Object.Instantiate(prefab);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            newPool[i] = go;
            go.SetActive(false);
        }
        pool = newPool;
    }

    private GameObject GetFromPool(ref GameObject[] pool, GameObject prefab, Transform parent)
    {
        if (prefab == null) return null;
        if (pool == null || pool.Length == 0)
        {
            EnsurePoolInitialized(ref pool, prefab, parent, InitialPoolSize);
        }
        // returns the instance still inactive so callers can position it before OnEnable / particle play-on-awake
        for (int i = 0; i < pool.Length; i++)
        {
            if (!pool[i].activeSelf)
            {
                return pool[i];
            }
        }
        int oldLen = pool.Length;
        int newLen = Mathf.Max(1, oldLen * 2);
        GameObject[] temp = new GameObject[newLen];
        for (int i = 0; i < oldLen; i++) temp[i] = pool[i];
        for (int i = oldLen; i < newLen; i++)
        {
            GameObject go = (GameObject)Object.Instantiate(prefab);
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            temp[i] = go;
            go.SetActive(false);
        }
        pool = temp;
        return pool[oldLen];
    }

    public GameObject AcquireInstance()
    {
        Transform parent = PoolParent != null ? PoolParent : this.transform;
        GameObject go = GetFromPool(ref Pool, Prefab, parent);
        if (go != null)
        {
            go.SetActive(true);
            UCS_HitEffectBase effect = go.GetComponent<UCS_HitEffectBase>();
            if (effect != null) effect.Initialize(this);
        }
        return go;
    }

    // Preferred entry point for guns, projectiles and grenades: positions the instance before enabling it and
    // passes blame along, so a pool of explosions can be dropped in anywhere a pool of sparks is used.
    public GameObject SpawnAt(Vector3 position, Quaternion rotation, int blamedPlayerID, int attackerTeamID)
    {
        Transform parent = PoolParent != null ? PoolParent : this.transform;
        GameObject go = GetFromPool(ref Pool, Prefab, parent);
        if (go == null) return null;
        go.transform.position = position;
        go.transform.rotation = rotation;
        go.SetActive(true);
        UCS_HitEffectBase effect = go.GetComponent<UCS_HitEffectBase>();
        if (effect != null)
        {
            effect.Initialize(this);
            effect.OnSpawned(blamedPlayerID, attackerTeamID);
        }
        return go;
    }
}
