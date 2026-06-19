
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]

public class UCS_MagBelt : UdonSharpBehaviour
{
    [SerializeField] private Transform BeltHipBone;

    [SerializeField] private Transform MagazineHolsterPoint;
    [SerializeField] private UCS_AmmoInventory ammoInventory;
    [SerializeField] private string requestedMagTypeId = "";
    private UCS_MagPool activeMagPool;

    //switch between left and right handed
    [SerializeField] private bool flipBelt = false; 

    [SerializeField] private bool UseFullHipRotation = false; //if true, the belt will match the full rotation of the hips, which can be useful for certain avatar types, but can cause jitter on others. If false, the belt will only match the yaw rotation of the hips, which is more stable for most avatars.
    private VRCPlayerApi localPlayer;

    private bool beltFlipped = false;
    private GameObject currentPreviewMag;
    private UCS_Mag currentPreviewMagData;
    private Collider[] beltColliders;
    private Rigidbody[] beltRigidbodies;
    private Vector3 baseLocalScale = Vector3.one;
    private float avatarScaleMultiplier = 1f;
    private float referenceEyeHeightAsMeters = -1f;
    // cached values to avoid creating temporaries in the update loop
    private Vector3 cachedHipPosition = Vector3.zero;
    private Quaternion cachedHipRotation = Quaternion.identity;

    private const float MinValidEyeHeight = 0.01f;

    private UCS_Mag TryGetMagData(GameObject magObject)
    {
        if (magObject == null)
        {
            return null;
        }

        UCS_Mag magData = magObject.GetComponent<UCS_Mag>();
        if (magData == null)
        {
            magData = magObject.GetComponentInChildren<UCS_Mag>(true);
        }

        return magData;
    }

    public bool IsMagCloseToHolster(UCS_Mag mag, float maxDistance)
    {
        if (mag == null || MagazineHolsterPoint == null)
        {
            return false;
        }

        Transform pickupRoot = mag.GetPickupRootTransform();
        if (pickupRoot == null)
        {
            return false;
        }

        return Vector3.Distance(pickupRoot.position, MagazineHolsterPoint.position) <= maxDistance;
    }

    public Transform GetMagazineHolsterPoint()
    {
        return MagazineHolsterPoint;
    }

    private void AlignPreviewMagToHolster()
    {
        if (currentPreviewMagData == null || MagazineHolsterPoint == null)
        {
            return;
        }

        if (currentPreviewMagData.IsHeld())
        {
            return;
        }

        // Belt preview mags should always be physics-locked while holstered.
        currentPreviewMagData.SetPickupUseGravity(false);
        currentPreviewMagData.SetPickupKinematic(true);

        // Preview mags should never stay socketed. If stale sync toggles socketed/hidden
        // state (often on first acquire), force preview state back before alignment.
        if (currentPreviewMagData.IsSocketed())
        {
            currentPreviewMagData.ClearSocket();
            currentPreviewMagData.SetSocketed(false);
            currentPreviewMagData.SetHeld(false);
            currentPreviewMagData.SetWorldVisible(true);
            currentPreviewMagData.SetPickupUseGravity(false);
            currentPreviewMagData.SetPickupKinematic(true);
        }

        Transform pickupRoot = currentPreviewMagData.GetPickupRootTransform();
        if (pickupRoot != null)
        {
            pickupRoot.SetPositionAndRotation(MagazineHolsterPoint.position, MagazineHolsterPoint.rotation);
        }
    }

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        baseLocalScale = transform.localScale;
        avatarScaleMultiplier = 1f;
        beltColliders = GetComponentsInChildren<Collider>(true);
        beltRigidbodies = GetComponentsInChildren<Rigidbody>(true);
        DisableBeltPhysics();
        FlipBelt(flipBelt);
        UpdateAvatarScale(localPlayer);
    }

    private void UpdateAvatarScale(VRCPlayerApi player)
    {
        if (player == null || !player.isLocal)
        {
            return;
        }

        float currentEyeHeightAsMeters = player.GetAvatarEyeHeightAsMeters();
        if (currentEyeHeightAsMeters <= MinValidEyeHeight || float.IsNaN(currentEyeHeightAsMeters) || float.IsInfinity(currentEyeHeightAsMeters))
        {
            avatarScaleMultiplier = 1f;
            ApplyBeltScale();
            return;
        }

        if (referenceEyeHeightAsMeters <= MinValidEyeHeight || float.IsNaN(referenceEyeHeightAsMeters) || float.IsInfinity(referenceEyeHeightAsMeters))
        {
            referenceEyeHeightAsMeters = currentEyeHeightAsMeters;
        }

        avatarScaleMultiplier = Mathf.Max(MinValidEyeHeight, currentEyeHeightAsMeters / referenceEyeHeightAsMeters);
        ApplyBeltScale();
    }

    private void ApplyBeltScale()
    {
        Vector3 scaledScale = baseLocalScale * avatarScaleMultiplier;
        if (beltFlipped)
        {
            scaledScale.x = -Mathf.Abs(scaledScale.x);
        }

        if (float.IsNaN(scaledScale.x) || float.IsInfinity(scaledScale.x) || float.IsNaN(scaledScale.y) || float.IsInfinity(scaledScale.y) || float.IsNaN(scaledScale.z) || float.IsInfinity(scaledScale.z))
        {
            return;
        }

        transform.localScale = scaledScale;
    }

    private void DisableBeltPhysics()
    {
        if (beltColliders != null)
        {
            for (int i = 0; i < beltColliders.Length; i++)
            {
                if (beltColliders[i] != null)
                {
                    beltColliders[i].enabled = false;
                }
            }
        }

        if (beltRigidbodies != null)
        {
            for (int i = 0; i < beltRigidbodies.Length; i++)
            {
                if (beltRigidbodies[i] != null)
                {
                    beltRigidbodies[i].useGravity = false;
                    beltRigidbodies[i].isKinematic = true;
                }
            }
        }
    }

    public override void PostLateUpdate()
    {
        //we use post late because it's after the avatar bones have been updated
        if (localPlayer != null && BeltHipBone != null)
        {
            cachedHipPosition = localPlayer.GetBonePosition(HumanBodyBones.Hips);
            cachedHipRotation = localPlayer.GetBoneRotation(HumanBodyBones.Hips);
            BeltHipBone.position = cachedHipPosition;
            if (UseFullHipRotation)
            {
                BeltHipBone.rotation = cachedHipRotation;
            }
            else
            {
                // Only match the yaw rotation for more stable behavior
                float yaw = cachedHipRotation.eulerAngles.y;
                BeltHipBone.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        AlignPreviewMagToHolster();
    }

    public void RequestMagType(string magTypeId)
    {
        requestedMagTypeId = magTypeId;

        if (currentPreviewMagData != null && (currentPreviewMagData.IsHeld() || currentPreviewMagData.IsSocketed()))
        {
            currentPreviewMag = null;
            currentPreviewMagData = null;
        }

        if (currentPreviewMag != null && activeMagPool != null)
        {
            activeMagPool.ReturnMagToPool(currentPreviewMag);
        }

        currentPreviewMag = null;
        currentPreviewMagData = null;

        if (activeMagPool == null || MagazineHolsterPoint == null || string.IsNullOrEmpty(magTypeId))
        {
            return;
        }

        // Acquire a preview mag owned by the local player so synced state updates correctly
        currentPreviewMag = activeMagPool.AcquireFullMag(Networking.LocalPlayer);
        if (currentPreviewMag == null)
        {
            return;
        }

        currentPreviewMagData = TryGetMagData(currentPreviewMag);
        if (currentPreviewMagData != null)
        {
            // Ensure a reused pooled mag cannot keep stale socket state when used as a belt preview.
            currentPreviewMagData.ClearSocket();
            currentPreviewMagData.SetInUse(true);
            currentPreviewMagData.SetSourceBelt(this);
            currentPreviewMagData.ClearReturnToPool();

            Transform pickupRoot = currentPreviewMagData.GetPickupRootTransform();
            if (pickupRoot != null && MagazineHolsterPoint != null)
            {
                pickupRoot.SetPositionAndRotation(MagazineHolsterPoint.position, MagazineHolsterPoint.rotation);
            }
            // disable gravity and make kinematic on the preview pickup so it doesn't fall or jitter
            currentPreviewMagData.SetPickupUseGravity(false);
            currentPreviewMagData.SetPickupKinematic(true);
        }
        if (currentPreviewMagData != null)
        {
            currentPreviewMagData.SetHeld(false);
            currentPreviewMagData.SetSocketed(false);
            currentPreviewMagData.SetWorldVisible(true);
            currentPreviewMagData.SetPickupRootActive(true);
            currentPreviewMagData.SetPickupVisualVisible(true);
            // SetSocketed(false) restores dynamic physics, so force belt-preview physics again.
            currentPreviewMagData.SetPickupUseGravity(false);
            currentPreviewMagData.SetPickupKinematic(true);
        }

        AlignPreviewMagToHolster();
    }

    public void OnMagPickedUpFromBelt(UCS_Mag mag)
    {
        if (mag == null)
        {
            return;
        }

        if (ammoInventory != null)
        {
            string magTypeId = mag.GetMagTypeId();
            int availableAmmo = ammoInventory.GetAmmoCount(magTypeId);
            int fillAmount = Mathf.Min(mag.GetMaxAmmo(), Mathf.Max(0, availableAmmo));
            mag.SetCurrentAmmo(fillAmount);

            if (fillAmount > 0)
            {
                ammoInventory.ConsumeAmmo(magTypeId, fillAmount);
            }
        }

        if (currentPreviewMag == mag.gameObject)
        {
            currentPreviewMag = null;
            currentPreviewMagData = null;
            SendCustomEventDelayedFrames(nameof(RespawnRequestedPreview), 1);
        }
        // when a mag is picked up by the player from the belt, restore physics on the pickup rigidbody
        if (mag != null)
        {
            mag.SetPickupUseGravity(true);
            mag.SetPickupKinematic(false);
        }
    }

    public bool ReturnMagToBelt(UCS_Mag mag)
    {
        if (mag == null)
        {
            return false;
        }

        if (mag.IsSocketed())
        {
            return false;
        }

        UCS_MagPool magPool = mag.GetMagPool();
        if (magPool == null)
        {
            magPool = activeMagPool;
        }

        if (magPool == null)
        {
            return false;
        }

        magPool.ReturnMagToPool(mag);

        RespawnRequestedPreview();
        return true;
    }

    public void SetRequestedMagSource(string magTypeId, UCS_MagPool magPool)
    {
        if (currentPreviewMag != null && activeMagPool != null)
        {
            activeMagPool.ReturnMagToPool(currentPreviewMag);
        }

        currentPreviewMag = null;
        currentPreviewMagData = null;

        activeMagPool = magPool;
        requestedMagTypeId = magTypeId;

        if (!string.IsNullOrEmpty(requestedMagTypeId) && activeMagPool != null)
        {
            RequestMagType(requestedMagTypeId);
        }
    }

    public void RespawnRequestedPreview()
    {
        if (string.IsNullOrEmpty(requestedMagTypeId))
        {
            return;
        }

        if (currentPreviewMag != null)
        {
            return;
        }

        RequestMagType(requestedMagTypeId);
    }

    public void ClearRequestedMag()
    {
        requestedMagTypeId = "";

        if (currentPreviewMag != null && activeMagPool != null)
        {
            activeMagPool.ReturnMagToPool(currentPreviewMag);
        }

        currentPreviewMag = null;
        currentPreviewMagData = null;
        activeMagPool = null;
    }

    private void FlipBelt(bool flip)
    {
        if (flip && !beltFlipped)
        {
            beltFlipped = true;
            ApplyBeltScale();
        }
        else if (!flip && beltFlipped)
        {
            beltFlipped = false;
            ApplyBeltScale();
        }
    }

    public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters)
    {
        base.OnAvatarEyeHeightChanged(player, prevEyeHeightAsMeters);
        if (player != null && player.isLocal)
        {
            FlipBelt(flipBelt);
            UpdateAvatarScale(player);
        }
    }


}
