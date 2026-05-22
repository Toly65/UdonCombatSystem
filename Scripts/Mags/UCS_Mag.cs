
using UdonSharp;
using UnityEngine;
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
            Debug.Log($"[UCS_Mag] OnDeserialization magId={magId} ownerId={ownerId} syncedSocketed={syncedSocketed} syncedPickupRootActive={syncedPickupRootActive} syncedPickupVisualActive={syncedPickupVisualActive} magPickupRootActive={pickupRootActive} magPickupVisualActive={pickupVisualActive} isSocketed={isSocketed}");
        }
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

            if (Networking.IsOwner(gameObject))
            {
                RequestSerialization();
            }
        }
        else
        {
            SetPickupUseGravity(true);
            SetPickupKinematic(false);
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
            Debug.Log($"[UCS_Mag] SetPickupRootActive magId={magId} active={active} owner={ownerId}");
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
        if (magPickupRoot != null)
        {
            Rigidbody rb = magPickupRoot.GetComponent<Rigidbody>();
            if (rb != null) return rb;
            // fallback: search children
            return magPickupRoot.GetComponentInChildren<Rigidbody>();
        }

        return GetComponent<Rigidbody>();
    }

    public void SetPickupUseGravity(bool useGravity)
    {
        Rigidbody rb = GetPickupRigidbody();
        if (rb != null)
        {
            rb.useGravity = useGravity;
            if (!useGravity)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
    }

    public void SetPickupKinematic(bool kinematic)
    {
        Rigidbody rb = GetPickupRigidbody();
        if (rb != null)
        {
            rb.isKinematic = kinematic;
            if (kinematic)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }
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
            Debug.Log($"[UCS_Mag] SetPickupVisualVisible magId={magId} visible={visible} owner={ownerId}");
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
