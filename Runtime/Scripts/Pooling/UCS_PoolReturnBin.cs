using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Trigger bin: a pickup dropped in here goes back to whichever listed BulkPreOrderPool it belongs to.
// Only the client that owns the pickup acts, so the pool sees one return per item.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[RequireComponent(typeof(BoxCollider))]
public class UCS_PoolReturnBin : UdonSharpBehaviour
{
    [SerializeField] private BulkPreOrderPool[] pools;

    private void OnTriggerEnter(Collider other)
    {
        VRC_Pickup pickup = other.GetComponent<VRC_Pickup>();
        if (!Utilities.IsValid(pickup)) pickup = other.GetComponentInParent<VRC_Pickup>();
        if (!Utilities.IsValid(pickup) || !Networking.IsOwner(pickup.gameObject)) return;

        // The managed object is the gun/grenade root, an ancestor of the pickup (or the pickup itself).
        Transform t = pickup.transform;
        for (int depth = 0; t != null && depth < 4; depth++)
        {
            for (int p = 0; p < pools.Length; p++)
            {
                int slot = pools[p].GetSlotIndexOf(t.gameObject);
                if (slot < 0) continue;

                if (pickup.IsHeld) pickup.Drop();
                // a stowed item stays locked to its holder's stow point and would be dragged back out
                StowableManager stow = t.GetComponent<StowableManager>();
                if (stow != null) stow.ForceUnstowEverywhere();
                pools[p]._ReturnObject(pools[p].GetClaimantAt(slot), slot);
                return;
            }
            t = t.parent;
        }
    }
}
