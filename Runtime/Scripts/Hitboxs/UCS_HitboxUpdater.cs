
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// NOTE (SDK follow-up): the per-frame position update below hand-tracks player bones. VRChat's
// Developer Update of 13 Aug 2026 announces native, cheaper helpers for this:
// VRCPlayerApi.AttachTransformToBone(Transform, HumanBodyBones) / DetachTransformFromBone(Transform),
// plus VRCBillboard.Register/Unregister for facing the viewer. Neither exists in SDK 3.10.5, so
// re-check once a newer SDK is installed. Caveat from the announcement: remote players behave the
// same as the manual approach (culled/hidden avatars and imposters are still unreliable), so
// attaching may cut the Udon cost but will not fix hitbox drift on those cases by itself.
// The matching TTT head-tag work (see MatchmakingFramework_ARCHITECTURE.md) should adopt it too.
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UCS_HitboxUpdater : UdonSharpBehaviour
{
    private const float FullBodyCapsuleWidthBoost = 1.7f;
    private const float CapsuleHeadProportionBoost = 2.0f;
    private const float EyeHeightResizeDelaySeconds = 1.0f;
    private const float PillFootDrop = 0.06f; // ankle -> sole, in eye heights

    [Tooltip("Torso box width/depth as a multiple of the upper-arm-root span. Tune to taste.")]
    [SerializeField] private float torsoWidthMultiplier = 1.3f;
    [SerializeField] private float torsoDepthMultiplier = 0.75f;

    [Header("Head sizing")]
    [Tooltip("Head bone -> eye midpoint distance on a realistic adult, as a fraction of eye height. Lower = bigger heads.")]
    [SerializeField] private float headToEyeRatio = 0.075f;
    [Tooltip("Left eye -> right eye distance on a realistic adult, as a fraction of eye height. Lower = bigger heads.")]
    [SerializeField] private float eyeSpanRatio = 0.04f;
    [Tooltip("Cap on measured head size, in multiples of eye height. Guards against misplaced eye bones.")]
    [SerializeField] private float maxHeadScaleRatio = 3f;
    [Tooltip("Flat multiplier on the head box size after measuring.")]
    [SerializeField] private float headSizeMultiplier = 1.15f;
    [Tooltip("Log each avatar's head measurements, for tuning the ratios above.")]
    [SerializeField] private bool logHeadSizing;

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
    private UCS_PlayerHitbox[] _sizeTier;
    private UCS_PlayerHitbox _sizeHitbox;
    private Vector3 _sizeVecA;
    private Vector3 _sizeVecB;
    private float _sizeHeadScale;

    // Position update working vars
    private int _posI;
    private UCS_PlayerHitbox _posHitbox;
    private Transform _posTransform;
    private VRCPlayerApi _posPlayer;
    private int _posTrackingType;
    private Vector3 _posVecA;
    private Vector3 _posVecB;
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
        UpdateOneLOD();
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
                Quaternion boneRot = _posPlayer.GetBoneRotation((HumanBodyBones)_posTrackingType);
                if (_posHitbox.boneOffset != Vector3.zero)
                    _posVecA += boneRot * (_posHitbox.boneOffset * (_posHitbox.offsetScale > 0f ? _posHitbox.offsetScale : _posPlayer.GetAvatarEyeHeightAsMeters()));
                _posTransform.SetPositionAndRotation(_posVecA, boneRot);
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.FullBodyPill)
            {
                // Spans feet -> head like the torso/leg boxes; GetPosition() is the playspace floor,
                // which leaves the pill on the ground when the player is playspaced up.
                _posVecB = GetFeetPosition(_posPlayer);
                if (_posVecB == Vector3.zero) _posVecB = _posPlayer.GetPosition();
                _posVecA = _posPlayer.GetBonePosition(HumanBodyBones.Head);
                if (_posVecA == Vector3.zero)
                    _posVecA = _posVecB + Vector3.up * _posPlayer.GetAvatarEyeHeightAsMeters();
                _posDir = _posVecA - _posVecB;
                if (_posDir.sqrMagnitude < 0.0001f) continue;
                _posDir = _posDir.normalized;
                // Foot bones sit at the ankle, so drop the base to the sole.
                _posVecB -= _posDir * (PillFootDrop * _posPlayer.GetAvatarEyeHeightAsMeters());
                _posTransform.position = _posVecB;
                _posTransform.up = _posDir; // capsule is radially symmetric, roll is irrelevant
                if (_posHitbox.scalePoint != null)
                {
                    Vector3 ps = _posHitbox.scalePoint.localScale;
                    // scaleBaseY holds the above-head allowance in metres (set in ApplySizingToTier).
                    ps.y = (Vector3.Distance(_posVecA, _posVecB) + _posHitbox.scaleBaseY) / ParentScale(_posHitbox.scalePoint).y;
                    _posHitbox.scalePoint.localScale = ps;
                }
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.TorsoBox)
            {
                _posVecA = _posPlayer.GetBonePosition(HumanBodyBones.Hips);
                _posVecB = _posPlayer.GetBonePosition(HumanBodyBones.Neck);
                if (_posVecA == Vector3.zero || _posVecB == Vector3.zero) continue;
                _posDir = _posVecB - _posVecA;
                // scalePoint pivots at the base of the box and extends along +Y (see the pill prefab).
                _posTransform.position = _posVecA;
                SetOrientation(_posTransform, _posDir.normalized, _posPlayer);
                if (_posHitbox.scalePoint != null)
                {
                    Vector3 ts = _posHitbox.scalePoint.localScale;
                    ts.y = _posDir.magnitude / ParentScale(_posHitbox.scalePoint).y;
                    _posHitbox.scalePoint.localScale = ts;
                }
                continue;
            }

            if (_posTrackingType == (int)HitboxTrackingType.LegBox)
            {
                _posVecA = _posPlayer.GetBonePosition(HumanBodyBones.Hips);
                if (_posVecA == Vector3.zero) continue;
                _posVecB = GetFeetPosition(_posPlayer);
                if (_posVecB == Vector3.zero)
                    _posVecB = new Vector3(_posVecA.x, _posPlayer.GetPosition().y, _posVecA.z);
                _posDir = _posVecB - _posVecA;
                // Base sits at the feet, box grows up towards the hips.
                _posTransform.position = _posVecB;
                SetOrientation(_posTransform, -_posDir.normalized, _posPlayer);
                if (_posHitbox.scalePoint != null)
                {
                    Vector3 s = _posHitbox.scalePoint.localScale;
                    s.y = _posDir.magnitude / ParentScale(_posHitbox.scalePoint).y;
                    _posHitbox.scalePoint.localScale = s;
                }
            }
        }
    }

    // Average of both feet, or whichever single foot bone exists. Vector3.zero when neither does.
    private Vector3 GetFeetPosition(VRCPlayerApi player)
    {
        Vector3 left = player.GetBonePosition(HumanBodyBones.LeftFoot);
        Vector3 right = player.GetBonePosition(HumanBodyBones.RightFoot);
        if (left != Vector3.zero && right != Vector3.zero) return (left + right) * 0.5f;
        if (left != Vector3.zero) return left;
        return right;
    }

    // Sizes below are in metres, but localScale is relative to the parent chain.
    // Divide by the parent's world scale so a scaled rig doesn't shrink the hitboxes.
    private Vector3 ParentScale(Transform t)
    {
        if (t.parent == null) return Vector3.one;
        Vector3 s = t.parent.lossyScale;
        if (Mathf.Abs(s.x) < 0.0001f) s.x = 1f;
        if (Mathf.Abs(s.y) < 0.0001f) s.y = 1f;
        if (Mathf.Abs(s.z) < 0.0001f) s.z = 1f;
        return s;
    }

    // Up alone leaves roll unconstrained, which is what flipped the box's width/depth axes.
    // Roll is pinned to the hips' facing so X stays width and Z stays depth.
    private void SetOrientation(Transform target, Vector3 up, VRCPlayerApi player)
    {
        Vector3 forward = Vector3.ProjectOnPlane(player.GetBoneRotation(HumanBodyBones.Hips) * Vector3.forward, up);
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.ProjectOnPlane(player.GetRotation() * Vector3.forward, up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            target.up = up;
            return;
        }
        target.rotation = Quaternion.LookRotation(forward.normalized, up);
    }

    // ponytail: one player re-evaluated per frame; fine to ~80 players, go per-frame-all if switching lags visibly
    private int _lodCursor;

    private void UpdateOneLOD()
    {
        if (_trackedPlayerCount <= 0) return;
        if (_lodCursor >= _trackedPlayerCount) _lodCursor = 0;
        UCS_PlayerHealthObjectBase healthObj = _trackedPlayers[_lodCursor];
        _lodCursor++;
        if (healthObj == null) return;

        VRCPlayerApi tracked = healthObj.GetTrackedPlayer();
        VRCPlayerApi local = Networking.LocalPlayer;
        if (tracked == null || !tracked.IsValid() || local == null) return;

        SwitchPlayerLOD(healthObj, healthObj.GetLODForDistance(Vector3.Distance(local.GetPosition(), tracked.GetPosition())));
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

        _sizeHeadScale = MeasureHeadScale(player, player.GetAvatarEyeHeightAsMeters());
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
                // Y is updated per-frame in PostLateUpdate; only set X/Z here.
                float capsuleWidth = GetUpperArmSpan(player, eyeHeight) * FullBodyCapsuleWidthBoost;
                Vector3 pillParent = ParentScale(_sizeHitbox.scalePoint);
                Vector3 pillScale = _sizeHitbox.scalePoint.localScale;
                pillScale.x = capsuleWidth / pillParent.x;
                pillScale.z = capsuleWidth / pillParent.z;
                _sizeHitbox.scalePoint.localScale = pillScale;
                _sizeHitbox.scaleBaseY = GetHeadTopAllowance(player, eyeHeight);
                continue;
            }

            if (_sizeHitbox.trackingBone == HitboxTrackingType.TorsoBox)
            {
                // Y is updated per-frame in PostLateUpdate; only set X/Z here
                float upperArmSpan = GetUpperArmSpan(player, eyeHeight);
                Vector3 torsoParent = ParentScale(_sizeHitbox.scalePoint);
                Vector3 torsoScale = _sizeHitbox.scalePoint.localScale;
                torsoScale.x = upperArmSpan * torsoWidthMultiplier / torsoParent.x;
                torsoScale.z = upperArmSpan * torsoDepthMultiplier / torsoParent.z;
                _sizeHitbox.scalePoint.localScale = torsoScale;
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
                    (_sizeVecB - _sizeVecA).magnitude / ParentScale(_sizeHitbox.scalePoint).y,
                    _sizeHitbox.scaleBaseZ * eyeHeight);
                continue;
            }

            // Head: eye height lies for chibi/big-head avatars, so size and offset off the measured head.
            if (_sizeHitbox.trackingBone == HitboxTrackingType.Head)
            {
                float h = _sizeHeadScale * headSizeMultiplier;
                _sizeHitbox.scalePoint.localScale = new Vector3(
                    _sizeHitbox.scaleBaseX * h,
                    _sizeHitbox.scaleBaseY * h,
                    _sizeHitbox.scaleBaseZ * h);
                _sizeHitbox.offsetScale = _sizeHeadScale;
                continue;
            }

            // Bone-tracked with no target bone (hands, feet): uniform scale off eye height.
            if ((int)_sizeHitbox.trackingBone >= 0)
            {
                _sizeHitbox.scalePoint.localScale = new Vector3(
                    _sizeHitbox.scaleBaseX * eyeHeight,
                    _sizeHitbox.scaleBaseY * eyeHeight,
                    _sizeHitbox.scaleBaseZ * eyeHeight);
            }
        }
    }

    private float GetUpperArmSpan(VRCPlayerApi player, float eyeHeight)
    {
        _sizeVecA = player.GetBonePosition(HumanBodyBones.LeftUpperArm);
        _sizeVecB = player.GetBonePosition(HumanBodyBones.RightUpperArm);
        if (_sizeVecA != Vector3.zero && _sizeVecB != Vector3.zero)
            return (_sizeVecB - _sizeVecA).magnitude;

        return eyeHeight * 0.35f;
    }

    // How far the pill extends above the head bone, in metres. Measured once per resize
    // off the neck->head span so big-headed avatars still get covered.
    private float GetHeadTopAllowance(VRCPlayerApi player, float eyeHeight)
    {
        _sizeVecA = player.GetBonePosition(HumanBodyBones.Neck);
        _sizeVecB = player.GetBonePosition(HumanBodyBones.Head);
        float fromHeadScale = _sizeHeadScale * 0.15f;
        if (_sizeVecA != Vector3.zero && _sizeVecB != Vector3.zero)
            return Mathf.Max((_sizeVecB - _sizeVecA).magnitude * CapsuleHeadProportionBoost, fromHeadScale);

        return Mathf.Max(eyeHeight * 0.15f, fromHeadScale);
    }

    // Head size expressed as "the eye height a realistic human with this head would have", so the
    // existing eye-height-based head box tuning still applies. Eyes scale with the head, not the body,
    // which is what catches chibi avatars. Each estimate is converted via the realistic-human ratio and
    // the largest wins (missing bones just drop out). Never smaller than eye height, so realistic
    // avatars size exactly as before.
    // ponytail: two linear ratios, not a classifier; tune the ratios from logHeadSizing output if heads read off
    private float MeasureHeadScale(VRCPlayerApi player, float eyeHeight)
    {
        float scale = eyeHeight;
        Vector3 head = player.GetBonePosition(HumanBodyBones.Head);
        Vector3 left = player.GetBonePosition(HumanBodyBones.LeftEye);
        Vector3 right = player.GetBonePosition(HumanBodyBones.RightEye);
        float fromDepth = 0f;
        float fromSpan = 0f;

        Vector3 eyeMid = Vector3.zero;
        if (left != Vector3.zero && right != Vector3.zero)
        {
            eyeMid = (left + right) * 0.5f;
            fromSpan = Vector3.Distance(left, right) / eyeSpanRatio;
        }
        else if (left != Vector3.zero) eyeMid = left;
        else if (right != Vector3.zero) eyeMid = right;

        if (head != Vector3.zero && eyeMid != Vector3.zero)
            fromDepth = Vector3.Distance(head, eyeMid) / headToEyeRatio;

        scale = Mathf.Max(scale, Mathf.Max(fromDepth, fromSpan));
        scale = Mathf.Min(scale, eyeHeight * maxHeadScaleRatio);

        if (logHeadSizing)
            Debug.Log("[UCS_HitboxUpdater] head sizing " + player.displayName + ": eyeHeight=" + eyeHeight
                + " fromDepth=" + fromDepth + " fromSpan=" + fromSpan + " -> " + scale + " (x" + (scale / eyeHeight) + ")");
        return scale;
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
        // Size once on join; OnAvatarEyeHeightChanged may never fire for an already-loaded avatar.
        // Goes through the delayed queue so the bones have settled before we measure them.
        VRCPlayerApi joined = healthObj.GetTrackedPlayer();
        if (joined == null || !joined.IsValid()) return;
        QueuePlayerForDelayedEyeHeightResize(joined.playerId);
        if (_delayedEyeHeightResizePending) return;
        _delayedEyeHeightResizePending = true;
        SendCustomEventDelayedSeconds(nameof(ApplyDelayedEyeHeightResize), EyeHeightResizeDelaySeconds);
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
        if (oldLOD == newLOD) return;
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
