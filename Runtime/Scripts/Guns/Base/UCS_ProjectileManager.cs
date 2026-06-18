
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_ProjectileManager : UdonSharpBehaviour
{
    [Header("Projectile / Pool")]
    [SerializeField] public GameObject ProjectilePrefab;
    [SerializeField] public Transform ProjectilePoolParent;
    [Tooltip("Initial number of projectile instances to create in the pool")]
    [SerializeField] public int InitialPoolSize = 8;

    private GameObject[] ProjectilePool;

    private void Start()
    {
        if (ProjectilePrefab == null) return;
        int initial = Mathf.Max(1, InitialPoolSize);
        Transform parent = ProjectilePoolParent != null ? ProjectilePoolParent : this.transform;
        EnsurePoolInitialized(ref ProjectilePool, ProjectilePrefab, parent, initial);
    }

    private void EnsurePoolInitialized(ref GameObject[] pool, GameObject prefab, Transform parent, int initialSize)
    {
        if (prefab == null) return;
        if (pool != null && pool.Length >= initialSize) return;
        GameObject[] newPool = new GameObject[Mathf.Max(initialSize, (pool == null ? 0 : pool.Length))];
        int start = 0;
        if (pool != null)
        {
            for (int i = 0; i < pool.Length; i++) newPool[i] = pool[i];
            start = pool.Length;
        }
        for (int i = start; i < newPool.Length; i++)
        {
            GameObject go = (GameObject)Object.Instantiate(prefab);
            if (parent != null) go.transform.SetParent(parent, false);
            go.SetActive(false);
            newPool[i] = go;
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
        for (int i = 0; i < pool.Length; i++)
        {
            if (!pool[i].activeSelf)
            {
                pool[i].SetActive(true);
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
            if (parent != null) go.transform.SetParent(parent, false);
            go.SetActive(false);
            temp[i] = go;
        }
        pool = temp;
        pool[oldLen].SetActive(true);
        return pool[oldLen];
    }

    public GameObject AcquireProjectile()
    {
        Transform parent = ProjectilePoolParent != null ? ProjectilePoolParent : this.transform;
        return GetFromPool(ref ProjectilePool, ProjectilePrefab, parent);
    }

    public GameObject SpawnProjectile(Vector3 position, Vector3 direction, float speed)
    {
        GameObject proj = AcquireProjectile();
        if (proj == null) return null;
        proj.transform.position = position;
        proj.transform.rotation = Quaternion.LookRotation(direction);
        Rigidbody rb = proj.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.velocity = direction * speed;
        }
        return proj;
    }
}
