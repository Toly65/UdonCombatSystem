
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
    [SerializeField] private float vrReinsertLockSeconds = 1f;
    [SerializeField] private bool disableDesktopPhysicalInsertion = true;
    private UCS_Mag currentMag;
    private bool gunIsHeld;
    private UCS_Mag blockedInsertMag;
    private bool blockedInsertMagLockActive;
    private bool blockedInsertMagDroppedSincePull;
    private int lastLoggedGunMagId = int.MinValue;
    private int lastLoggedCurrentMagId = int.MinValue;
    private int lastLoggedGunOwnerId = int.MinValue;

    // Resolving a socketed mag by pool ID is async (SetOwner + synced variables can lag
    // behind the gun's syncedMagazineId, especially on a handoff / late join). Retry a
    // short window before giving up so a freshly-handed gun still adopts its mag instead
    // of leaving the socket (and thus the anchor) empty.
    private const int MaxMagResolveRetries = 6;
    private const int MagResolveRetryDelayFrames = 5;
    private int pendingMagResolveRetries;

    // Networking.SetOwner is async, and the OLD owner's stale sync packets can keep overwriting the
    // mag's isKinematic for a latency-dependent time AFTER the mag becomes ours. This is a short
    // BOUNDED settle window (delayed events, no per-frame loop — zero cost when idle) that keeps
    // re-applying the socketed physics through that whole tail, then stops. The pickup child's
    // OnOwnershipTransferred handles the immediate ownership-landing moment.
    private const int MaxSocketedKinematicReasserts = 30; // ~1.5s at 0.05s intervals
    private const float SocketedKinematicReassertInterval = 0.05f;
    private int pendingSocketedKinematicReasserts;

    [SerializeField] private UCS_MagSocketAnchor magSocketAnchor;

    // manager responsibilities merged into UCS_Mag; operate directly on the mag

    void Start()
    {
        InitializeEmptySocket();   
    }

    public bool HasCurrentMag()
    {
        return currentMag != null;
    }

    public UCS_Mag GetCurrentMag()
    {
        return currentMag;
    }

    public UCS_ComplexGun GetGun()
    {
        return gun;
    }

    public void OnEnable()
    {
        RefreshSocketedMagFromGunState();
    }

    private void InitializeEmptySocket()
    {
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
            // disable gravity and make kinematic on the mag pickup while socketed so the anchor can keep it in place
            currentMag.SetPickupUseGravity(false);
            currentMag.SetPickupKinematic(true);
            // ponytail: stays true while socketed. isKinematic already stops the mag being moved
            // by physics; detectCollisions=false additionally hides it from VRChat's grab search.
            currentMag.SetPickupDetectCollisions(true);
        }

        if (gun != null)
        {
            gun.SetMagazineInserted(true);
            gun.SetMagazineVisualVisible(true);
            SetMagazineVisualObjectVisible(true);
            // Inform the gun of the inserted mag so it can adopt the mag's ammo count.
            gun.OnMagazineInserted(currentMag);
        }

        UpdateAnchorState();
        ScheduleSocketedKinematicReassert();
    }

    // Ownership transfer (Networking.SetOwner) is async, and the old owner's stale sync packets can
    // keep overwriting the mag's isKinematic for a latency-dependent time AFTER the mag becomes ours.
    // This bounded settle window (delayed events, not a per-frame loop — zero cost when idle) keeps
    // re-applying the socketed physics through that whole tail, then stops.
    private void ScheduleSocketedKinematicReassert()
    {
        // Only the spawn path (mag not yet owned at insert) has the ownership handoff + stale-sync
        // tail. A physically inserted mag is already owned by the local player — nothing to settle.
        if (currentMag == null || IsMagPickupOwnedLocally(currentMag))
        {
            pendingSocketedKinematicReasserts = 0;
            return;
        }

        pendingSocketedKinematicReasserts = MaxSocketedKinematicReasserts;
        SendCustomEventDelayedSeconds(nameof(ReassertSocketedKinematic), SocketedKinematicReassertInterval);
    }

    // Delayed entry point (see ScheduleSocketedKinematicReassert). Re-applies the socketed physics
    // every interval through the whole settle window — EVEN after the mag becomes ours — because the
    // old owner's stale VRCObjectSync packets can still flip isKinematic=false for a while. Once the
    // window expires (or the mag is ejected) it stops and costs nothing.
    public void ReassertSocketedKinematic()
    {
        if (currentMag == null)
        {
            pendingSocketedKinematicReasserts = 0;
            return;
        }

        if (pendingSocketedKinematicReasserts <= 0)
        {
            return;
        }

        pendingSocketedKinematicReasserts--;

        if (!IsMagPickupOwnedLocally(currentMag))
        {
            EnsureGunOwnerOwnsMag(currentMag);
        }
        currentMag.ReassertSocketedPhysics();

        if (pendingSocketedKinematicReasserts > 0)
        {
            SendCustomEventDelayedSeconds(nameof(ReassertSocketedKinematic), SocketedKinematicReassertInterval);
        }
    }

    // Whether the local player owns the pickup child — the object that actually carries the
    // Rigidbody and VRCObjectSync. This is the ownership that decides whether our kinematic setting
    // sticks; owning only the mag root is not enough.
    private bool IsMagPickupOwnedLocally(UCS_Mag mag)
    {
        if (mag == null)
        {
            return true;
        }

        Transform pickupRoot = mag.GetPickupRootTransform();
        if (pickupRoot != null && pickupRoot.gameObject != mag.gameObject)
        {
            return Networking.IsOwner(pickupRoot.gameObject);
        }

        return Networking.IsOwner(mag.gameObject);
    }

    public void OnTriggerEnter(Collider other)
    {
        if (other == null)
        {
            return;
        }

        if (disableDesktopPhysicalInsertion && Networking.LocalPlayer != null && !Networking.LocalPlayer.IsUserInVR())
        {
            return;
        }

        // Only the gun owner should process physical mag insertions; on remote clients
        // VRCObjectSync moves the mag to the anchor and would otherwise fire this trigger
        // spuriously, setting currentMag on those clients.
        if (gun == null || !Networking.IsOwner(gun.gameObject))
        {
            return;
        }

        UCS_Mag mag = other.GetComponentInParent<UCS_Mag>();
        if (mag == null)
        {
            return;
        }

        if (!mag.IsHeld())
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

        if (IsMagBlockedForInsertion(mag))
        {
            return;
        }

        InsertMag(mag);
    }

    private bool IsMagBlockedForInsertion(UCS_Mag mag)
    {
        if (!blockedInsertMagLockActive || mag == null || blockedInsertMag == null)
        {
            return false;
        }

        return mag == blockedInsertMag;
    }

    public void NotifyMagPulledFromSocket(UCS_Mag mag)
    {
        if (mag == null)
        {
            return;
        }

        blockedInsertMag = mag;
        blockedInsertMagLockActive = true;
        blockedInsertMagDroppedSincePull = false;

        bool isVR = Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR();
        if (!isVR)
        {
            // Desktop users use a separate reload flow; keep this pulled mag out of insert path.
            return;
        }

        if (vrReinsertLockSeconds <= 0f)
        {
            blockedInsertMagLockActive = false;
            return;
        }

        SendCustomEventDelayedSeconds(nameof(ClearMagInsertLockByTimer), vrReinsertLockSeconds);
    }

    public void ClearMagInsertLockByTimer()
    {
        blockedInsertMagLockActive = false;
    }

    public void NotifyMagDroppedAfterSocketPull(UCS_Mag mag)
    {
        if (mag == null || blockedInsertMag == null)
        {
            return;
        }

        if (mag == blockedInsertMag)
        {
            blockedInsertMagDroppedSincePull = true;
        }
    }

    public void NotifyMagPickedUpAfterDrop(UCS_Mag mag)
    {
        if (mag == null || blockedInsertMag == null)
        {
            return;
        }

        if (mag == blockedInsertMag && blockedInsertMagDroppedSincePull)
        {
            blockedInsertMagLockActive = false;
            blockedInsertMagDroppedSincePull = false;
        }
    }
    
    public void EjectMag()// for when the mag is being ejected from the gun, either by dropping it in the world or inserting it into another gun
    {
        if (currentMag == null)
        {
            return;
        }

        if (currentMag != null)
        {
            UCS_Mag ejectedMag = currentMag;
            // Let the gun play pull sound / update state, then transfer ammo into the mag so it reflects remaining rounds.
            if (gun != null)
            {
                gun.NotifyMagazineEjected();
                gun.TransferAmmoToMag(ejectedMag);
                gun.SetInsertedMagId(-1);
            }

            ejectedMag.SetSocketed(false);
            ejectedMag.SetHeld(true);
            ejectedMag.SetWorldVisible(true);
            // re-enable gravity and restore kinematic state on the mag pickup when it's ejected to the world
            ejectedMag.SetPickupUseGravity(true);
            ejectedMag.SetPickupKinematic(false);
            ejectedMag.SetPickupDetectCollisions(true);
            ejectedMag.ClearSocket();

            // If this mag is never grabbed again (e.g. swapped out by inserting a fresh mag), it
            // would otherwise sit in the world forever with isHeld=true and no cleanup timer, and
            // the pool would never reclaim it. Schedule a safety cleanup.
            ejectedMag.ScheduleCleanupIfAbandoned();
        }

        if (gun != null)
        {
            gun.SetMagazineInserted(false);
            gun.SetMagazineVisualVisible(false);
        }

        SetMagazineVisualObjectVisible(false);

        currentMag = null;
        UpdateAnchorState();
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

    // Explicit reset path for external systems (for example a gun spawner/stow reset).
    // Unlike OnEnable, this intentionally clears any socketed mag.
    public void ClearSocketForSpawner()
    {
        if (gun != null && !Networking.IsOwner(gun.gameObject))
        {
            return;
        }

        if (currentMag == null)
        {
            if (gun != null)
            {
                gun.SetInsertedMagId(-1);
                gun.SetMagazineInserted(false);
                gun.SetMagazineVisualVisible(false);
            }

            SetMagazineVisualObjectVisible(false);
            return;
        }

        UCS_Mag magToClear = currentMag;

        if (gun != null)
        {
            gun.TransferAmmoToMag(magToClear);
            gun.SetInsertedMagId(-1);
            gun.SetMagazineInserted(false);
            gun.SetMagazineVisualVisible(false);
        }

        magToClear.ClearSocket();
        magToClear.SetHeld(false);
        magToClear.SetWorldVisible(false);
        magToClear.SetPickupVisualVisible(false);
        magToClear.SetPickupUseGravity(false);
        magToClear.SetPickupKinematic(true);
        magToClear.SetPickupDetectCollisions(false);
        magToClear.ClearReturnToPool();

        currentMag = null;
        SetMagazineVisualObjectVisible(false);
        UpdateAnchorState();

        UCS_MagPool pool = magToClear.GetMagPool();
        if (pool != null)
        {
            pool.ReturnMagToPool(magToClear);
        }
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
            pendingMagResolveRetries = 0;
            gun.SetMagazineInserted(false);
            SetMagazineVisualObjectVisible(false);
            UpdateAnchorState();
            return;
        }

        if (currentMag != null && currentMag.GetMagId() != gunMagId)
        {
            currentMag = null;
        }

        if (currentMag != null && currentMag.GetMagId() == gunMagId)
        {
            // Re-link the mag to this socket (guards against a stale link on a pooled mag)
            // so UCS_MagPickup.ApplyPickupState() sees it as socketed and the anchor knows
            // which mag to position.
            currentMag.SetSocket(this);
            EnsureGunOwnerOwnsMag(currentMag);
            currentMag.SetPickupUseGravity(false);
            currentMag.SetPickupKinematic(true);
            // ponytail: stays true while socketed. isKinematic already stops the mag being moved
            // by physics; detectCollisions=false additionally hides it from VRChat's grab search.
            currentMag.SetPickupDetectCollisions(true);
            currentMag.SetWorldVisible(false);
            currentMag.SetPickupVisualVisible(false);
            gun.SetMagazineInserted(true);
            UpdateAnchorState();
            return;
        }

        UCS_MagPool magPool = gun.GetRequiredMagPool();
        if (magPool == null)
        {
            return;
        }

        UCS_Mag resolvedMag = magPool.FindActiveMagById(gunMagId);
        if (resolvedMag == null)
        {
            // The gun says a mag is inserted, but this client hasn't received that mag's
            // synced state yet (SetOwner + synced vars are async; a late joiner may see the
            // gun id before the mag object). Do NOT clear the gun's synced inserted / visual
            // state here — just retry for a short window.
            if (pendingMagResolveRetries < MaxMagResolveRetries)
            {
                pendingMagResolveRetries++;
                SendCustomEventDelayedFrames(nameof(RetryRefreshSocketedMag), MagResolveRetryDelayFrames);
            }
            else
            {
                // Retries exhausted: the mag is genuinely not resolvable on this client.
                // Fall back to clearing the socket so the gun doesn't claim an inserted mag.
                currentMag = null;
                gun.SetMagazineInserted(false);
                SetMagazineVisualObjectVisible(false);
                UpdateAnchorState();
            }
            return;
        }
        pendingMagResolveRetries = 0;

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
        // ponytail: stays true while socketed. isKinematic already stops the mag being moved
        // by physics; detectCollisions=false additionally hides it from VRChat's grab search.
        currentMag.SetPickupDetectCollisions(true);
        currentMag.SetWorldVisible(false);
        currentMag.SetPickupVisualVisible(false);
        currentMag.ClearReturnToPool();

        gun.SetMagazineInserted(currentMag != null);
        SetMagazineVisualObjectVisible(currentMag != null);
        int afterId = (currentMag != null ? currentMag.GetMagId() : -1);
        if (afterId != lastLoggedCurrentMagId)
        {
            lastLoggedCurrentMagId = afterId;
            Debug.Log($"[UCS_MagSocket] AfterRefresh currentMagId={afterId}");
        }
        UpdateAnchorState();
    }

    // Delayed-retry entry point (see MaxMagResolveRetries above).
    public void RetryRefreshSocketedMag()
    {
        RefreshSocketedMagFromGunState();
    }

    private void UpdateAnchorState()
    {
        if (magSocketAnchor != null)
        {
            // The anchor is only needed for the local gun owner. While socketed the mag pickup
            // has SetWorldVisible(false) + SetPickupDetectCollisions(false), so remote players
            // cannot see or grab it — there's nothing to anchor for them.
            bool shouldBeActive = currentMag != null
                && gun != null
                && Networking.IsOwner(gun.gameObject);
            if (magSocketAnchor.gameObject.activeSelf != shouldBeActive)
            {
                magSocketAnchor.gameObject.SetActive(shouldBeActive);
            }
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

    public void SetSocketedMagGunHeld(bool held)
    {
        gunIsHeld = held;

        // Transfer socket GameObject ownership to the player holding the gun so that
        // network events and VRCObjectSync on child objects (e.g. the anchor) work.
        if (held && gun != null && Networking.IsOwner(gun.gameObject) && !Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    public bool IsGunHeld()
    {
        return gunIsHeld;
    }
}
