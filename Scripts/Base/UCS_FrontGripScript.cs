
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_FrontGripScript : UdonSharpBehaviour
{
    public UCS_TwoHandedManager twoHandedManager;

    private VRC_Pickup pickup;

    void Start()
    {
        pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        if (pickup == null || Networking.LocalPlayer == null)
            return;

        if (Networking.LocalPlayer.IsUserInVR())
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.No;
        else
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
    }

    public override void OnPickup()
    {
        if (twoHandedManager != null)
            twoHandedManager.SecondaryPickup();

        if (pickup != null)
            pickup.pickupable = false;
    }

    public override void OnDrop()
    {
        if (twoHandedManager != null)
            twoHandedManager.SecondaryDrop();

        if (pickup != null)
            pickup.pickupable = true;
    }
}
