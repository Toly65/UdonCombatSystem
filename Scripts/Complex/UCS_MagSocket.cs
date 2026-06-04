
using librsync.net;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_MagSocket : UdonSharpBehaviour
{
    [SerializeField] private UCS_ComplexGun gun; //reference to the gun script, which will handle the logic of accepting and ejecting mags
    [SerializeField] private UCS_MagBelt magBelt;
    [SerializeField] private GameObject magazineVisualObject;
    private UCS_Mag currentMag;
    private bool gunIsHeld;
    private int lastLoggedGunMagId = int.MinValue;
    private int lastLoggedCurrentMagId = int.MinValue;
    private int lastLoggedGunOwnerId = int.MinValue;

    private Transform magPickupAnchor;

    // manager responsibilities merged into UCS_Mag; operate directly on the mag

    void Start()
    {
        InitializeEmptySocket();   
    }

    public bool HasCurrentMag()
    {
        return currentMag != null;
    }

    public void OnEnable()
    {
        InitializeEmptySocket();
    }

    private void CacheMagPickupAnchor()
    {
        if (gun != null)
        {
            magPickupAnchor = gun.GetMagazinePickupAnchor();
        }
    }

    private void InitializeEmptySocket()
    {
        CacheMagPickupAnchor();

        bool localOwnsGun = gun != null && Networking.IsOwner(gun.gameObject);
        bool useMagBelt = gunIsHeld && localOwnsGun && Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR() && gun != null && magBelt != null;
        bool hasSyncedMag = gun != null && gun.GetInsertedMagId() >= 0;

        if (useMagBelt)
        {
            magBelt.SetRequestedMagSource(gun.GetAcceptedMagTypeId(), gun.GetRequiredMagPool());
        }
        else if (magBelt != null)
        {
            magBelt.ClearRequestedMag();
        }

        if (gun != null && (!hasSyncedMag || Networking.IsOwner(gun.gameObject)))
        {
            gun.SetMagazineInserted(false);
            gun.SetMagazineVisualVisible(false);
        }

        SetMagazineVisualObjectVisible(false);
    }

    private void PreInsertMag()// for when a reload explicitly needs a fresh mag
    {
        CacheMagPickupAnchor();

        bool localOwnsGun = gun != null && Networking.IsOwner(gun.gameObject);
        bool useMagBelt = gunIsHeld && localOwnsGun && Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR() && gun != null && magBelt != null;
        bool hasSyncedMag = gun != null && gun.GetInsertedMagId() >= 0;

        if (useMagBelt)
        {
            magBelt.SetRequestedMagSource(gun.GetAcceptedMagTypeId(), gun.GetRequiredMagPool());
        }
        else if (magBelt != null)
        {
            magBelt.ClearRequestedMag();
        }

        if (gun != null && (!hasSyncedMag || Networking.IsOwner(gun.gameObject)))
        {
            gun.SetMagazineInserted(false);
            gun.SetMagazineVisualVisible(false);
        }

        if (!hasSyncedMag)
        {
            SetMagazineVisualObjectVisible(false);
        }

        if (currentMag == null && gun != null)
        {
            UCS_MagPool magPool = gun.GetRequiredMagPool();
            if (magPool != null)
            {
                // Only the gun owner spawns a mag; without this every client calls TryToSpawn
                // at reload time and depletes the pool with orphaned mags.
                if (!Networking.IsOwner(gun.gameObject))
                {
                    return;
                }

                // Always spawn a full mag; the prefill-from-inventory concept has been removed.
                VRCPlayerApi gunOwner = gun != null ? gun.GetCachedPickupOwner() : null;
                GameObject magObject = magPool.AcquireFullMag(gunOwner);
                if (magObject != null)
                {
                    UCS_Mag spawnedMag = magObject.GetComponent<UCS_Mag>();
                    if (spawnedMag == null)
                    {
                        spawnedMag = magObject.GetComponentInChildren<UCS_Mag>(true);
                    }
                    InsertMag(spawnedMag);
                }
            }
        }
        if(currentMag != null)
        {
            SetMagazineVisualObjectVisible(true);
        }
    }

    public void InsertMag(UCS_Mag mag)// for when a mag is being inserted into the gun
    {
        CacheMagPickupAnchor();

        if (mag == null)
        {
            return;
        }

        if (gun != null && !string.IsNullOrEmpty(gun.GetAcceptedMagTypeId()) && mag.GetMagTypeId() != gun.GetAcceptedMagTypeId())
        {
            return;
        }

        if (currentMag != null && currentMag != mag)
        {
            EjectMag();
        }

        currentMag = mag;
        if (currentMag != null)
        {
            EnsureGunOwnerOwnsMag(currentMag);
            if (gun != null)
            {
                gun.SetInsertedMagId(currentMag.GetMagId());
            }
            currentMag.SetSocketed(true);
            currentMag.SetHeld(false);
            currentMag.SetWorldVisible(false);
            currentMag.SetPickupVisualVisible(false);
            currentMag.ClearReturnToPool();
            currentMag.SetSocket(this);
            //gun.SetMagazineVisualVisible(true);
            ApplyMagPickupAnchor();
            // disable gravity and make kinematic on the mag pickup while socketed so it stays aligned to the socket
            currentMag.SetPickupUseGravity(false);
            currentMag.SetPickupKinematic(true);

            UCS_MagPickup magPickup = currentMag.GetMagPickup();
            if (magPickup != null)
            {
                magPickup.SetGunHeld(gunIsHeld);
            }
        }

        if (gun != null)
        {
            gun.SetMagazineInserted(true);
            gun.SetMagazineVisualVisible(true);
            SetMagazineVisualObjectVisible(true);
            // Inform the gun of the inserted mag so it can adopt the mag's ammo count.
            gun.OnMagazineInserted(currentMag);
        }
    }

    public void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            return;
        }

        // Only the gun owner should process physical mag insertions; on remote clients
        // VRCObjectSync moves the mag to the anchor and would otherwise fire this trigger
        // spuriously, setting currentMag and breaking ApplyMagPickupAnchor on those clients.
        if (gun == null || !Networking.IsOwner(gun.gameObject))
        {
            return;
        }

        UCS_Mag mag = other.GetComponentInParent<UCS_Mag>();
        if (mag == null)
        {
            return;
        }

        if (currentMag == mag)
        {
            return;
        }

        if (gun != null && !string.IsNullOrEmpty(gun.GetAcceptedMagTypeId()) && mag.GetMagTypeId() != gun.GetAcceptedMagTypeId())
        {
            return;
        }

        InsertMag(mag);
    }
    
    public void EjectMag()// for when the mag is being ejected from the gun, either by dropping it in the world or inserting it into another gun
    {
        if (currentMag == null)
        {
            return;
        }

        if (currentMag != null)
        {
            // Let the gun play pull sound / update state, then transfer ammo into the mag so it reflects remaining rounds.
            if (gun != null)
            {
                gun.NotifyMagazineEjected();
                gun.TransferAmmoToMag(currentMag);
                gun.SetInsertedMagId(-1);
            }

            currentMag.SetSocketed(false);
            currentMag.SetHeld(true);
            currentMag.SetWorldVisible(true);
            // re-enable gravity and restore kinematic state on the mag pickup when it's ejected to the world
            currentMag.SetPickupUseGravity(true);
            currentMag.SetPickupKinematic(false);

            UCS_MagPickup magPickup = currentMag.GetMagPickup();
            if (magPickup != null)
            {
                magPickup.SetGunHeld(false);
            }
            currentMag.ClearSocket();
        }

        if (gun != null)
        {
            gun.SetMagazineInserted(false);
            gun.SetMagazineVisualVisible(false);
        }

        SetMagazineVisualObjectVisible(false);

        currentMag = null;
    }

    public void ReloadMag()
    {
        EjectMag();
        PreInsertMag();
    }

    // Eject the mag and then insert a fresh one after a delay (so the mag is visibly out)
    public void ReloadMagDelayed(float delay)
    {
        UCS_Mag ejectedMag = currentMag;
        EjectMag();

        if (ejectedMag != null)
        {
            ejectedMag.MarkDropped();
        }

        // schedule the delayed pre-insert on this socket
        SendCustomEventDelayedSeconds("PerformDelayedPreInsert", delay);
    }

    public void PerformDelayedPreInsert()
    {
        PreInsertMag();
    }

    public override void PostLateUpdate()
    {
        RefreshSocketedMagFromGunState();
        ApplyMagPickupAnchor();
    }

    public void RefreshSocketedMagFromGunState()
    {
        if (gun == null)
        {
            currentMag = null;
            return;
        }

        int gunMagId = gun.GetInsertedMagId();
        int ownerId = -1;
        var owner = Networking.GetOwner(gun.gameObject);
        if (owner != null) ownerId = owner.playerId;
        int currentMagId = (currentMag != null ? currentMag.GetMagId() : -1);
        if (gunMagId != lastLoggedGunMagId || currentMagId != lastLoggedCurrentMagId || ownerId != lastLoggedGunOwnerId)
        {
            lastLoggedGunMagId = gunMagId;
            lastLoggedCurrentMagId = currentMagId;
            lastLoggedGunOwnerId = ownerId;
            Debug.Log($"[UCS_MagSocket] RefreshSocketedMagFromGunState gun={gun.gameObject.name} gunMagId={gunMagId} currentMagId={currentMagId} owner={ownerId}");
        }
        if (gunMagId < 0)
        {
            currentMag = null;
            gun.SetMagazineInserted(false);
            SetMagazineVisualObjectVisible(false);
            return;
        }

        if (currentMag != null && currentMag.GetMagId() != gunMagId)
        {
            currentMag = null;
        }

        if (currentMag != null && currentMag.GetMagId() == gunMagId)
        {
            EnsureGunOwnerOwnsMag(currentMag);
            currentMag.SetPickupUseGravity(false);
            currentMag.SetPickupKinematic(true);
            gun.SetMagazineInserted(true);
            return;
        }

        UCS_MagPool magPool = gun.GetRequiredMagPool();
        if (magPool == null)
        {
            return;
        }

        UCS_Mag resolvedMag = magPool.FindActiveMagById(gunMagId);
        if (resolvedMag != null)
        {
            int resolvedId = resolvedMag.GetMagId();
            if (resolvedId != lastLoggedCurrentMagId)
            {
                currentMag = resolvedMag;
                EnsureGunOwnerOwnsMag(currentMag);
                lastLoggedCurrentMagId = resolvedId;
                Debug.Log($"[UCS_MagSocket] Resolved magId={(currentMag!=null?currentMag.GetMagId():-1)} magObj={(currentMag!=null?currentMag.gameObject.name:"null")}");
            }
            else
            {
                currentMag = resolvedMag;
            }

            currentMag.SetSocket(this);
            EnsureGunOwnerOwnsMag(currentMag);
            currentMag.SetSocketed(true);
            currentMag.SetHeld(false);
            currentMag.SetPickupUseGravity(false);
            currentMag.SetPickupKinematic(true);
            currentMag.ClearReturnToPool();
        }

        gun.SetMagazineInserted(currentMag != null);
        SetMagazineVisualObjectVisible(currentMag != null);
        int afterId = (currentMag != null ? currentMag.GetMagId() : -1);
        if (afterId != lastLoggedCurrentMagId)
        {
            lastLoggedCurrentMagId = afterId;
            Debug.Log($"[UCS_MagSocket] AfterRefresh currentMagId={afterId}");
        }
    }

    private void ApplyMagPickupAnchor()
    {
        CacheMagPickupAnchor();

        if (currentMag == null || gun == null)
        {
            return;
        }
        if (magPickupAnchor == null)
        {
            return;
        }

        // Belt-and-suspenders: even if currentMag is somehow set on a non-owner client,
        // only the gun owner should drive the mag's position. Non-owners must not fight
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

    private void SetMagazineVisualObjectVisible(bool visible)
    {
        if (magazineVisualObject != null)
        {
            magazineVisualObject.SetActive(visible);
        }
    }

    private void EnsureGunOwnerOwnsMag(UCS_Mag mag)
    {
        if (mag == null || gun == null || !Networking.IsOwner(gun.gameObject))
        {
            return;
        }

        Networking.SetOwner(Networking.LocalPlayer, mag.gameObject);
        Transform magPickupRoot = mag.GetPickupRootTransform();
        if (magPickupRoot != null && magPickupRoot.gameObject != mag.gameObject)
        {
            Networking.SetOwner(Networking.LocalPlayer, magPickupRoot.gameObject);
        }
    }

    public void RefreshSocketedMagPickupState()
    {
        if (currentMag == null)
        {
            return;
        }

        UCS_MagPickup magPickup = currentMag.GetMagPickup();
        if (magPickup != null)
        {
            if (gun != null)
            {
                magPickup.SetGunHeld(true);
            }
        }
    }

    // Called when the local player picks up the gun. If this client's currentMag is null
    // (ownership transferred without InsertMag ever running here), find the mag that is
    // frozen at the anchor position and re-establish the socket relationship so EjectMag
    // and ApplyMagPickupAnchor work correctly for the new owner.
    public void TryReattachSocketedMag()
    {
        if (currentMag != null)
        {
            return;
        }

        CacheMagPickupAnchor();
        if (magPickupAnchor == null || gun == null)
        {
            return;
        }

        UCS_MagPool magPool = gun.GetRequiredMagPool();
        if (magPool == null)
        {
            return;
        }

        int insertedMagId = gun.GetInsertedMagId();
        UCS_Mag found = insertedMagId >= 0 ? magPool.FindActiveMagById(insertedMagId) : null;
        if (found == null)
        {
            found = magPool.FindActiveMagNear(magPickupAnchor.position, 0.5f);
        }
        if (found == null)
        {
            return;
        }

        // Take ownership of the frozen mag before reasserting socket state.
        Networking.SetOwner(Networking.LocalPlayer, found.gameObject);
        Transform foundPickupRoot = found.GetPickupRootTransform();
        if (foundPickupRoot != null && foundPickupRoot.gameObject != found.gameObject)
        {
            Networking.SetOwner(Networking.LocalPlayer, foundPickupRoot.gameObject);
        }

        currentMag = found;
        EnsureGunOwnerOwnsMag(currentMag);
        currentMag.SetSocketed(true);
        currentMag.SetHeld(false);
        currentMag.SetSocket(this);
        currentMag.SetPickupUseGravity(false);
        currentMag.SetPickupKinematic(true);

        gun.SetMagazineInserted(true);
    }

    public void SetSocketedMagGunHeld(bool held)
    {
        gunIsHeld = held;

        if (currentMag == null)
        {
            return;
        }

        UCS_MagPickup magPickup = currentMag.GetMagPickup();
        if (magPickup != null)
        {
            magPickup.SetGunHeld(held);
        }
    }

    
}
