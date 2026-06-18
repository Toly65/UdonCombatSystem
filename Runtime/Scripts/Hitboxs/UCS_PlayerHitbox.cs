
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.UdonNetworkCalling;

public enum HitboxTrackingType
{
    None = -1,
    Hips = 0,
    LeftUpperLeg = 1,
    RightUpperLeg = 2,
    LeftLowerLeg = 3,
    RightLowerLeg = 4,
    LeftFoot = 5,
    RightFoot = 6,
    Spine = 7,
    Chest = 8,
    Neck = 9,
    Head = 10,
    LeftShoulder = 11,
    RightShoulder = 12,
    LeftUpperArm = 13,
    RightUpperArm = 14,
    LeftLowerArm = 15,
    RightLowerArm = 16,
    LeftHand = 17,
    RightHand = 18,
    TorsoBox = 100,
    LegBox = 101,
    FullBodyPill = 102,
}
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]

public class UCS_PlayerHitbox : UCS_Hitbox
{
    private const int NoTeam = -1;

    [SerializeField] private UCS_PlayerHealthObjectBase healthObject;
    [SerializeField] private int DamageMultiplier = 1;
    [HideInInspector][SerializeField] private int teamID = NoTeam;

    public HitboxTrackingType trackingBone;
    public HitboxTrackingType targetBone = HitboxTrackingType.None;

    // Scaled to fit avatar dimensions on eye height change; base X/Z captured from initial localScale
    public Transform scalePoint;
    [HideInInspector] public float scaleBaseX;
    [HideInInspector] public float scaleBaseZ;

    private VRCPlayerApi _trackedPlayer;
    private VRCPlayerApi localPlayer;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (scalePoint != null)
        {
            scaleBaseX = scalePoint.localScale.x;
            scaleBaseZ = scalePoint.localScale.z;
        }
        PropagateTeamIDToAttachedHitboxes();
    }

    public void SetTrackedPlayer(VRCPlayerApi player) { _trackedPlayer = player; }
    public VRCPlayerApi GetTrackedPlayer() => _trackedPlayer;

    public void SetTeamID(int newTeamID)
    {
        if (teamID == newTeamID) return;
        teamID = newTeamID;
        PropagateTeamIDToAttachedHitboxes();
    }

    public int GetTeamID() => teamID;

    private bool IsSameValidTeam(int otherTeamID)
    {
        if (teamID == NoTeam || otherTeamID == NoTeam) return false;
        return teamID == otherTeamID;
    }

    private bool ShouldApplyHitFromTeam(int otherTeamID) => !IsSameValidTeam(otherTeamID);

    public void PropagateTeamIDToAttachedHitboxes()
    {
        if (hitboxes == null) return;

        foreach (Collider col in hitboxes)
        {
            if (col == null) continue;
            UCS_PlayerHitbox attachedPlayerHitbox = col.gameObject.GetComponent<UCS_PlayerHitbox>();
            if (attachedPlayerHitbox != null && attachedPlayerHitbox != this)
                attachedPlayerHitbox.SetTeamID(teamID);
        }
    }

    public override void HitEvent(int damage, int BlamedPlayerID, int attackerTeamID, int damageType)
    {
        if (localPlayer == null) localPlayer = Networking.LocalPlayer;
        if (localPlayer == null || localPlayer.playerId != BlamedPlayerID) return;
        if (!ShouldApplyHitFromTeam(attackerTeamID)) return;

        if (damage > 0) damage *= DamageMultiplier;

        if (healthObject == null) return;

        healthObject.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(UCS_PlayerHealthObjectBase.ApplyDamage), damage, BlamedPlayerID, attackerTeamID, damageType);
    }
}
