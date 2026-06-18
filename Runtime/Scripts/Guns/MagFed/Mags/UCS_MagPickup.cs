
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
[RequireComponent(typeof(VRC_Pickup))]

public class UCS_MagPickup : UdonSharpBehaviour
{
    [SerializeField] private UCS_Mag mag; //reference to the mag script on the mag prefab, which will handle ammo count and despawning
    [SerializeField] private UCS_MagPool magPool; //reference to the mag pool
    [SerializeField]private VRC_Pickup magPickup;
    private bool gunIsHeld;
    private VRCPlayerApi cachedPickupOwner;
    private UCS_MagSocket lastSocketPulledFrom;
    // manager responsibilities merged into UCS_Mag; no separate mag manager required

    // visuals are handled by `UCS_Mag` now

    private void Start()
    {
        if (magPickup == null)
        {
            magPickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        }

        UpdatePickupOwner();
        ApplyPickupState();
    }

    private void UpdatePickupOwner()
    {
        if (magPickup == null) return;
        VRCPlayerApi owner = Networking.GetOwner(magPickup.gameObject);
        if (owner != null)
        {
            cachedPickupOwner = owner;
        }
    }

    public void SetGunHeld(bool held)
    {
        gunIsHeld = held;
        ApplyPickupState();
    }

    public override void PostLateUpdate()
    {
        ApplyPickupState();
    }

    private void ApplyPickupState()
    {
        if (magPickup == null)
        {
            return;
        }

        bool isSocketed = mag != null && mag.IsSocketed();
        bool allowPickup = !isSocketed;

        if (isSocketed)
        {
            allowPickup = gunIsHeld && mag != null && Networking.IsOwner(mag.gameObject);
        }

        magPickup.pickupable = allowPickup;
    }

    public override void OnPickup()
    {
        // If this mag was socketed, tell the socket to eject it first so ammo transfer and state are handled.
        if (mag != null && mag.IsSocketed())
        {
            UCS_MagSocket socket = mag.GetSocket();
            if (socket != null)
            {
                lastSocketPulledFrom = socket;
                socket.EjectMag();
                socket.NotifyMagPulledFromSocket(mag);
            }
        }
        else if (mag != null && lastSocketPulledFrom != null)
        {
            lastSocketPulledFrom.NotifyMagPickedUpAfterDrop(mag);
        }

        if (mag != null)
        {
            mag.SetHeld(true);
            mag.ClearReturnToPool();

            // ensure pickup rigidbody behaves correctly when held
            mag.SetPickupUseGravity(true);
            mag.SetPickupKinematic(false);

            UCS_MagBelt sourceBelt = mag.GetSourceBelt();
            if (sourceBelt != null)
            {
                sourceBelt.OnMagPickedUpFromBelt(mag);
            }
        }

        if (mag != null)
        {
            mag.SetWorldVisible(true);
        }

        ApplyPickupState();
    }

    public override void OnDrop()
    {
        if (TryReturnToSourcePoolOnDesktopDrop())
        {
            ApplyPickupState();
            return;
        }

        if (mag != null && lastSocketPulledFrom != null)
        {
            lastSocketPulledFrom.NotifyMagDroppedAfterSocketPull(mag);
        }

        if (mag != null && !mag.IsSocketed())
        {
            mag.MarkDropped();
        }

        if (mag != null)
        {
            mag.SetHeld(false);
            if (!mag.IsSocketed())
            {
                mag.SetWorldVisible(true);
                // re-enable gravity and restore kinematic state when dropped
                mag.SetPickupUseGravity(true);
                mag.SetPickupKinematic(false);
            }
        }

        ApplyPickupState();
    }

    private bool TryReturnToSourcePoolOnDesktopDrop()
    {
        if (mag == null)
        {
            return false;
        }
        UpdatePickupOwner();
        // Only allow desktop (non-VR) owner to return mag to pool, and only if we're the owner locally.
        if (cachedPickupOwner == null || cachedPickupOwner != Networking.LocalPlayer || cachedPickupOwner.IsUserInVR())
        {
            return false;
        }

        if (magPool == null)
        {
            return false;
        }

        lastSocketPulledFrom = null;
        magPool.ReturnMagToPool(mag);
        return true;
    }

    public void Despawn()
    {
        if (mag != null)
        {
            mag.ResetForPool();
        }

        UCS_MagPool returnPool = magPool;
        if (returnPool == null && mag != null)
        {
            returnPool = mag.GetMagPool();
        }

        if (returnPool != null)
        {
            returnPool.ReturnMagToPool(gameObject);
        }
    }
}
