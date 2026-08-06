using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;

// Which grips are currently held. Packed into a single synced int (bitmask) so the
// grip state broadcasts to every client as one value.
public enum UCS_GripState
{
    None = 0,
    PrimaryOnly = 1,
    SecondaryOnly = 2,
    Both = 3
}

// Manages two-handed grip states and a look-at rotation for the gun visual.
//
// Networking model:
//  - The grip state (which grips are held) is [UdonSynced] so every client reparents
//    the gun visual and locks/unlocks grips exactly like the holder.
//  - Ownership is requested from the grabbing player when the primary grip is picked
//    up; that player becomes the authority that serializes the grip state.
//  - Remote clients run the same deterministic look-at / socket logic from the synced
//    pickup transforms, so the held pose replicates instead of drifting.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_TwoHandedManager : UdonSharpBehaviour
{
    [Header("Grip Pickups")]
    [SerializeField] private VRC_Pickup primaryGripPickup;
    [SerializeField] private VRC_Pickup secondaryGripPickup;

    [Header("Grip Sockets")]
    [SerializeField] private Transform primaryGripPoint;

    [Header("Gun Visual")]
    // The visual root of the gun; reparented between grips at runtime.
    [SerializeField] private Transform gunVisual;
    // Local marker for the front grip on the gun mesh.
    [SerializeField] private Transform frontGripPoint;

    [Header("Two-Handed Look-At")]
    [SerializeField] private Vector2 rotationRange = new Vector2(60f, 60f);
    [SerializeField] private float followSpeed = 0.05f;

    // State
    private bool primaryHeld;
    private bool secondaryHeld;
    // Look-at smoothing
    private Vector3 followAngles;
    private Vector3 followVelocity;
    // Resting local pose of `gunVisual` relative to Primary Grip.
    // Used as the neutral pose when computing look-at offsets.
    private Vector3 gunVisualRestLocalPosition;
    private Quaternion gunVisualRestLocalRotation;
    private Transform primaryGripOriginalParent;
    private Transform secondaryGripOriginalParent;
    private Vector3 primaryGripPointLocalPosition;
    private Quaternion primaryGripPointLocalRotation;
    private Vector3 secondaryGripGunVisualLocalPosition;
    private Quaternion secondaryGripGunVisualLocalRotation;
    private Rigidbody primaryGripRigidbody;
    private VRCObjectSync primaryGripObjectSync;
    private bool primaryGripOriginalUseGravity;
    private bool primaryGripOriginalIsKinematic;
    private bool primaryGripOriginalDetectCollisions;
    private bool updateLoopActive;
    // Networking
    // Which grips are held, broadcast to every client. Written only by the owner.
    [UdonSynced] private int syncedGripState = (int)UCS_GripState.None;
    // True when the local player changed the grip state but ownership was still in
    // flight (SetOwner is async); serialization is flushed in OnOwnershipTransferred.
    private bool pendingSync;
    // Lifecycle
    void Start()
    {
        if (primaryGripPickup != null)
        {
            primaryGripOriginalParent = primaryGripPickup.transform.parent;
            primaryGripRigidbody = primaryGripPickup.GetComponent<Rigidbody>();

            if (primaryGripRigidbody != null)
            {
                primaryGripOriginalUseGravity = primaryGripRigidbody.useGravity;
                primaryGripOriginalIsKinematic = primaryGripRigidbody.isKinematic;
                primaryGripOriginalDetectCollisions = primaryGripRigidbody.detectCollisions;
            }

            if (primaryGripPoint != null)
            {
                primaryGripPointLocalPosition = primaryGripPickup.transform.InverseTransformPoint(primaryGripPoint.position);
                primaryGripPointLocalRotation = Quaternion.Inverse(primaryGripPickup.transform.rotation) * primaryGripPoint.rotation;
            }
        }

        if (secondaryGripPickup != null)
            secondaryGripOriginalParent = secondaryGripPickup.transform.parent;

        if (gunVisual != null && secondaryGripPickup != null)
        {
            secondaryGripGunVisualLocalPosition = gunVisual.InverseTransformPoint(secondaryGripPickup.transform.position);
            secondaryGripGunVisualLocalRotation = Quaternion.Inverse(gunVisual.rotation) * secondaryGripPickup.transform.rotation;
        }

        if (gunVisual != null && primaryGripPickup != null)
        {
            // Parent gun visual under Primary Grip while keeping world transform.
            gunVisual.SetParent(primaryGripPickup.transform, true);
            gunVisualRestLocalPosition = gunVisual.localPosition;
            gunVisualRestLocalRotation = gunVisual.localRotation;
        }

        // Disable secondary until primary is held locally.
        if (secondaryGripPickup != null)
            secondaryGripPickup.pickupable = false;

        updateLoopActive = false;
        UpdateGripPointParents(false, false);
        RefreshGripPickupability();

        // Sync the initial (empty) grip state so remote clients start consistent.
        if (Networking.IsOwner(gameObject))
            RequestSerialization();
    }

    void Update()
    {
        if (!updateLoopActive)
            return;

        // Pickup ownership transfer is async; re-evaluate grabbability every frame so
        // the owner can take the front grip the moment ownership settles.
        RefreshGripPickupability();

        // Two-handed look-at runs every frame while both grips are held.
        if (primaryHeld && secondaryHeld)
            ApplyTwoHandedRotation();
        else if (!primaryHeld && secondaryHeld)
            KeepPrimaryGripAtSocket();
    }

    public void PrimaryPickup()
    {
        // The player who grabs the primary grip becomes the authority for the grip
        // state. VRC auto-owns the primary pickup itself, but the manager's GameObject
        // may differ, so request ownership here to authorize serialization.
        if (Networking.LocalPlayer != null && !Networking.IsOwner(gameObject))
            Networking.SetOwner(Networking.LocalPlayer, gameObject);

        // Also take the secondary grip. If it stays owned by the master (or whoever
        // last touched it), that client is authoritative for its synced transform and
        // has no idea the gun is being held — so the front grip floats at its resting
        // spot instead of being snapped to the gun's front grip where the player can
        // actually grab it. Owning it lets THIS client's grip logic place it.
        if (secondaryGripPickup != null && !Networking.IsOwner(secondaryGripPickup.gameObject))
            Networking.SetOwner(Networking.LocalPlayer, secondaryGripPickup.gameObject);

        SetGripState(true, secondaryHeld);
    }

    public void PrimaryDrop()
    {
        SetGripState(false, secondaryHeld);
    }

    public void SecondaryPickup()
    {
        // Defensive: VRC auto-owns the front grip on grab, but that transfer is async.
        // Ensure this client is authoritative so the pickup transform stays put.
        if (secondaryGripPickup != null && !Networking.IsOwner(secondaryGripPickup.gameObject))
            Networking.SetOwner(Networking.LocalPlayer, secondaryGripPickup.gameObject);

        SetGripState(primaryHeld, true);
    }

    public void SecondaryDrop()
    {
        SetGripState(primaryHeld, false);
    }

    public override void OnDeserialization()
    {
        ApplyGripState(IsPrimaryFromState(syncedGripState), IsSecondaryFromState(syncedGripState));
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        if (player == null || player != Networking.LocalPlayer)
            return;

        // Ownership landed after picking up the gun: re-evaluate grabbability now
        // that ownership checks are valid, and flush any pending serialization.
        RefreshGripPickupability();
        TrySendSync();
    }

    // Local grip changes from pickup events. Only the holder's client runs this;
    // remote clients receive the state through OnDeserialization instead.
    private void SetGripState(bool isPrimaryHeld, bool isSecondaryHeld)
    {
        if (isPrimaryHeld == primaryHeld && isSecondaryHeld == secondaryHeld)
            return;

        ApplyGripState(isPrimaryHeld, isSecondaryHeld);

        // Broadcast the new grip state. Ownership may still be in flight (SetOwner
        // is async), so TrySendSync defers until OnOwnershipTransferred confirms it.
        int newState = ToGripState(isPrimaryHeld, isSecondaryHeld);
        if (newState != syncedGripState)
        {
            syncedGripState = newState;
            pendingSync = true;
            TrySendSync();
        }
    }

    // Applies a grip state to the runtime fields and the visual/parenting logic.
    // Used for local changes (SetGripState) and remote receives (OnDeserialization).
    private void ApplyGripState(bool isPrimaryHeld, bool isSecondaryHeld)
    {
        if (isPrimaryHeld == primaryHeld && isSecondaryHeld == secondaryHeld)
            return;

        bool wasPrimary = primaryHeld;
        bool wasSecondary = secondaryHeld;

        primaryHeld = isPrimaryHeld;
        secondaryHeld = isSecondaryHeld;

        OnGripStateChanged(wasPrimary, wasSecondary, primaryHeld, secondaryHeld);
        UpdateGripPointParents(primaryHeld, secondaryHeld);
        updateLoopActive = primaryHeld || secondaryHeld;
    }

    private int ToGripState(bool isPrimaryHeld, bool isSecondaryHeld)
    {
        return (isPrimaryHeld ? (int)UCS_GripState.PrimaryOnly : 0)
             | (isSecondaryHeld ? (int)UCS_GripState.SecondaryOnly : 0);
    }

    private bool IsPrimaryFromState(int state) { return (state & (int)UCS_GripState.PrimaryOnly) != 0; }

    private bool IsSecondaryFromState(int state) { return (state & (int)UCS_GripState.SecondaryOnly) != 0; }

    private void TrySendSync()
    {
        if (!pendingSync) return;
        if (Networking.LocalPlayer == null) return;
        if (!Networking.IsOwner(gameObject)) return;
        pendingSync = false;
        RequestSerialization();
    }

    // Public state accessors so other systems can query how the gun is being held.
    public bool IsPrimaryHeld() { return primaryHeld; }
    public bool IsSecondaryHeld() { return secondaryHeld; }
    public bool IsHeld() { return primaryHeld || secondaryHeld; }

    // Grip state machine
    private void OnGripStateChanged(bool wasPrimary, bool wasSecondary, bool isPrimary, bool isSecondary)
    {
        if (gunVisual != null)
        {
            if (isPrimary && isSecondary)
            {
                if (primaryGripPickup != null)
                    gunVisual.SetParent(primaryGripPickup.transform, true);
            }
            else if (isSecondary)
            {
                if (secondaryGripPickup != null)
                    gunVisual.SetParent(secondaryGripPickup.transform, true);
            }
            else if (isPrimary)
            {
                if (primaryGripPickup != null)
                {
                    gunVisual.SetParent(primaryGripPickup.transform, true);
                    gunVisual.localPosition = gunVisualRestLocalPosition;
                    gunVisual.localRotation = gunVisualRestLocalRotation;
                }
            }
            else if (primaryGripPickup != null)
            {
                gunVisual.SetParent(primaryGripPickup.transform, true);
                gunVisual.localPosition = gunVisualRestLocalPosition;
                gunVisual.localRotation = gunVisualRestLocalRotation;
            }
        }

        RefreshGripPickupability();

        // Reset look-at only when the gun is fully released.
        if (!isPrimary && !isSecondary)
        {
            followAngles = Vector3.zero;
            followVelocity = Vector3.zero;
        }
    }

    // Sets which grips the LOCAL player may grab.
    //  - The gun holder keeps the primary locked while held and exposes the front
    //    grip for the second hand / one-to-two-handed transitions.
    //  - Remote observers lock both grips while anyone is holding, so the gun can't
    //    be stolen out of someone's hands.
    private void RefreshGripPickupability()
    {
        if (Networking.LocalPlayer == null)
            return;

        if (primaryGripPickup != null)
        {
            bool localOwnsPrimary = Networking.IsOwner(primaryGripPickup.gameObject);
            if (localOwnsPrimary)
                primaryGripPickup.pickupable = !primaryHeld;
            else
                primaryGripPickup.pickupable = !(primaryHeld || secondaryHeld);
        }

        if (secondaryGripPickup != null)
        {
            bool localHoldsPrimary = primaryHeld && primaryGripPickup != null && Networking.IsOwner(primaryGripPickup.gameObject);
            bool localHoldsFrontGrip = secondaryHeld && Networking.IsOwner(secondaryGripPickup.gameObject);
            secondaryGripPickup.pickupable = localHoldsPrimary || localHoldsFrontGrip;
        }
    }

    private void UpdateGripPointParents(bool isPrimaryHeld, bool isSecondaryHeld)
    {
        if (gunVisual == null)
            return;

        if (secondaryGripPickup != null)
        {
            if (isSecondaryHeld)
                secondaryGripPickup.transform.SetParent(secondaryGripOriginalParent, true);
            else if (isPrimaryHeld)
            {
                secondaryGripPickup.transform.SetParent(gunVisual, false);
                secondaryGripPickup.transform.localPosition = secondaryGripGunVisualLocalPosition;
                secondaryGripPickup.transform.localRotation = secondaryGripGunVisualLocalRotation;
            }
            else if (secondaryGripPickup.transform.parent == gunVisual && secondaryGripOriginalParent != gunVisual)
                secondaryGripPickup.transform.SetParent(secondaryGripOriginalParent, true);
        }

        if (primaryGripPickup != null)
        {
            if (isPrimaryHeld)
            {
                primaryGripPickup.transform.SetParent(primaryGripOriginalParent, true);
                SetPrimaryGripPhysics(true);
            }
            else if (isSecondaryHeld)
            {
                primaryGripPickup.transform.SetParent(gunVisual, true);
                SnapPrimaryGripToGunVisual();
                SetPrimaryGripPhysics(true);
            }
            else if (primaryGripPickup.transform.parent == gunVisual && primaryGripOriginalParent != gunVisual)
            {
                primaryGripPickup.transform.SetParent(primaryGripOriginalParent, true);
                SetPrimaryGripPhysics(false);
            }
            else
            {
                SetPrimaryGripPhysics(false);
            }
        }
    }

    private void KeepPrimaryGripAtSocket()
    {
        if (primaryGripPickup == null || gunVisual == null)
            return;

        primaryGripPickup.transform.SetParent(gunVisual, true);
        SnapPrimaryGripToGunVisual();
        SetPrimaryGripPhysics(true);
    }

    private void SnapPrimaryGripToGunVisual()
    {
        if (primaryGripPickup == null || gunVisual == null)
            return;

        if (primaryGripPoint == null)
        {
            primaryGripPickup.transform.localPosition = primaryGripPickup.transform.localPosition;
            primaryGripPickup.transform.localRotation = primaryGripPickup.transform.localRotation;
            return;
        }

        Quaternion rootRotation = gunVisual.rotation * Quaternion.Inverse(primaryGripPointLocalRotation);
        Vector3 rootPosition = gunVisual.position - (rootRotation * primaryGripPointLocalPosition);
        primaryGripPickup.transform.SetPositionAndRotation(rootPosition, rootRotation);
    }

    private void SetPrimaryGripPhysics(bool anchored)
    {
        if (primaryGripRigidbody == null)
            return;

        if (anchored)
        {
            if (!primaryGripRigidbody.isKinematic)
            {
                primaryGripRigidbody.velocity = Vector3.zero;
                primaryGripRigidbody.angularVelocity = Vector3.zero;
            }
            ApplyGripKinematicState(false, true);
            primaryGripRigidbody.detectCollisions = true;
            return;
        }

        ApplyGripKinematicState(primaryGripOriginalUseGravity, primaryGripOriginalIsKinematic);
        primaryGripRigidbody.detectCollisions = primaryGripOriginalDetectCollisions;
    }

    // VRCObjectSync keeps its own copy of the rigidbody's kinematic/gravity state and reverts any
    // direct write to Rigidbody.isKinematic / .useGravity every update (see the ClientSim warning
    // "Rigidbody.isKinematic was set outside of VRCObjectSync.SetKinematic method!"). That state is
    // network-synced, so direct writes only visibly lose against real remote clients. Guarded on the
    // current value so repeat calls don't re-serialize.
    private void ApplyGripKinematicState(bool useGravity, bool kinematic)
    {
        if (primaryGripObjectSync == null)
        {
            primaryGripObjectSync = primaryGripRigidbody.GetComponent<VRCObjectSync>();
        }

        if (primaryGripRigidbody.useGravity != useGravity)
        {
            if (primaryGripObjectSync != null)
                primaryGripObjectSync.SetGravity(useGravity);
            else
                primaryGripRigidbody.useGravity = useGravity;
        }

        if (primaryGripRigidbody.isKinematic != kinematic)
        {
            if (primaryGripObjectSync != null)
                primaryGripObjectSync.SetKinematic(kinematic);
            else
                primaryGripRigidbody.isKinematic = kinematic;
        }
    }

    // Two-handed look-at: rotate `gunVisual` toward the secondary grip.
    private void ApplyTwoHandedRotation()
    {
        if (gunVisual == null || secondaryGripPickup == null) return;

        Vector3 targetPos = secondaryGripPickup.transform.position;

        // Start from rest pose for a deterministic offset.
        gunVisual.localPosition = gunVisualRestLocalPosition;
        gunVisual.localRotation = gunVisualRestLocalRotation;

        // Yaw (horizontal)
        Vector3 localTarget = gunVisual.InverseTransformPoint(targetPos);
        float yAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;
        yAngle = Mathf.Clamp(yAngle, -rotationRange.y * 0.5f, rotationRange.y * 0.5f);
        gunVisual.localRotation = gunVisualRestLocalRotation * Quaternion.Euler(0f, yAngle, 0f);

        // Pitch (vertical) — recompute local target after applying yaw.
        localTarget = gunVisual.InverseTransformPoint(targetPos);
        float xAngle = Mathf.Atan2(localTarget.y, localTarget.z) * Mathf.Rad2Deg;
        xAngle = Mathf.Clamp(xAngle, -rotationRange.x * 0.5f, rotationRange.x * 0.5f);

        // Smooth angles to avoid quaternion/gimbal issues.
        Vector3 targetAngles = new Vector3(
            followAngles.x + Mathf.DeltaAngle(followAngles.x, xAngle),
            followAngles.y + Mathf.DeltaAngle(followAngles.y, yAngle));

        followAngles = Vector3.SmoothDamp(followAngles, targetAngles, ref followVelocity, followSpeed);

        gunVisual.localRotation = gunVisualRestLocalRotation * Quaternion.Euler(-followAngles.x, followAngles.y, 0f);
    }
}
