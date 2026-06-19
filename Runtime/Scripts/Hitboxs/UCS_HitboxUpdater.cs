
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UCS_HitboxUpdater : UdonSharpBehaviour
{
    private const float FullBodyCapsuleWidthBoost = 1.7f;
    private const float CapsuleHeadProportionBoost = 2.0f;
    private const float EyeHeightResizeDelaySeconds = 1.0f;

    [SerializeField] private int maxHitboxCapacity = 800;
    [SerializeField] private int maxPlayerCapacity = 80;

    private UCS_PlayerHitbox[] _activeHitboxes;
    private int _activeCount;
    private UCS_PlayerHealthObjectBase[] _trackedPlayers;
    private int _trackedPlayerCount;
    private int[] _pendingEyeHeightPlayerIds;
    private int _pendingEyeHeightPlayerCount;
    private bool _delayedEyeHeightResizePending;

    // Avatar sizing working vars
    private int _sizeI;
    private int _sizeJ;
    private UCS_PlayerHealthObjectBase _sizeObj;
    private UCS_PlayerHitbox[] _sizeTier;
    private UCS_PlayerHitbox _sizeHitbox;
    private Vector3 _sizeVecA;
    private Vector3 _sizeVecB;

    // Position update working vars
    private int _posI;
    private UCS_PlayerHitbox _posHitbox;
    private Transform _posTransform;
    private VRCPlayerApi _posPlayer;
    private int _posTrackingType;
    private Vector3 _posVecA;
    private Vector3 _posVecB;
    private Vector3 _posVecC;
    private Vector3 _posDir;

    private UCS_PlayerHealthObjectBase ResolveHealthObject(GameObject healthObjGo, string caller)
    {
        if (healthObjGo == null)
        {
            Debug.LogWarning("UCS_HitboxUpdater." + caller + " was called with a null GameObject.");
            return null;
        }

        UCS_PlayerHealthObjectBase healthObj = healthObjGo.GetComponent<UCS_PlayerHealthObjectBase>();
        if (healthObj == null)
            Debug.LogWarning("UCS_HitboxUpdater." + caller + " could not find UCS_PlayerHealthObjectBase on " + healthObjGo.name + ".");

        return healthObj;
    }

    private void EnsureInitialized()
    {
        if (_activeHitboxes == null)
            _activeHitboxes = new UCS_PlayerHitbox[maxHitboxCapacity];
        if (_trackedPlayers == null)
            _trackedPlayers = new UCS_PlayerHealthObjectBase[maxPlayerCapacity];
        if (_pendingEyeHeightPlayerIds == null)
            _pendingEyeHeightPlayerIds = new int[maxPlayerCapacity];
    }

    void Start()
    {
        EnsureInitialized();
    }

    public override void PostLateUpdate()
    {
        EnsureInitialized();
        for (_posI = 0; _posI < _activeCount; _posI++)
        {
            _posHitbox = _activeHitboxes[_posI];
            if (_posHitbox == null) continue;
            _posTransform = _posHitbox.scalePoint != null ? _posHitbox.scalePoint : _posHitbox.transform;
            _posPlayer = _posHitbox.GetTrackedPlayer();
            if (_posPlayer == null || !_posPlayer.IsValid()) continue;

            _posTrackingType = (int)_posHitbox.trackingBone;

            if (_posTrackingType >= 0 && _posTrackingType < 100)
            {
                _posVecA = _posPlayer.GetBonePosition((HumanBodyBones)_posTrackingType);
                if (_posVecA == Vector3.zero) continue;
                _posTransform.SetPositionAndRotation(_posVecA, _posPlayer.GetBoneRotation((HumanBodyBones)_posTrackingType));
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.FullBodyPill)
            {
                _posTransform.position = _posPlayer.GetPosition();
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.TorsoBox)
            {
                _posVecA = _posPlayer.GetBonePosition(HumanBodyBones.Hips);
                _posVecB = _posPlayer.GetBonePosition(HumanBodyBones.Neck);
                if (_posVecA == Vector3.zero || _posVecB == Vector3.zero) continue;
                _posDir = _posVecB - _posVecA;
                _posTransform.position = _posVecA + _posDir * 0.5f;
                _posTransform.up = _posDir.normalized;
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.LegBox)
            {
                _posVecA = _posPlayer.GetBonePosition(HumanBodyBones.Hips);
                if (_posVecA == Vector3.zero) continue;
                _posVecB = _posPlayer.GetBonePosition(HumanBodyBones.LeftFoot);
                _posVecC = _posPlayer.GetBonePosition(HumanBodyBones.RightFoot);
                if (_posVecB != Vector3.zero && _posVecC != Vector3.zero)
                    _posVecB = (_posVecB + _posVecC) * 0.5f;
                else
                    _posVecB = _posVecA + Vector3.down * 0.5f;
                _posDir = _posVecB - _posVecA;
                _posTransform.position = _posVecA + _posDir * 0.5f;
                _posTransform.up = -_posDir.normalized;
                if (_posHitbox.scalePoint != null)
                {
                    Vector3 s = _posHitbox.scalePoint.localScale;
                    s.y = _posDir.magnitude;
                    _posHitbox.scalePoint.localScale = s;
                }
            }
        }
    }

    public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeight)
    {
        EnsureInitialized();
        if (player == null || !player.IsValid()) return;

        QueuePlayerForDelayedEyeHeightResize(player.playerId);

        // Delay one pass so avatar bones settle after eye-height updates.
        if (_delayedEyeHeightResizePending) return;

        _delayedEyeHeightResizePending = true;
        SendCustomEventDelayedSeconds(nameof(ApplyDelayedEyeHeightResize), EyeHeightResizeDelaySeconds);
    }

    public void ApplyDelayedEyeHeightResize()
    {
        EnsureInitialized();

        int queuedCount = _pendingEyeHeightPlayerCount;

        for (_sizeI = 0; _sizeI < queuedCount; _sizeI++)
        {
            ApplySizingForQueuedPlayerId(_pendingEyeHeightPlayerIds[_sizeI]);
        }

        if (_pendingEyeHeightPlayerCount == queuedCount)
        {
            _pendingEyeHeightPlayerCount = 0;
        }
        else
        {
            int remainingCount = _pendingEyeHeightPlayerCount - queuedCount;
            for (int i = 0; i < remainingCount; i++)
                _pendingEyeHeightPlayerIds[i] = _pendingEyeHeightPlayerIds[queuedCount + i];
            _pendingEyeHeightPlayerCount = remainingCount;
        }

        if (_pendingEyeHeightPlayerCount > 0)
        {
            SendCustomEventDelayedSeconds(nameof(ApplyDelayedEyeHeightResize), EyeHeightResizeDelaySeconds);
            return;
        }

        _delayedEyeHeightResizePending = false;
    }

    private void QueuePlayerForDelayedEyeHeightResize(int playerId)
    {
        for (int i = 0; i < _pendingEyeHeightPlayerCount; i++)
        {
            if (_pendingEyeHeightPlayerIds[i] != playerId) continue;
            return;
        }

        if (_pendingEyeHeightPlayerCount >= _pendingEyeHeightPlayerIds.Length) return;
        _pendingEyeHeightPlayerIds[_pendingEyeHeightPlayerCount] = playerId;
        _pendingEyeHeightPlayerCount++;
    }

    private void ApplySizingForQueuedPlayerId(int playerId)
    {
        for (int i = 0; i < _trackedPlayerCount; i++)
        {
            UCS_PlayerHealthObjectBase healthObj = _trackedPlayers[i];
            if (healthObj == null) continue;

            VRCPlayerApi trackedPlayer = healthObj.GetTrackedPlayer();
            if (trackedPlayer == null || !trackedPlayer.IsValid()) continue;
            if (trackedPlayer.playerId != playerId) continue;

            ApplySizingForPlayer(healthObj, trackedPlayer);
            return;
        }
    }

    private void ApplySizingForPlayer(UCS_PlayerHealthObjectBase healthObj, VRCPlayerApi player)
    {
        if (healthObj == null || player == null || !player.IsValid()) return;

        ApplySizingToTier(healthObj.lod0Hitboxes, player);
        ApplySizingToTier(healthObj.lod1Hitboxes, player);
        ApplySizingToTier(healthObj.lod2Hitboxes, player);
    }

    private void ApplySizingToTier(UCS_PlayerHitbox[] hitboxes, VRCPlayerApi player)
    {
        if (hitboxes == null) return;
        _sizeTier = hitboxes;
        float eyeHeight = player.GetAvatarEyeHeightAsMeters();
        for (_sizeJ = 0; _sizeJ < _sizeTier.Length; _sizeJ++)
        {
            _sizeHitbox = _sizeTier[_sizeJ];
            if (_sizeHitbox == null || _sizeHitbox.scalePoint == null) continue;

            if (_sizeHitbox.trackingBone == HitboxTrackingType.FullBodyPill)
            {
                float upperArmSpan = GetUpperArmSpan(player, eyeHeight);
                float capsuleWidth = upperArmSpan * FullBodyCapsuleWidthBoost;
                float capsuleHeight = GetCapsuleHeightFromHeadProportion(player, eyeHeight);
                _sizeHitbox.scalePoint.localScale = new Vector3(capsuleWidth, capsuleHeight, capsuleWidth);
                continue;
            }

            if (_sizeHitbox.trackingBone == HitboxTrackingType.TorsoBox)
            {
                _sizeVecA = player.GetBonePosition(HumanBodyBones.Hips);
                _sizeVecB = player.GetBonePosition(HumanBodyBones.Neck);
                if (_sizeVecA == Vector3.zero || _sizeVecB == Vector3.zero) continue;
                float torsoLength = (_sizeVecB - _sizeVecA).magnitude;
                float upperArmSpan = GetUpperArmSpan(player, eyeHeight);
                _sizeHitbox.scalePoint.localScale = new Vector3(upperArmSpan, torsoLength, upperArmSpan * 0.5f);
                continue;
            }

            if (_sizeHitbox.trackingBone == HitboxTrackingType.LegBox)
            {
                // Y is updated per-frame in PostLateUpdate; only set X/Z here
                Vector3 s = _sizeHitbox.scalePoint.localScale;
                s.x = _sizeHitbox.scaleBaseX * eyeHeight;
                s.z = _sizeHitbox.scaleBaseZ * eyeHeight;
                _sizeHitbox.scalePoint.localScale = s;
                continue;
            }

            if (_sizeHitbox.targetBone != HitboxTrackingType.None)
            {
                _sizeVecA = player.GetBonePosition((HumanBodyBones)(int)_sizeHitbox.trackingBone);
                _sizeVecB = player.GetBonePosition((HumanBodyBones)(int)_sizeHitbox.targetBone);
                if (_sizeVecA == Vector3.zero || _sizeVecB == Vector3.zero) continue;
                _sizeHitbox.scalePoint.localScale = new Vector3(
                    _sizeHitbox.scaleBaseX * eyeHeight,
                    (_sizeVecB - _sizeVecA).magnitude,
                    _sizeHitbox.scaleBaseZ * eyeHeight);
            }
        }
    }

    private float GetShoulderWidth(VRCPlayerApi player, float eyeHeight)
    {
        _sizeVecA = player.GetBonePosition(HumanBodyBones.LeftShoulder);
        _sizeVecB = player.GetBonePosition(HumanBodyBones.RightShoulder);
        if (_sizeVecA != Vector3.zero && _sizeVecB != Vector3.zero)
            return (_sizeVecB - _sizeVecA).magnitude;

        return eyeHeight * 0.35f;
    }

    private float GetUpperArmSpan(VRCPlayerApi player, float eyeHeight)
    {
        _sizeVecA = player.GetBonePosition(HumanBodyBones.LeftUpperArm);
        _sizeVecB = player.GetBonePosition(HumanBodyBones.RightUpperArm);
        if (_sizeVecA != Vector3.zero && _sizeVecB != Vector3.zero)
            return (_sizeVecB - _sizeVecA).magnitude;

        return eyeHeight * 0.35f;
    }

    private float GetCapsuleHeightFromHeadProportion(VRCPlayerApi player, float eyeHeight)
    {
        _sizeVecA = player.GetBonePosition(HumanBodyBones.Neck);
        _sizeVecB = player.GetBonePosition(HumanBodyBones.Head);
        if (_sizeVecA != Vector3.zero && _sizeVecB != Vector3.zero && eyeHeight > 0.001f)
        {
            float headToNeckDistance = (_sizeVecB - _sizeVecA).magnitude;
            float headProportion = headToNeckDistance / eyeHeight;
            return eyeHeight * (1f + (headProportion * CapsuleHeadProportionBoost));
        }

        return eyeHeight * 1.05f;
    }

    public void RegisterPlayer(GameObject healthObjGo)
    {
        EnsureInitialized();
        UCS_PlayerHealthObjectBase healthObj = ResolveHealthObject(healthObjGo, nameof(RegisterPlayer));
        if (healthObj == null) return;

        for (int i = 0; i < _trackedPlayerCount; i++)
        {
            if (_trackedPlayers[i] != healthObj) continue;
            return;
        }

        if (_trackedPlayerCount >= _trackedPlayers.Length) return;
        _trackedPlayers[_trackedPlayerCount] = healthObj;
        _trackedPlayerCount++;
        healthObj.SetCurrentLOD(-1);
        SwitchPlayerLOD(healthObj, 0);
    }

    public void UnregisterPlayer(GameObject healthObjGo)
    {
        EnsureInitialized();
        UCS_PlayerHealthObjectBase healthObj = ResolveHealthObject(healthObjGo, nameof(UnregisterPlayer));
        if (healthObj == null) return;
        int currentLOD = healthObj.GetCurrentLOD();
        if (currentLOD >= 0) UnsubscribeHitboxes(healthObj.GetLODHitboxes(currentLOD));
        healthObj.SetCurrentLOD(-1);

        for (int i = 0; i < _trackedPlayerCount; i++)
        {
            if (_trackedPlayers[i] != healthObj) continue;
            _trackedPlayers[i] = _trackedPlayers[_trackedPlayerCount - 1];
            _trackedPlayers[_trackedPlayerCount - 1] = null;
            _trackedPlayerCount--;
            return;
        }
    }

    public void SwitchPlayerLOD(UCS_PlayerHealthObjectBase healthObj, int newLOD)
    {
        EnsureInitialized();
        if (healthObj == null) return;
        int oldLOD = healthObj.GetCurrentLOD();
        if (oldLOD >= 0 && healthObj.AreHitboxesActive())
        {
            UnsubscribeHitboxes(healthObj.GetLODHitboxes(oldLOD));
            healthObj.SetLODRootActive(oldLOD, false);
        }
        healthObj.SetCurrentLOD(newLOD);
        if (healthObj.AreHitboxesActive())
        {
            SubscribeHitboxes(healthObj.GetLODHitboxes(newLOD));
            healthObj.SetLODRootActive(newLOD, true);
        }
    }

    public void SetPlayerHitboxesActive(UCS_PlayerHealthObjectBase healthObj, bool active)
    {
        EnsureInitialized();
        if (healthObj == null) return;
        healthObj.SetHitboxesActive(active);
        int currentLOD = healthObj.GetCurrentLOD();
        if (currentLOD < 0) return;
        if (active)
            SubscribeHitboxes(healthObj.GetLODHitboxes(currentLOD));
        else
            UnsubscribeHitboxes(healthObj.GetLODHitboxes(currentLOD));
    }

    private void SubscribeHitboxes(UCS_PlayerHitbox[] hitboxes)
    {
        EnsureInitialized();
        if (hitboxes == null) return;
        foreach (UCS_PlayerHitbox hitbox in hitboxes)
        {
            if (hitbox == null || _activeCount >= _activeHitboxes.Length) continue;
            _activeHitboxes[_activeCount] = hitbox;
            _activeCount++;
        }
    }

    private void UnsubscribeHitboxes(UCS_PlayerHitbox[] hitboxes)
    {
        EnsureInitialized();
        if (hitboxes == null) return;
        foreach (UCS_PlayerHitbox hitbox in hitboxes)
        {
            if (hitbox == null) continue;
            for (int i = 0; i < _activeCount; i++)
            {
                if (_activeHitboxes[i] != hitbox) continue;
                _activeHitboxes[i] = _activeHitboxes[_activeCount - 1];
                _activeHitboxes[_activeCount - 1] = null;
                _activeCount--;
                break;
            }
        }
    }
}
