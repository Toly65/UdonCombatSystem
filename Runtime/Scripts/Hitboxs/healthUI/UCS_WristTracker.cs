
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_WristTracker : UdonSharpBehaviour
{
    const float MinDirectionSqrMagnitude = 0.000001f;

    VRCPlayerApi localPlayer;

    [SerializeField]
    bool rightWrist = false;

    [SerializeField]
    Transform wristPoint;

    HumanBodyBones lowerArmBone;
    HumanBodyBones handBone;
    VRCPlayerApi.TrackingDataType handTrackingType;
    Vector3 wristLocalOffset;
    bool wristOffsetReady;
    Vector3 cachedLowerArmPosition;
    Vector3 cachedHandPosition;
    Quaternion cachedLowerArmRotation;
    VRCPlayerApi.TrackingData cachedHandTrackingData;
    Vector3 wristToForearmDirection;
    Vector3 wristPointUpDirection;
    Vector3 handYAxisWorld;
    Vector3 handForwardWorld;
    Vector3 projectedForwardOnForearmPlane;
    Quaternion snappedWorldRotation;
    Quaternion wristPointLocalRotation;
    Vector3 wristPointBaseScale;
    float baseEyeHeight = 1f;
    float currentEyeHeight = 1f;
    float wristPointScaleMultiplier = 1f;
    bool wristPointScaleReady;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        UpdateTrackedBones();

        if (wristPoint == null)
        {
            wristPoint = transform;
        }

        InitializeWristPointScale();
        RecalculateWristOffset();
    }

    public override void PostLateUpdate()
    {
        if (Utilities.IsValid(localPlayer))
        {
            cachedLowerArmPosition = localPlayer.GetBonePosition(lowerArmBone);
            if (cachedLowerArmPosition == Vector3.zero)
            {
                return;
            }

            cachedLowerArmRotation = localPlayer.GetBoneRotation(lowerArmBone);
            transform.SetPositionAndRotation(cachedLowerArmPosition, cachedLowerArmRotation);

            cachedHandPosition = localPlayer.GetBonePosition(handBone);
            if (cachedHandPosition == Vector3.zero)
            {
                return;
            }

            RecalculateWristPointRotation();
        }
    }

    public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeight)
    {
        if (!Utilities.IsValid(localPlayer) || !Utilities.IsValid(player))
        {
            return;
        }

        if (player.playerId != localPlayer.playerId)
        {
            return;
        }

        UpdateWristPointScaleFromEyeHeight();
        RecalculateWristOffset();
        // One delayed pass helps after avatar swap/resize when bone data settles a frame later.
        SendCustomEventDelayedSeconds(nameof(RefreshAfterEyeHeightChanged), 0.1f);
    }

    public void RefreshAfterEyeHeightChanged()
    {
        UpdateWristPointScaleFromEyeHeight();
        RecalculateWristOffset();
    }

    public void RecalculateWristOffset()
    {
        if (!Utilities.IsValid(localPlayer))
        {
            wristOffsetReady = false;
            return;
        }

        cachedLowerArmPosition = localPlayer.GetBonePosition(lowerArmBone);
        cachedHandPosition = localPlayer.GetBonePosition(handBone);
        if (cachedLowerArmPosition == Vector3.zero || cachedHandPosition == Vector3.zero)
        {
            wristOffsetReady = false;
            return;
        }

        cachedLowerArmRotation = localPlayer.GetBoneRotation(lowerArmBone);
        wristLocalOffset = Quaternion.Inverse(cachedLowerArmRotation) * (cachedHandPosition - cachedLowerArmPosition);
        wristOffsetReady = true;
        ApplyWristPointOffset();
        RecalculateWristPointRotation();
    }

    void ApplyWristPointOffset()
    {
        if (!wristOffsetReady || wristPoint == null || wristPoint == transform)
        {
            return;
        }

        wristPoint.localPosition = wristLocalOffset;
    }

    void RecalculateWristPointRotation()
    {
        if (!wristOffsetReady || !Utilities.IsValid(localPlayer))
        {
            return;
        }

        if (wristPoint == null || wristPoint == transform)
        {
            return;
        }

        wristToForearmDirection = cachedLowerArmPosition - cachedHandPosition;
        if (wristToForearmDirection.sqrMagnitude <= MinDirectionSqrMagnitude)
        {
            return;
        }

        wristToForearmDirection.Normalize();
        // Requirement: wristPoint -Y points toward the forearm bone.
        wristPointUpDirection = -wristToForearmDirection;

        cachedHandTrackingData = localPlayer.GetTrackingData(handTrackingType);
        handYAxisWorld = cachedHandTrackingData.rotation * Vector3.up;
        handForwardWorld = cachedHandTrackingData.rotation * Vector3.forward;

        // Requirement: wristPoint +Z follows hand tracking +Y, projected to the forearm plane.
        projectedForwardOnForearmPlane = Vector3.ProjectOnPlane(handYAxisWorld, wristPointUpDirection);
        if (projectedForwardOnForearmPlane.sqrMagnitude <= MinDirectionSqrMagnitude)
        {
            projectedForwardOnForearmPlane = Vector3.ProjectOnPlane(handForwardWorld, wristPointUpDirection);
            if (projectedForwardOnForearmPlane.sqrMagnitude <= MinDirectionSqrMagnitude)
            {
                return;
            }
        }
        projectedForwardOnForearmPlane.Normalize();

        // Continuous barrel-style rotation around the forearm axis using palm direction.
        snappedWorldRotation = Quaternion.LookRotation(projectedForwardOnForearmPlane, wristPointUpDirection);
        wristPointLocalRotation = Quaternion.Inverse(cachedLowerArmRotation) * snappedWorldRotation;
        ApplyWristPointRotation();
    }

    void ApplyWristPointRotation()
    {
        if (wristPoint == null || wristPoint == transform)
        {
            return;
        }

        wristPoint.localRotation = wristPointLocalRotation;
    }

    void UpdateTrackedBones()
    {
        lowerArmBone = rightWrist ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm;
        handBone = rightWrist ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand;
        handTrackingType = rightWrist ? VRCPlayerApi.TrackingDataType.RightHand : VRCPlayerApi.TrackingDataType.LeftHand;
    }

    void InitializeWristPointScale()
    {
        if (wristPoint == null || !Utilities.IsValid(localPlayer))
        {
            wristPointScaleReady = false;
            return;
        }

        wristPointBaseScale = wristPoint.localScale;
        currentEyeHeight = localPlayer.GetAvatarEyeHeightAsMeters();
        if (currentEyeHeight > 0.001f)
        {
            baseEyeHeight = currentEyeHeight;
        }
        else
        {
            baseEyeHeight = 1f;
        }

        wristPointScaleReady = true;
        ApplyWristPointScale();
    }

    void UpdateWristPointScaleFromEyeHeight()
    {
        if (!wristPointScaleReady || wristPoint == null || !Utilities.IsValid(localPlayer))
        {
            return;
        }

        currentEyeHeight = localPlayer.GetAvatarEyeHeightAsMeters();
        if (currentEyeHeight <= 0.001f || baseEyeHeight <= 0.001f)
        {
            return;
        }

        wristPointScaleMultiplier = currentEyeHeight / baseEyeHeight;
        ApplyWristPointScale();
    }

    void ApplyWristPointScale()
    {
        if (!wristPointScaleReady || wristPoint == null)
        {
            return;
        }

        wristPoint.localScale = wristPointBaseScale * wristPointScaleMultiplier;
    }

    public void SetRightWrist(bool right)
    {
        rightWrist = right;
        UpdateTrackedBones();
        RecalculateWristOffset();
    }
}
