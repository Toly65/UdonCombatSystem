
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_PickupEventTransferer : UdonSharpBehaviour
{
    public UdonBehaviour targetBehaviour;
    public UCS_TwoHandedManager twoHandedManager;
    public string pickupUseDownEvent = "TriggerPull";
    public string pickupUseUpEvent = "TriggerRelease";
    public string pickupDropEvent = "Drop";
    public string pickupEvent = "Pickup";
    private VRC_Pickup pickup;

    private void Start()
    {
        pickup = (VRC_Pickup)this.GetComponent(typeof(VRC_Pickup));
        if(Networking.LocalPlayer.IsUserInVR())
        {
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.No;
        }else
        {
            pickup.AutoHold = VRC_Pickup.AutoHoldMode.Yes;
        }
    }
    public override void OnPickupUseDown()
    {
        targetBehaviour.SendCustomEvent(pickupUseDownEvent);
    }

    public override void OnPickupUseUp()
    {
        targetBehaviour.SendCustomEvent(pickupUseUpEvent);
    }

    public override void OnDrop()
    {
        targetBehaviour.SendCustomEvent(pickupDropEvent);
        if (twoHandedManager != null)
            twoHandedManager.PrimaryDrop();
        pickup.pickupable = true;
    }
    public override void OnPickup()
    {
        Networking.SetOwner(Networking.LocalPlayer, targetBehaviour.gameObject);
        targetBehaviour.SendCustomEvent(pickupEvent);
        if (twoHandedManager != null)
            twoHandedManager.PrimaryPickup();
        pickup.pickupable = false; // we do this so that the player can't pickup the pickup from their own hand, which causes issues with the gun interactions.
    }
}
