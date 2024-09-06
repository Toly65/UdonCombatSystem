
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.Animations;
using VRC.SDK3.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class GripScript : UdonSharpBehaviour
{
    private Rigidbody body;
    public Transform gunGrip;
    public VRCPickup gunPickup;
    public Transform gunParent;
    private Vector3 offsetFromGun;
    public void Start()
    {
        body = gameObject.GetComponent<Rigidbody>();
        //record offset from gun
        offsetFromGun = transform.localPosition - gunGrip.localPosition;
        AttachSelfToGun();
    }

    private void OnDrop()
    {
        body.isKinematic = true;
        if (gunPickup.IsHeld)
        {
            AttachSelfToGun();
        }
        //reposition the grip to the offset from the gun
        //transform.localPosition = gunGrip.localPosition + offsetFromGun;
    }

    private void OnPickup()
    {
        body.isKinematic = true;
        DetachSelfFromGun();
    }

    public void AttachSelfToGun()
    {
        gunGrip.SetParent(gunParent);
        transform.SetParent(gunGrip);
        body.isKinematic = false;
        transform.localPosition = gunGrip.localPosition + offsetFromGun;
    }

    public void DetachSelfFromGun()
    {
        transform.SetParent(gunParent);
        gunGrip.SetParent(gunParent);
    }

    public void AttachGunToSelf()
    {
        transform.SetParent(gunParent);
        gunGrip.SetParent(transform);
    }

    public void DetachGunFromSelf()
    {
        //make the parent constraint inactive to allow the grip to be moved around
        gunGrip.SetParent(gunParent);
    }
}
