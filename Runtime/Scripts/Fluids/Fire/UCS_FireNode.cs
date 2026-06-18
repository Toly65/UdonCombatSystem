
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_FireNode : UdonSharpBehaviour
{
    public UCS_FireNodeManager fireNodeManager;
    private Transform attachedTarget;
    public Transform visualRoot;

    private void Start()
    {
        RefreshVisuals();
    }

    public void AttachToPool(Transform followTarget)
    {
        attachedTarget = followTarget;
        ApplyFollowTarget();
        gameObject.SetActive(true);
        RefreshVisuals();
    }

    public void Detach()
    {
        attachedTarget = null;
        gameObject.SetActive(false);
        RefreshVisuals();
    }

    public bool IsAttachedTo(Transform followTarget)
    {
        return attachedTarget == followTarget;
    }

    private void Update()
    {
        if (attachedTarget == null)
        {
            return;
        }

        ApplyFollowTarget();

        transform.rotation = Quaternion.identity;
    }

    private void RefreshVisuals()
    {
        if (visualRoot != null)
        {
            visualRoot.gameObject.SetActive(gameObject.activeSelf);
        }
    }

    private void ApplyFollowTarget()
    {
        if (attachedTarget == null)
        {
            return;
        }

        transform.position = attachedTarget.position;
        transform.rotation = attachedTarget.rotation;
        transform.localScale = attachedTarget.lossyScale;
    }
}
