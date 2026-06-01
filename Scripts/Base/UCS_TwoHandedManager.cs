using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Manages two-handed grip states and a look-at rotation for the gun visual.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
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
    private bool primaryGripOriginalUseGravity;
    private bool primaryGripOriginalIsKinematic;
    private bool primaryGripOriginalDetectCollisions;
    private bool updateLoopActive;
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
    }

    void Update()
    {
        if (!updateLoopActive)
            return;

        // Two-handed look-at runs every frame while both grips are held.
        if (primaryHeld && secondaryHeld)
            ApplyTwoHandedRotation();
        else if (!primaryHeld && secondaryHeld)
            KeepPrimaryGripAtSocket();
    }

    public void PrimaryPickup()
    {
        SetGripState(true, secondaryHeld);
    }

    public void PrimaryDrop()
    {
        SetGripState(false, secondaryHeld);
    }

    public void SecondaryPickup()
    {
        SetGripState(primaryHeld, true);
    }

    public void SecondaryDrop()
    {
        SetGripState(primaryHeld, false);
    }

    private void SetGripState(bool isPrimaryHeld, bool isSecondaryHeld)
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

        if (primaryGripPickup != null)
            primaryGripPickup.pickupable = !isPrimary;

        // Allow secondary pickup only when local player owns the primary, or while secondary is held.
        if (secondaryGripPickup != null)
        {
            bool localPlayerHoldsPrimary = isPrimary && primaryGripPickup != null && Networking.IsOwner(primaryGripPickup.gameObject);
            secondaryGripPickup.pickupable = localPlayerHoldsPrimary || isSecondary;
        }

        // Reset look-at only when the gun is fully released.
        if (!isPrimary && !isSecondary)
        {
            followAngles = Vector3.zero;
            followVelocity = Vector3.zero;
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
                SetPrimaryGripPhysics(false);
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
        primaryGripPickup.pickupable = true;
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
            primaryGripRigidbody.useGravity = false;
            primaryGripRigidbody.isKinematic = true;
            primaryGripRigidbody.detectCollisions = true;
            primaryGripRigidbody.velocity = Vector3.zero;
            primaryGripRigidbody.angularVelocity = Vector3.zero;
            return;
        }

        primaryGripRigidbody.useGravity = primaryGripOriginalUseGravity;
        primaryGripRigidbody.isKinematic = primaryGripOriginalIsKinematic;
        primaryGripRigidbody.detectCollisions = primaryGripOriginalDetectCollisions;
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
