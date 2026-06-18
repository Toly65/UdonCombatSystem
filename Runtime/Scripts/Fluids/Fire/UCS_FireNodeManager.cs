
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_FireNodeManager : UdonSharpBehaviour
{
    [Header("Fire Node Setup")]
    public UCS_FireNode fireNodePrefab;
    public Transform fireNodeParent;
    [SerializeField, Min(1)] private int initialPoolSize = 4;

    private UCS_FireNode[] _fireNodes;

    private void Start()
    {
        EnsurePool();
    }

    public void AttachFireToPool(Transform followTarget)
    {
        if (followTarget == null || fireNodePrefab == null)
        {
            return;
        }

        EnsurePool();

        UCS_FireNode node = GetInactiveNode();
        if (node == null)
        {
            node = CreateNode();
        }

        if (node != null)
        {
            node.AttachToPool(followTarget);
        }
    }

    public void DetachFireFromPool(Transform followTarget)
    {
        if (_fireNodes == null || followTarget == null)
        {
            return;
        }

        for (int i = 0; i < _fireNodes.Length; i++)
        {
            UCS_FireNode node = _fireNodes[i];
            if (node != null && node.IsAttachedTo(followTarget))
            {
                node.Detach();
                return;
            }
        }
    }

    private void EnsurePool()
    {
        if (_fireNodes != null && _fireNodes.Length > 0)
        {
            return;
        }

        int count = Mathf.Max(1, initialPoolSize);
        _fireNodes = new UCS_FireNode[count];

        if (fireNodePrefab == null)
        {
            return;
        }

        Transform parent = fireNodeParent != null ? fireNodeParent : transform;

        for (int i = 0; i < count; i++)
        {
            _fireNodes[i] = CreateNode(parent);
        }
    }

    private UCS_FireNode GetInactiveNode()
    {
        if (_fireNodes == null)
        {
            return null;
        }

        for (int i = 0; i < _fireNodes.Length; i++)
        {
            UCS_FireNode node = _fireNodes[i];
            if (node != null && !node.gameObject.activeSelf)
            {
                return node;
            }
        }

        return null;
    }

    private UCS_FireNode CreateNode()
    {
        Transform parent = fireNodeParent != null ? fireNodeParent : transform;
        return CreateNode(parent);
    }

    private UCS_FireNode CreateNode(Transform parent)
    {
        if (fireNodePrefab == null)
        {
            return null;
        }

        GameObject fireObject = Instantiate(fireNodePrefab.gameObject);
        fireObject.transform.SetParent(parent, false);
        fireObject.SetActive(false);

        UCS_FireNode node = fireObject.GetComponent<UCS_FireNode>();
        if (node != null)
        {
            node.fireNodeManager = this;
        }

        if (_fireNodes != null)
        {
            int nextIndex = -1;
            for (int i = 0; i < _fireNodes.Length; i++)
            {
                if (_fireNodes[i] == null)
                {
                    nextIndex = i;
                    break;
                }
            }

            if (nextIndex >= 0)
            {
                _fireNodes[nextIndex] = node;
            }
        }

        return node;
    }
}
