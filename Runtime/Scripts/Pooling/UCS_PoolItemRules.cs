using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

// Put on a BulkPreOrderPool's GameObject. Automatic returns and pickup rules for every item in that pool:
//  - return an item that nobody has held, stowed or moved for a while (replaces ActiveItem's timer)
//  - return a player's items (and free their reservations) when they leave
//  - when someone other than the dispenser picks an item up, either hand ownership to them or keep it with the dispenser
//  - let only the claimant (the player the pool dispensed it to) pick it up
// Returns run on the pool's owner only, so each item is returned once. The pickup scripts
// (UCS_PickupEventTransferer, UCS_GrenadePickup) find this component in their parents; nothing needs wiring.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_PoolItemRules : UdonSharpBehaviour
{
    [Header("Idle return")]
    [SerializeField] private bool returnWhenIdle = true;
    [Tooltip("Seconds an item must sit unheld, unstowed and unmoved before it goes back to the pool.")]
    [SerializeField] private float idleSeconds = 300f;
    [Tooltip("How often idle items are checked. Idle time is counted in steps of this.")]
    [SerializeField] private float checkSeconds = 5f;
    [Tooltip("An item that moves further than this between checks is not idle.")]
    [SerializeField] private float movedMetres = 0.05f;

    [Header("Player leaving")]
    [SerializeField] private bool returnOnPlayerLeft = true;

    [Header("Pickup")]
    [Tooltip("The player an item was dispensed to owns it. If someone else picks it up, on = ownership moves to them; off = it stays with the dispenser.")]
    [SerializeField] private bool swapOwnerOnPickup = true;
    [Tooltip("Only the player the pool dispensed the item to can pick it up; anyone else is made to drop it.")]
    [SerializeField] private bool ownerOnlyPickup = false;

    private BulkPreOrderPool pool;
    private VRC_Pickup[] pickups;   // first pickup of each slot, to read held state and position
    private int[] idleSteps;
    private Vector3[] lastPos;

    private void Start()
    {
        pool = GetComponent<BulkPreOrderPool>();
        int n = pool.GetObjectCount();
        pickups = new VRC_Pickup[n];
        idleSteps = new int[n];
        lastPos = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            GameObject obj = pool.GetObjectAt(i);
            if (obj != null) pickups[i] = obj.GetComponentInChildren<VRC_Pickup>(true);
        }

        if (returnWhenIdle) SendCustomEventDelayedSeconds(nameof(_IdleCheck), checkSeconds);
    }

    // ---- pickup rules, called by the pickup scripts ----
    /// <summary>
    /// Whether the local player should take ownership of this item as they pick it up. The dispenser (claimant)
    /// always does, so they get it back after someone else held it; anyone else only if swapOwnerOnPickup is on.
    /// </summary>
    public bool ShouldTakeOwnership(GameObject pickupObject)
    {
        if (swapOwnerOnPickup) return true;
        int slot = SlotOf(pickupObject);
        if (slot < 0) return true;
        int claimant = pool.GetClaimantAt(slot);
        return claimant == 0 || claimant == Networking.LocalPlayer.playerId;
    }

    /// <summary>False if the local player may not pick this item up (owner-only pickup and they are not its claimant).</summary>
    public bool AllowPickup(GameObject pickupObject)
    {
        if (!ownerOnlyPickup) return true;
        int slot = SlotOf(pickupObject);
        if (slot < 0) return true;
        int claimant = pool.GetClaimantAt(slot);
        return claimant == 0 || claimant == Networking.LocalPlayer.playerId;
    }

    // The managed object is the item root, an ancestor of the pickup (or the pickup itself).
    private int SlotOf(GameObject pickupObject)
    {
        Transform t = pickupObject.transform;
        for (int depth = 0; t != null && depth < 4; depth++)
        {
            int slot = pool.GetSlotIndexOf(t.gameObject);
            if (slot >= 0) return slot;
            t = t.parent;
        }
        return -1;
    }

    // ---- idle return ----
    public void _IdleCheck()
    {
        SendCustomEventDelayedSeconds(nameof(_IdleCheck), checkSeconds);
        if (!Networking.IsOwner(pool.gameObject)) return;

        int limit = Mathf.Max(1, Mathf.CeilToInt(idleSeconds / Mathf.Max(0.1f, checkSeconds)));
        for (int i = 0; i < pickups.Length; i++)
        {
            GameObject obj = pool.GetObjectAt(i);
            // free slots and reserved-but-not-yet-fulfilled slots are not out in the world
            if (obj == null || !obj.activeSelf || pool.IsSlotFree(i) || pickups[i] == null)
            {
                idleSteps[i] = 0;
                continue;
            }

            Vector3 pos = pickups[i].transform.position;
            bool busy = pickups[i].IsHeld || IsStowed(obj) || IsLiveGrenade(obj) || Vector3.Distance(pos, lastPos[i]) > movedMetres;
            lastPos[i] = pos;
            if (busy) { idleSteps[i] = 0; continue; }

            idleSteps[i]++;
            if (idleSteps[i] >= limit)
            {
                idleSteps[i] = 0;
                ReturnSlot(i);
            }
        }
    }

    // ---- player leaving ----
    public override void OnPlayerLeft(VRCPlayerApi player)
    {
        if (!returnOnPlayerLeft || player == null || !Networking.IsOwner(pool.gameObject)) return;

        int id = player.playerId;
        pool._CancelPreOrders(id); // anything they reserved but never took
        for (int i = 0; i < pickups.Length; i++)
        {
            if (pool.GetClaimantAt(i) != id) continue;
            GameObject obj = pool.GetObjectAt(i);
            if (obj == null || !obj.activeSelf || IsLiveGrenade(obj)) continue; // live grenades return themselves
            ReturnSlot(i);
        }
    }

    private void ReturnSlot(int slot)
    {
        GameObject obj = pool.GetObjectAt(slot);
        // a stowed item stays locked to its holder's stow point and would be dragged back out of the pool
        StowableManager stow = obj.GetComponent<StowableManager>();
        if (stow != null) stow.ForceUnstowEverywhere();
        pool._ReturnObject(pool.GetClaimantAt(slot), slot);
    }

    private bool IsStowed(GameObject obj)
    {
        StowableManager stow = obj.GetComponent<StowableManager>();
        return stow != null && stow.IsStowedSynced;
    }

    private bool IsLiveGrenade(GameObject obj)
    {
        UCS_Grenade grenade = obj.GetComponent<UCS_Grenade>();
        return grenade != null && grenade.GetState() >= UCS_Grenade.StateLive;
    }
}
