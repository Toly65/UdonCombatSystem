
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

//enum for which hand is closest


public class GrenadeScript : UdonSharpBehaviour
{
    public Transform pin;
    public float pinGrabProximity = 0.1f;
    public float pinPullDistance = 0.1f;
    public VRC_Pickup pickup;


    private bool isPulled = false;
    private bool isThrown = false;

    void Start()
    {

    }

    private float offHandDistance;
    private Vector3 offHandPos;
    private VRC_Pickup.PickupHand offHand;
    
    void Update()
    {
        if(pickup.IsHeld)
        {
            
           
        }
       
    }

    public override void OnPickup()
    {
        //set the offhand to the hand that is not holding the grenade
        if (pickup.currentHand == VRC_Pickup.PickupHand.Left)
        {
            offHand = VRC_Pickup.PickupHand.Right;
        }
        else
        {
            offHand = VRC_Pickup.PickupHand.Left;
        }
    }
    float RTrigger = 0;
    float LTrigger = 0;

    private bool offHandGrabbingAlready;
    private bool offHandGrabbedPin = false;
    public bool OffHandGrabbingPin()
    {
        RTrigger = Input.GetAxisRaw("Oculus_CrossPlatform_SecondaryIndexTrigger");
        LTrigger = Input.GetAxisRaw("Oculus_CrossPlatform_PrimaryIndexTrigger");
        //check if the offhand is grabbing within proximity of the pin
        if (offHand == VRC_Pickup.PickupHand.Left)
        {
            offHandDistance = Vector3.Distance(pin.position, Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position);
        }
        else
        {
            offHandDistance = Vector3.Distance(pin.position, Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position);
        }
        if(!offHandGrabbingAlready)
        {

        }

        return false;
    }
}
