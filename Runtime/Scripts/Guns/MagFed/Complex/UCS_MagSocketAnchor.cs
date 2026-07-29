
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_MagSocketAnchor : UdonSharpBehaviour
{
    [SerializeField] private UCS_MagSocket magSocket;
    private Transform magPickupAnchor;

    void Start()
    {
        CacheAnchorTransform();
    }

    public void CacheAnchorTransform()
    {
        if (magSocket != null)
        {
            UCS_ComplexGun gun = magSocket.GetGun();
            if (gun != null)
            {
                magPickupAnchor = gun.GetMagazinePickupAnchor();
            }
        }
    }

    public Transform GetAnchorTransform()
    {
        return magPickupAnchor;
    }

    public override void PostLateUpdate()
    {
        if (magSocket == null)
        {
            gameObject.SetActive(false);
            return;
        }

        UCS_Mag currentMag = magSocket.GetCurrentMag();
        if (currentMag == null)
        {
            gameObject.SetActive(false);
            return;
        }

        UCS_ComplexGun gun = magSocket.GetGun();
        if (gun == null)
        {
            return;
        }

        // Cache the anchor transform each frame (the gun's anchor reference may change).
        magPickupAnchor = gun.GetMagazinePickupAnchor();
        if (magPickupAnchor == null)
        {
            return;
        }

        // Only the gun owner should drive the mag's position. Non-owners must not fight
        // VRCObjectSync here — PostLateUpdate runs after sync updates and would win visually.
        if (!Networking.IsOwner(gun.gameObject))
        {
            return;
        }

        Transform pickupRoot = currentMag.GetPickupRootTransform();
        if (pickupRoot != null)
        {
            pickupRoot.SetPositionAndRotation(magPickupAnchor.position, magPickupAnchor.rotation);
        }
    }
}
