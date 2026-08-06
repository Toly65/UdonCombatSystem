
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_Mag : UdonSharpBehaviour
{
    [SerializeField] private UCS_MagPool magPool; //reference to the mag pool, which will handle pooling and despawning of mags
    [UdonSynced] private int magId = -1;
    [UdonSynced] private bool syncedInUse;
    [UdonSynced] private int currentAmmo;
    [UdonSynced] private bool syncedPickupRootActive;
    [UdonSynced] private bool syncedPickupVisualActive;
    [UdonSynced] private bool syncedSocketed;

    [SerializeField] private float lifetimeAfterNonInteraction = 30f; //time in seconds after the mag is dropped that it will be destroyed, to prevent cluttering the world with infinite mags
    private float dropTime;

    private bool isHeld;
    private bool isSocketed;
    private bool isInUse;
    private UCS_MagBelt sourceBelt;
    private UCS_MagPool returnToPoolOnDrop;
    private Transform returnHolsterPoint;
    private float returnHolsterDistance;
    private UCS_MagSocket currentSocket;

    [SerializeField] private UCS_AmmoInventory ammoInventory;
    // Root GameObject that contains the pickup logic (the mag pickup). Toggle this to allow/disallow grabbing.
    [SerializeField] private GameObject magPickupRoot;
    // The visual model of the mag (can be enabled/disabled independently of the pickup root).
    [SerializeField] private GameObject magPickupVisual;

    private void Start()
    {
        SyncDefinitionFromPool();
        // cache mag pickup component to avoid repeated GetComponent calls
        if (magPickupRoot != null)
        {
            cachedMagPickup = magPickupRoot.GetComponent<UCS_MagPickup>();
        }
    }

    private UCS_MagPickup cachedMagPickup;
    private Rigidbody cachedMagRigidbody;
    private VRCObjectSync cachedMagObjectSync;
    private string lastLoggedMagState = "";

    public void SetMagPool(UCS_MagPool newMagPool)
    {
        magPool = newMagPool;
        SyncDefinitionFromPool();
    }

    public int GetMagId()
    {
        return magId;
    }

    public void SetMagId(int newMagId)
    {
        magId = newMagId;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public UCS_MagPool GetMagPool()
    {
        return magPool;
    }

    public void SetSourceBelt(UCS_MagBelt belt)
    {
        sourceBelt = belt;
    }

    public UCS_MagBelt GetSourceBelt()
    {
        return sourceBelt;
    }

    private void SyncDefinitionFromPool()
    {
        currentAmmo = Mathf.Clamp(currentAmmo, 0, GetMaxAmmo());
    }

    public string GetMagTypeId()
    {
        if (magPool != null)
        {
            return magPool.GetMagTypeId();
        }

        return string.Empty;
    }

    public int GetCurrentAmmo()
    {
        return currentAmmo;
    }

    public void SetCurrentAmmo(int ammo)
    {
        currentAmmo = Mathf.Clamp(ammo, 0, GetMaxAmmo());
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        isSocketed = syncedSocketed;
        isInUse = syncedInUse;
        int ownerId = -1;
        var owner = Networking.GetOwner(gameObject);
        if (owner != null) ownerId = owner.playerId;
        if (magPickupRoot != null)
        {
            if (isSocketed && !Networking.IsOwner(gameObject))
            {
                magPickupRoot.SetActive(false);
            }
            else
            {
                magPickupRoot.SetActive(syncedPickupRootActive);
            }
        }
        if (magPickupVisual != null)
        {
            magPickupVisual.SetActive(syncedPickupVisualActive);
        }
        ApplyVisualState();

        bool pickupRootActive = (magPickupRoot != null ? magPickupRoot.activeSelf : false);
        bool pickupVisualActive = (magPickupVisual != null ? magPickupVisual.activeSelf : false);

        string messageKey = $"magId={magId}:owner={ownerId}:syncedSocketed={syncedSocketed}:syncedPickupRootActive={syncedPickupRootActive}:syncedPickupVisualActive={syncedPickupVisualActive}:actualRoot={pickupRootActive}:actualVisual={pickupVisualActive}:isSocketed={isSocketed}";
        if (lastLoggedMagState != messageKey)
        {
            lastLoggedMagState = messageKey;
            //Debug.Log($"[UCS_Mag] OnDeserialization magId={magId} ownerId={ownerId} syncedSocketed={syncedSocketed} syncedPickupRootActive={syncedPickupRootActive} syncedPickupVisualActive={syncedPickupVisualActive} magPickupRootActive={pickupRootActive} magPickupVisualActive={pickupVisualActive} isSocketed={isSocketed}");
        }
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (player == null || player != Networking.LocalPlayer)
        {
            return;
        }

        // Use the socket as the source of truth rather than the local isSocketed flag, which can be
        // clobbered by a stale OnDeserialization (syncedSocketed) during the latency window.
        UCS_MagSocket socket = currentSocket;
        bool isSocketedNow = socket != null && socket.GetCurrentMag() == this;

        if (isSocketedNow)
        {
            // Networking.SetOwner is async: a freshly spawned mag can be socketed (InsertMag)
            // while ownership is still pending. Until it lands, the pickup child's VRCObjectSync
            // is authoritative for the rigidbody on the non-owner side and keeps the mag
            // non-kinematic. This is the reliable "we own it now" moment — re-apply the socketed
            // physics state so the anchor can hold the mag.
            SetPickupUseGravity(false);
            SetPickupKinematic(true);
            SetPickupDetectCollisions(true);
        }

        // Re-push our state now that we own the object. SetOwner is async, so any
        // RequestSerialization made in the same frame as SetOwner (spawn, socket change, or a
        // return-to-pool reset) is silently dropped; this is the reliable moment to send it.
        // Without this, a mag pooled by a client that wasn't yet the owner stays in-use on other
        // clients and their pool leaks until it runs dry.
        RequestSerialization();
    }

    public int GetMaxAmmo()
    {
        if (magPool != null)
        {
            return magPool.GetMaxAmmo();
        }

        return 1;
    }

    public void SetHeld(bool held)
    {
        isHeld = held;
        if (held)
        {
            dropTime = 0f;
        }
    }

    public void Pickup()
    {
        isHeld = true;
        dropTime = 0f;
        ApplyVisualState();
    }

    public void Drop()
    {
        isHeld = false;
        dropTime = Time.time;
        ApplyVisualState();
        SendCustomEventDelayedSeconds(nameof(DespawnIfExpired), lifetimeAfterNonInteraction);
    }

    public bool IsHeld()
    {
        return isHeld;
    }

    public void SetSocketed(bool socketed)
    {
        isSocketed = socketed;
        syncedSocketed = socketed;
        if (socketed)
        {
            syncedPickupRootActive = false;
            syncedPickupVisualActive = false;

            if (magPickupRoot != null)
            {
                magPickupRoot.SetActive(true);
            }
            if (magPickupVisual != null)
            {
                magPickupVisual.SetActive(false);
            }

            SetPickupUseGravity(false);
            SetPickupKinematic(true);
            // ponytail: see UCS_MagSocket — collisions stay on so the mag remains grabbable.
            SetPickupDetectCollisions(true);

            if (Networking.IsOwner(gameObject))
            {
                RequestSerialization();
            }
        }
        else
        {
            SetPickupUseGravity(true);
            SetPickupKinematic(false);
            SetPickupDetectCollisions(true);
            ApplyVisualState();
        }
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public bool IsInUse()
    {
        return isInUse;
    }

    public void SetInUse(bool inUse)
    {
        isInUse = inUse;
        syncedInUse = inUse;

        if (inUse)
        {
            ApplyVisualState();
        }
        else
        {
            if (magPickupRoot != null)
            {
                magPickupRoot.SetActive(false);
            }
            if (magPickupVisual != null)
            {
                magPickupVisual.SetActive(false);
            }
        }

        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public void SetSocket(UCS_MagSocket socket)
    {
        currentSocket = socket;
        SetSocketed(socket != null);
    }

    public void ClearSocket()
    {
        currentSocket = null;
        SetSocketed(false);
    }

    public UCS_MagSocket GetSocket()
    {
        return currentSocket;
    }

    public void SetPickupRootActive(bool active)
    {
        syncedPickupRootActive = active;
        if (magPickupRoot != null)
        {
            magPickupRoot.SetActive(active);
        }
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
        int ownerId = -1;
        var owner = Networking.GetOwner(gameObject);
        if (owner != null) ownerId = owner.playerId;

        bool pickupRootActive = (magPickupRoot != null ? magPickupRoot.activeSelf : false);
        bool pickupVisualActive = (magPickupVisual != null ? magPickupVisual.activeSelf : false);
        string messageKey = $"magId={magId}:owner={ownerId}:actualRoot={pickupRootActive}:actualVisual={pickupVisualActive}:isSocketed={isSocketed}";
        if (lastLoggedMagState != messageKey)
        {
            lastLoggedMagState = messageKey;
            //Debug.Log($"[UCS_Mag] SetPickupRootActive magId={magId} active={active} owner={ownerId}");
        }
    }

    public Transform GetPickupRootTransform()
    {
        if (magPickupRoot != null)
        {
            return magPickupRoot.transform;
        }

        return transform;
    }

    public UCS_MagPickup GetMagPickup()
    {
        if (cachedMagPickup != null) return cachedMagPickup;
        if (magPickupRoot != null)
        {
            cachedMagPickup = magPickupRoot.GetComponent<UCS_MagPickup>();
            if (cachedMagPickup != null) return cachedMagPickup;
        }
        cachedMagPickup = GetComponentInChildren<UCS_MagPickup>(true);
        return cachedMagPickup;
    }

    public Rigidbody GetPickupRigidbody()
    {
        if (cachedMagRigidbody != null)
        {
            return cachedMagRigidbody;
        }

        if (magPickupRoot != null)
        {
            cachedMagRigidbody = magPickupRoot.GetComponent<Rigidbody>();
            if (cachedMagRigidbody != null) return cachedMagRigidbody;
            // fallback: search children
            cachedMagRigidbody = magPickupRoot.GetComponentInChildren<Rigidbody>();
            return cachedMagRigidbody;
        }

        cachedMagRigidbody = GetComponent<Rigidbody>();
        return cachedMagRigidbody;
    }

    // VRCObjectSync owns the rigidbody's kinematic/gravity state and reverts any direct write to
    // Rigidbody.isKinematic / .useGravity every update (see ClientSimPositionSyncedHelperBase:
    // "Rigidbody.isKinematic was set outside of VRCObjectSync.SetKinematic method!"). Its state is
    // also network-synced, which is why direct writes only visibly lost with real remote clients.
    // Everything below must go through the sync when one is present.
    private VRCObjectSync GetPickupObjectSync()
    {
        if (cachedMagObjectSync != null)
        {
            return cachedMagObjectSync;
        }

        Rigidbody rb = GetPickupRigidbody();
        if (rb != null)
        {
            cachedMagObjectSync = rb.GetComponent<VRCObjectSync>();
        }

        return cachedMagObjectSync;
    }

    public void SetPickupUseGravity(bool useGravity)
    {
        Rigidbody rb = GetPickupRigidbody();
        if (rb == null)
        {
            return;
        }

        VRCObjectSync sync = GetPickupObjectSync();
        // ponytail: rb.useGravity mirrors the sync's own state, so it doubles as the "already set?"
        // check. Guarding keeps the socket's repeated re-asserts free instead of re-serializing.
        if (rb.useGravity != useGravity)
        {
            if (sync != null)
            {
                sync.SetGravity(useGravity);
            }
            else
            {
                rb.useGravity = useGravity;
            }
        }

        if (!useGravity && !rb.isKinematic)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }

    public void SetPickupKinematic(bool kinematic)
    {
        Rigidbody rb = GetPickupRigidbody();
        if (rb == null)
        {
            return;
        }

        if (kinematic && !rb.isKinematic)
        {
            // Zero the velocity before it goes kinematic; Unity ignores the write afterwards.
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (rb.isKinematic == kinematic)
        {
            return;
        }

        VRCObjectSync sync = GetPickupObjectSync();
        if (sync != null)
        {
            sync.SetKinematic(kinematic);
        }
        else
        {
            rb.isKinematic = kinematic;
        }
    }

    public void SetPickupDetectCollisions(bool detect)
    {
        Rigidbody rb = GetPickupRigidbody();
        if (rb != null)
        {
            rb.detectCollisions = detect;
        }

        // ponytail: colliders stay enabled on purpose. Disabling them made the socketed mag
        // ungrabbable (VRC_Pickup needs a live collider to be targeted). rb.detectCollisions=false
        // already stops physics contacts while overlap/raycast queries still hit.
    }

    // Local-only re-assert of the socketed rigidbody state. Deliberately does NOT call
    // RequestSerialization. Called from UCS_MagPickup.OnOwnershipTransferred (and the socket's
    // bounded delayed re-assert) at the moment the pickup child's ownership lands, when its
    // VRCObjectSync switches to local-authority and the kinematic state finally sticks.
    public void ReassertSocketedPhysics()
    {
        SetPickupUseGravity(false);
        SetPickupKinematic(true);
        SetPickupDetectCollisions(true);
    }

    public void SetPickupVisualVisible(bool visible)
    {
        syncedPickupVisualActive = visible;
        if (magPickupVisual != null)
        {
            magPickupVisual.SetActive(visible);
        }
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
        int ownerId = -1;
        var owner = Networking.GetOwner(gameObject);
        if (owner != null) ownerId = owner.playerId;

        bool pickupRootActive = (magPickupRoot != null ? magPickupRoot.activeSelf : false);
        bool pickupVisualActive = (magPickupVisual != null ? magPickupVisual.activeSelf : false);
        string messageKey = $"magId={magId}:owner={ownerId}:actualRoot={pickupRootActive}:actualVisual={pickupVisualActive}:isSocketed={isSocketed}";
        if (lastLoggedMagState != messageKey)
        {
            lastLoggedMagState = messageKey;
            //Debug.Log($"[UCS_Mag] SetPickupVisualVisible magId={magId} visible={visible} owner={ownerId}");
        }
    }

    // When the mag is in the world or in a player's hand: enable pickup root and visual.
    // When socketed in a gun: visual should be disabled but pickup root remains enabled so the player can remove it.
    public void SetWorldVisible(bool visible)
    {
        if (visible)
        {
            SetPickupRootActive(true);
            SetPickupVisualVisible(true);
        }
        else
        {
            // Visible == false indicates a socketed state: keep pickup active but hide visual.
            SetPickupRootActive(true);
            SetPickupVisualVisible(false);
        }
    }

    private void ApplyVisualState()
    {
        if (!isInUse)
        {
            SetPickupRootActive(false);
            SetPickupVisualVisible(false);
            return;
        }

        if (isSocketed)
        {
            // socketed: hide visual, keep pickup enabled
            SetPickupRootActive(Networking.IsOwner(gameObject));
            SetPickupVisualVisible(false);
        }
        else if (isHeld)
        {
            // in-hand: enable pickup + visual
            SetPickupRootActive(true);
            SetPickupVisualVisible(true);
        }
        else
        {
            // dropped in world: enable pickup + visual
            SetPickupRootActive(true);
            SetPickupVisualVisible(true);
        }
    }

    public bool IsSocketed()
    {
        return isSocketed;
    }

    public void ResetForPool()
    {
        magId = -1;
        currentAmmo = GetMaxAmmo();
        isHeld = false;
        isSocketed = false;
        currentSocket = null;
        syncedSocketed = false;
        isInUse = false;
        syncedInUse = false;
        sourceBelt = null;
        dropTime = 0f;
        returnToPoolOnDrop = null;
        returnHolsterPoint = null;
        returnHolsterDistance = 0f;
        // Reset visuals: disable pickup root and visual by default so pooled mags aren't interactable/visible until spawned.
        SetPickupRootActive(false);
        SetPickupVisualVisible(false);
        // Restore pickup rigidbody to default physics state
        SetPickupUseGravity(true);
        SetPickupKinematic(false);
        SetPickupDetectCollisions(true);
        RequestSerialization();
    }

    private void returnToPool()
    {
        ResetForPool();
        if (magPool != null)
        {
            magPool.ReturnMagToPool(gameObject);
        }
    }

    // Schedule a delayed cleanup for a mag that was placed in the world without going through a
    // normal pickup/drop cycle (e.g. a mag swapped out by inserting a fresh one). Uses the actual
    // pickup's synced held state rather than the local isHeld flag, which EjectMag leaves true even
    // when the mag is abandoned on the ground, so the mag still gets returned instead of leaking.
    public void ScheduleCleanupIfAbandoned()
    {
        SendCustomEventDelayedSeconds(nameof(ReturnToPoolIfAbandoned), lifetimeAfterNonInteraction);
    }

    public void ReturnToPoolIfAbandoned()
    {
        if (isSocketed || !isInUse)
        {
            return;
        }

        UCS_MagPickup pickup = GetMagPickup();
        if (pickup == null || pickup.IsPickupHeld())
        {
            // Can't verify, or it's actually in someone's hand — leave it to the normal drop path.
            return;
        }

        returnToPool();
    }

    public void DespawnIfExpired()
    {
        if (isHeld || isSocketed)
        {
            return;
        }

        if (dropTime <= 0f)
        {
            return;
        }

        if (Time.time - dropTime >= lifetimeAfterNonInteraction)
        {
            // return to pool and clear visuals
            returnToPool();
        }
    }

    public void MarkDropped()
    {
        isHeld = false;
        dropTime = Time.time;
        SendCustomEventDelayedSeconds(nameof(DespawnIfExpired), lifetimeAfterNonInteraction);
        if (returnToPoolOnDrop != null)
        {
            bool isStillNearHolster = true;
            if (returnHolsterPoint != null)
            {
                isStillNearHolster = Vector3.Distance(GetPickupRootTransform().position, returnHolsterPoint.position) <= returnHolsterDistance;
            }

            UCS_MagPool pool = returnToPoolOnDrop;
            returnToPoolOnDrop = null;
            returnHolsterPoint = null;
            returnHolsterDistance = 0f;

            if (isStillNearHolster && pool != null)
            {
                pool.ReturnMagToPool(this);
            }
        }
    }

    public void SetReturnToPool(UCS_MagPool pool, Transform holsterPoint, float maxDistance)
    {
        returnToPoolOnDrop = pool;
        returnHolsterPoint = holsterPoint;
        returnHolsterDistance = maxDistance;
    }

    public void ClearReturnToPool()
    {
        returnToPoolOnDrop = null;
        returnHolsterPoint = null;
        returnHolsterDistance = 0f;
    }
}
