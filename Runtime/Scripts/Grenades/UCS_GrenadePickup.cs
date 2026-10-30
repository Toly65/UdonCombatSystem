using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Sits on the grenade's pickup (VRCPickup + Rigidbody + VRCObjectSync) and forwards to UCS_Grenade on the
// always-active parent. Kept stateless because this object is enabled/disabled by stowing and despawning.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[RequireComponent(typeof(VRC_Pickup))]
public class UCS_GrenadePickup : UdonSharpBehaviour
{
    [SerializeField] private UCS_Grenade grenade;
    private VRC_Pickup pickup;
    private UCS_PoolItemRules rules; // optional claimant-only pickup, found on the pool above this grenade
    private bool rejectedPickup;

    private void Start()
    {
        pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        rules = GetComponentInParent<UCS_PoolItemRules>();
        VRCPlayerApi local = Networking.LocalPlayer;
        // desktop holds until thrown, matching the guns
        if (local != null) pickup.AutoHold = local.IsUserInVR() ? VRC_Pickup.AutoHoldMode.No : VRC_Pickup.AutoHoldMode.Yes;
    }

    public override void OnPickup()
    {
        if (rules != null && !rules.AllowPickup(gameObject))
        {
            rejectedPickup = true;
            pickup.Drop();
            return;
        }
        grenade.OnPickedUp();
        // stops the free hand grabbing the pickup instead of the pin physbone
        pickup.pickupable = false;
    }

    public override void OnDrop()
    {
        if (rejectedPickup) { rejectedPickup = false; return; } // the grenade never saw this pickup
        pickup.pickupable = true;
        grenade.OnDropped();
    }

    public override void OnPickupUseDown()
    {
        grenade.OnUseDown();
    }

    private void OnCollisionEnter(Collision collision)
    {
        grenade.OnImpact();
    }
}
