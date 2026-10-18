
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.UdonNetworkCalling;
using VRC.Udon.Common.Interfaces;

/// <summary>
/// Bulk pre-order object pool - the replacement for the old BetterObjectPool /
/// BulkOrderObjectPool. A helper module that loadout systems, dispensers (and anything else needing to reserve
/// items in bulk) talk to. Named for its two defining properties: it fulfills claims in bulk rather
/// than one at a time, and it has an explicit pre-order/reserve phase distinct from fulfillment.
///
/// WHY IT DIFFERS FROM THE OLD POOL
/// The old pool's fatal flaw: every requesting player called Networking.SetOwner on the pool itself
/// before writing to the shared claim arrays, so concurrent requests raced each other and could
/// silently double-claim the same slot (whichever write serialized last won, with no reconciliation).
///
/// New design: ownership of the pool NEVER moves. Every synced array is written by exactly one entity
/// (the current owner, master by convention). Requests are routed to whoever owns the pool via
/// parameterized [NetworkCallable] custom network events, the owner resolves each request serially
/// (Udon events on one instance execute one at a time, which removes the race by construction),
/// writes the result once, and responds directly to the specific requester. Nothing but the pool
/// itself ever calls SetOwner on the pool object, so ownership naturally tracks master through a
/// master change. Manual sync replicates the claim arrays to every client on each
/// RequestSerialization - not just the current owner - so a newly-promoted master already has an
/// accurate picture the moment it takes over. The one gap (a request in flight at the exact moment of
/// a handover can be dropped) is handled by a client-side timeout-and-retry, not at the pool level.
///
/// PHASES
///   1. Pre-order (reserve): a player claims up to N free slots for themselves; objects stay hidden.
///      Claiming is idempotent - it tops the player's reservation up to N, so retries never over-grant.
///   2. Fulfill: activate every reserved object and hand each one's ownership to its claimant (the
///      same final step the old FulfillAllPreOrders did with Networking.SetOwner).
///
/// A claim is keyed by playerId only - there is no separate "script claim" channel anymore, because
/// every reserved item ultimately belongs to the player who will own it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class BulkPreOrderPool : UdonSharpBehaviour
{
    [Header("Pool")]
    [Tooltip("The objects this pool manages. They start inactive. Array order is the slot order used by every claim.")]
    [SerializeField] private GameObject[] managedObjects = new GameObject[0];

    [Header("Result Notifications")]
    [Tooltip("Optional behaviours (e.g. a loadout manager) notified with notifyEventName whenever a reservation / fulfillment / cancel result lands for the LOCAL player. They then query this pool.")]
    [SerializeField] private UdonBehaviour[] resultReceivers = new UdonBehaviour[0];

    [Tooltip("Public method name called on resultReceivers when a result lands for the local player. No arguments - receivers query the pool for details.")]
    [SerializeField] private string notifyEventName = "_OnPoolResult";

    [Header("Retry")]
    [Tooltip("How long a requester waits for a response before resending its request (covers a request dropped during a master handover).")]
    [SerializeField] private float retryDelaySeconds = 1.0f;

    [Tooltip("How many times a requester retries before giving up.")]
    [SerializeField] private int maxRetries = 3;

    [Header("Debug")]
    [SerializeField] private bool debugLogging;

    // Parallel to managedObjects. claimOwnerIds[i] == 0 means slot i is free; otherwise it holds the
    // playerId that reserved it. isActive[i] is true once the slot has been fulfilled (activated).
    // Invariant: when claimOwnerIds[i] == 0, isActive[i] is false.
    [UdonSynced] private int[] claimOwnerIds = new int[0];
    [UdonSynced] private bool[] isActive = new bool[0];

    // --- requester-side bookkeeping (one outstanding request at a time) ---
    private int pendingRequesterId = -1;   // local playerId of the outstanding request, -1 = none
    private int pendingAmount;
    private int pendingRetriesLeft;
    private bool pendingAnswered;

    // --- last result landed for the local player (for resultReceivers to read) ---
    public const int EVENT_NONE = 0;
    public const int EVENT_RESERVED = 1;
    public const int EVENT_FULFILLED = 2;
    public const int EVENT_CANCELLED = 3;
    public int lastEventType = EVENT_NONE;
    public int lastEventCount;         // slots reserved / fulfilled / cancelled
    public int lastEventFullyReserved; // 1 if a reservation request was fully satisfied, else 0

    void Start()
    {
        int size = managedObjects.Length;
        if (claimOwnerIds.Length != size) claimOwnerIds = new int[size];
        if (isActive.Length != size) isActive = new bool[size];

        // Pool starts empty: every slot free and every managed object hidden.
        for (int i = 0; i < size; i++)
        {
            claimOwnerIds[i] = 0;
            isActive[i] = false;
            if (managedObjects[i] != null)
                managedObjects[i].SetActive(false);
        }

        if (Networking.IsOwner(gameObject))
            RequestSerialization();
    }

    #region Request / Reserve (Phase 1)

    /// <summary>
    /// Reserve up to <paramref name="amount"/> free slots for <paramref name="requesterId"/>
    /// (typically the local player). Idempotent: if that player already has R slots reserved, this
    /// only tops them up to amount - so a retried request can never double-claim. Safe to call from
    /// any client; if this client isn't the pool owner the request is forwarded to the owner, which
    /// resolves everything serially and responds with <see cref="_OnItemsReserved"/>.
    /// </summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _RequestItems(int requesterId, int amount)
    {
        if (amount <= 0) return;

        if (!Networking.IsOwner(gameObject))
        {
            // Remember it locally so we can retry if the owner (master) hands over mid-request.
            if (requesterId == Networking.LocalPlayer.playerId)
                BeginOutstandingRequest(requesterId, amount);

            if (debugLogging)
                Debug.Log($"[{name}] forwarding reserve request: player {requesterId} x {amount} -> owner");

            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestItems), requesterId, amount);
            return;
        }

        // --- owner-only from here: single writer, no race possible ---
        ResolveRequest(requesterId, amount);
    }

    /// <summary>
    /// Owner-side resolution. Tops the requester's reservation up to <paramref name="amount"/>
    /// (never double-counts) and responds to the requester with the actual result.
    /// </summary>
    void ResolveRequest(int requesterId, int amount)
    {
        int already = CountNonActiveClaimsFor(requesterId);
        int extraNeeded = amount - already;
        if (extraNeeded < 0) extraNeeded = 0;

        int granted = 0;
        if (extraNeeded > 0)
        {
            int size = managedObjects.Length;
            int[] newClaims = Clone(claimOwnerIds);
            for (int i = 0; i < size && granted < extraNeeded; i++)
            {
                if (newClaims[i] == 0)
                {
                    newClaims[i] = requesterId;
                    granted++;
                }
            }
            claimOwnerIds = newClaims;
            RequestSerialization();
        }

        int totalReserved = already + granted;
        int fullyReserved = totalReserved >= amount ? 1 : 0;

        if (debugLogging)
            Debug.Log($"[{name}] reserved {totalReserved}/{amount} for player {requesterId}");

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_OnItemsReserved), requesterId, totalReserved, fullyReserved);
    }

    /// <summary>Response sent to the requester (all clients receive it; non-requesters drop it).</summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _OnItemsReserved(int requesterId, int reservedCount, int fullyReserved)
    {
        if (requesterId != Networking.LocalPlayer.playerId) return; // not for us

        if (debugLogging)
            Debug.Log($"[{name}] reservation result for local player: {reservedCount} (full={fullyReserved})");

        ClearOutstandingRequest();
        lastEventType = EVENT_RESERVED;
        lastEventCount = reservedCount;
        lastEventFullyReserved = fullyReserved;
        NotifyReceivers();
    }

    #endregion

    #region Cancel Reservation

    /// <summary>Release all of <paramref name="requesterId"/>'s reservations that have NOT been fulfilled yet.</summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _CancelPreOrders(int requesterId)
    {
        if (requesterId == Networking.LocalPlayer.playerId)
            ClearOutstandingRequest(); // we no longer want the outstanding reservation

        if (!Networking.IsOwner(gameObject))
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_CancelPreOrders), requesterId);
            return;
        }

        int cancelled = 0;
        int size = managedObjects.Length;
        int[] newClaims = Clone(claimOwnerIds);
        for (int i = 0; i < size; i++)
        {
            if (newClaims[i] == requesterId && !isActive[i])
            {
                newClaims[i] = 0;
                cancelled++;
            }
        }
        if (cancelled > 0)
        {
            claimOwnerIds = newClaims;
            RequestSerialization();
        }

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_OnPreOrdersCancelled), requesterId, cancelled);
    }

    /// <summary>Response confirming how many reservations were released.</summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _OnPreOrdersCancelled(int requesterId, int cancelledCount)
    {
        if (requesterId != Networking.LocalPlayer.playerId) return;

        lastEventType = EVENT_CANCELLED;
        lastEventCount = cancelledCount;
        NotifyReceivers();
    }

    #endregion

    #region Fulfill (Phase 2)

    /// <summary>
    /// Activate every reserved object and hand each one's ownership to its claimant. Called once at
    /// match/loadout start (by the master / game manager). Runs on the pool owner; forwarded there if
    /// called elsewhere. Each claimant is then told how many of its pre-orders were fulfilled.
    /// </summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _FulfillAllPreOrders()
    {
        if (!Networking.IsOwner(gameObject))
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_FulfillAllPreOrders));
            return;
        }

        int size = managedObjects.Length;

        // Collect the unique claimants and how many slots each one is getting, as we activate.
        int[] claimants = new int[size];
        int[] claimantCounts = new int[size];
        int claimantCount = 0;

        bool[] newActive = Clone(isActive);
        for (int i = 0; i < size; i++)
        {
            int claimantId = claimOwnerIds[i];
            if (claimantId == 0) continue; // free slot, nothing to fulfill

            // Negotiation is owner-authoritative, but the resulting item ends up player-owned:
            // hand ownership to the actual claimant (mirrors the old FulfillAllPreOrders SetOwner step).
            VRCPlayerApi claimant = VRCPlayerApi.GetPlayerById(claimantId);
            if (Utilities.IsValid(claimant))
                Networking.SetOwner(claimant, managedObjects[i]);

            newActive[i] = true;
            if (managedObjects[i] != null)
                managedObjects[i].SetActive(true);

            // tally this claimant
            bool found = false;
            for (int k = 0; k < claimantCount; k++)
            {
                if (claimants[k] == claimantId)
                {
                    claimantCounts[k]++;
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                claimants[claimantCount] = claimantId;
                claimantCounts[claimantCount] = 1;
                claimantCount++;
            }
        }

        if (claimantCount > 0)
        {
            isActive = newActive;
            RequestSerialization();
        }

        // Respond to each claimant directly.
        for (int k = 0; k < claimantCount; k++)
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(_OnPreOrdersFulfilled), claimants[k], claimantCounts[k]);
    }

    /// <summary>Response telling a claimant how many of its reserved objects were fulfilled/activated.</summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _OnPreOrdersFulfilled(int requesterId, int fulfilledCount)
    {
        if (requesterId != Networking.LocalPlayer.playerId) return;

        if (debugLogging)
            Debug.Log($"[{name}] fulfilled {fulfilledCount} pre-orders for local player");

        lastEventType = EVENT_FULFILLED;
        lastEventCount = fulfilledCount;
        NotifyReceivers();
    }

    #endregion

    #region Return / Reset

    /// <summary>
    /// Return one fulfilled object to the pool (deactivate + free its slot). Only the player who
    /// claimed the slot may return it. Runs on the owner; forwarded there if called elsewhere.
    /// </summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _ReturnObject(int requesterId, int slotIndex)
    {
        if (!Networking.IsOwner(gameObject))
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_ReturnObject), requesterId, slotIndex);
            return;
        }

        int size = managedObjects.Length;
        if (slotIndex < 0 || slotIndex >= size) return;
        if (claimOwnerIds[slotIndex] != requesterId) return; // only the claimant can return it

        FreeSlotAtIndex(slotIndex);
    }

    /// <summary>Return everything to the pool (full round/match reset). Runs on the owner.</summary>
    [NetworkCallable(maxEventsPerSecond: 100)]
    public void _ReturnAllToPool()
    {
        if (!Networking.IsOwner(gameObject))
        {
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_ReturnAllToPool));
            return;
        }

        int size = managedObjects.Length;
        int[] newClaims = new int[size];
        bool[] newActive = new bool[size];
        for (int i = 0; i < size; i++)
        {
            newClaims[i] = 0;
            newActive[i] = false;
            if (managedObjects[i] != null)
                managedObjects[i].SetActive(false);
        }
        claimOwnerIds = newClaims;
        isActive = newActive;
        RequestSerialization();
    }

    #endregion

    #region Query API (for resultReceivers, loadout managers and dispensers)

    public int GetObjectCount()
    {
        return managedObjects.Length;
    }

    /// <summary>The managed object in slot <paramref name="slotIndex"/>, whatever its claim state (null if out of range).</summary>
    public GameObject GetObjectAt(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < managedObjects.Length ? managedObjects[slotIndex] : null;
    }

    public int GetFreeCount()
    {
        int count = 0;
        for (int i = 0; i < claimOwnerIds.Length; i++)
            if (claimOwnerIds[i] == 0) count++;
        return count;
    }

    /// <summary>How many slots this player currently has reserved but not yet fulfilled.</summary>
    public int GetReservedCountForPlayer(int playerId)
    {
        int count = 0;
        for (int i = 0; i < claimOwnerIds.Length; i++)
            if (claimOwnerIds[i] == playerId && !isActive[i]) count++;
        return count;
    }

    /// <summary>How many slots this player currently has active (fulfilled).</summary>
    public int GetActiveCountForPlayer(int playerId)
    {
        int count = 0;
        for (int i = 0; i < claimOwnerIds.Length; i++)
            if (claimOwnerIds[i] == playerId && isActive[i]) count++;
        return count;
    }

    /// <summary>The objects this player has reserved but not yet fulfilled, in slot order.</summary>
    public GameObject[] GetReservedObjectsForPlayer(int playerId)
    {
        return CollectObjectsForPlayer(playerId, false);
    }

    /// <summary>The objects this player currently has active (fulfilled), in slot order.</summary>
    public GameObject[] GetActiveObjectsForPlayer(int playerId)
    {
        return CollectObjectsForPlayer(playerId, true);
    }

    /// <summary>playerId that claimed slot <paramref name="slotIndex"/> (0 = free / out of range). Needed by <see cref="_ReturnObject"/> callers that aren't the claimant, e.g. a grenade thrown by someone else.</summary>
    public int GetClaimantAt(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < claimOwnerIds.Length ? claimOwnerIds[slotIndex] : 0;
    }

    public bool IsSlotFree(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < claimOwnerIds.Length && claimOwnerIds[slotIndex] == 0;
    }

    /// <summary>Slot index of a managed object, or -1 if it isn't managed by this pool.</summary>
    public int GetSlotIndexOf(GameObject obj)
    {
        for (int i = 0; i < managedObjects.Length; i++)
            if (managedObjects[i] == obj) return i;
        return -1;
    }

    #endregion

    #region Helpers

    GameObject[] CollectObjectsForPlayer(int playerId, bool activeOnly)
    {
        int size = managedObjects.Length;
        int count = 0;
        for (int i = 0; i < size; i++)
        {
            if (claimOwnerIds[i] == playerId && isActive[i] == activeOnly)
                count++;
        }

        GameObject[] result = new GameObject[count];
        int write = 0;
        for (int i = 0; i < size; i++)
        {
            if (claimOwnerIds[i] == playerId && isActive[i] == activeOnly)
            {
                result[write] = managedObjects[i];
                write++;
            }
        }
        return result;
    }

    int CountNonActiveClaimsFor(int playerId)
    {
        int count = 0;
        for (int i = 0; i < claimOwnerIds.Length; i++)
            if (claimOwnerIds[i] == playerId && !isActive[i]) count++;
        return count;
    }

    void FreeSlotAtIndex(int slotIndex)
    {
        int[] newClaims = Clone(claimOwnerIds);
        bool[] newActive = Clone(isActive);
        newClaims[slotIndex] = 0;
        newActive[slotIndex] = false;
        if (managedObjects[slotIndex] != null)
            managedObjects[slotIndex].SetActive(false);
        claimOwnerIds = newClaims;
        isActive = newActive;
        RequestSerialization();
    }

    int[] Clone(int[] src)
    {
        int[] copy = new int[src.Length];
        for (int i = 0; i < src.Length; i++) copy[i] = src[i];
        return copy;
    }

    bool[] Clone(bool[] src)
    {
        bool[] copy = new bool[src.Length];
        for (int i = 0; i < src.Length; i++) copy[i] = src[i];
        return copy;
    }

    void BeginOutstandingRequest(int requesterId, int amount)
    {
        pendingRequesterId = requesterId;
        pendingAmount = amount;
        pendingRetriesLeft = maxRetries;
        pendingAnswered = false;

        if (pendingRetriesLeft > 0)
            SendCustomEventDelayedSeconds(nameof(_RetryOutstandingRequest), retryDelaySeconds);
    }

    void ClearOutstandingRequest()
    {
        pendingAnswered = true;   // stops any already-scheduled retry from resending
        pendingRequesterId = -1;
    }

    /// <summary>Local-only retry tick: resend the outstanding request if no response arrived.</summary>
    public void _RetryOutstandingRequest()
    {
        if (pendingAnswered || pendingRequesterId < 0) return; // answered or cancelled meanwhile

        if (pendingRetriesLeft <= 0)
        {
            if (debugLogging)
                Debug.LogWarning($"[{name}] gave up on reserve request for player {pendingRequesterId} after {maxRetries} retries");
            ClearOutstandingRequest();
            return;
        }

        pendingRetriesLeft--;
        if (debugLogging)
            Debug.Log($"[{name}] retrying reserve request for player {pendingRequesterId} x {pendingAmount} ({pendingRetriesLeft} left)");

        SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(_RequestItems), pendingRequesterId, pendingAmount);

        if (pendingRetriesLeft > 0)
            SendCustomEventDelayedSeconds(nameof(_RetryOutstandingRequest), retryDelaySeconds);
    }

    void NotifyReceivers()
    {
        for (int i = 0; i < resultReceivers.Length; i++)
        {
            if (resultReceivers[i] != null)
                resultReceivers[i].SendCustomEvent(notifyEventName);
        }
    }

    #endregion

    #region VRC Overrides

    public override void OnDeserialization()
    {
        // Apply activation state on every client (and keep a freshly-promoted master's view correct).
        for (int i = 0; i < managedObjects.Length; i++)
        {
            if (managedObjects[i] != null)
                managedObjects[i].SetActive(isActive[i]);
        }
    }

    #endregion
}
