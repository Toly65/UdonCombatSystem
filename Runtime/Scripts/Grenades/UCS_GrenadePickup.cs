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

    private void Start()
    {
        pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        VRCPlayerApi local = Networking.LocalPlayer;
        // desktop holds until thrown, matching the guns
        if (local != null) pickup.AutoHold = local.IsUserInVR() ? VRC_Pickup.AutoHoldMode.No : VRC_Pickup.AutoHoldMode.Yes;
    }

    public override void OnPickup()
    {
        grenade.OnPickedUp();
        // stops the free hand grabbing the pickup instead of the pin physbone
        pickup.pickupable = false;
    }

    public override void OnDrop()
    {
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
