
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.UdonNetworkCalling;

public enum HitboxLODLevel
{
    Pill = 0,
    SegmentedPill = 1,
    Articulated = 2,
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_PlayerHealthObjectBase : UdonSharpBehaviour
{
    private const int NoTeam = -1;

    [UdonSynced] private float health = 100f;
    [UdonSynced] private int lastBlamedPlayerID = -1;
    [UdonSynced] private int lastDamageType = -1;
    [UdonSynced] private int PlayerTeamID = NoTeam;

    private bool friendlyFireEnabled = false;
    [SerializeField] public float maxHealth = 100f;
    [SerializeField] private float syncIntervalSeconds = 0.25f;
    [SerializeField] private float respawnDelaySeconds = 5f;

    [SerializeField] private UCS_HitboxUpdater _hitboxUpdater;
    [SerializeField] private UCS_HealthBarsManager _localHealthBar;
    [SerializeField] private UCS_DeathHandlerBase _deathHandler;

    [Header("pill hitbox")]
    public GameObject lod0Root;
    public UCS_PlayerHitbox[] lod0Hitboxes;
    [Header("segmented Pill")]
    public GameObject lod1Root;
    public UCS_PlayerHitbox[] lod1Hitboxes;
    [Header("Articulated hitboxes")]
    public GameObject lod2Root;
    public UCS_PlayerHitbox[] lod2Hitboxes;
    public HitboxLODLevel maxLOD = HitboxLODLevel.Articulated;

    private int _currentLOD = -1;
    private VRCPlayerApi _trackedPlayer;
    private bool _hitboxesActive = true;

    private float shadowHealth = 100f;
    private bool healthDirty;
    private bool healthSyncQueued;
    private bool deathHandled;

    private void Start()
    {
        shadowHealth = Mathf.Clamp(health, 0f, maxHealth);
        deathHandled = shadowHealth <= 0f;
        SetTrackedPlayer(Networking.GetOwner(gameObject));
        PushLocalHealthBarUpdate();
    }

    public override void OnDeserialization()
    {
        shadowHealth = Mathf.Clamp(health, 0f, maxHealth);
        deathHandled = shadowHealth <= 0f;
        healthDirty = false;
        healthSyncQueued = false;
        PushLocalHealthBarUpdate();
    }

    public float GetShadowHealth() => shadowHealth;
    public float GetSyncedHealth() => health;

    public int GetCurrentLOD() => _currentLOD;
    public void SetCurrentLOD(int level) { _currentLOD = level; }

    public VRCPlayerApi GetTrackedPlayer() => _trackedPlayer;
    public bool AreHitboxesActive() => _hitboxesActive;

    public void SetHitboxesActive(bool active)
    {
        _hitboxesActive = active;
        SetLODRootActive(_currentLOD, active);
    }

    public void SetLODRootActive(int level, bool active)
    {
        GameObject root = GetLODRoot(level);
        if (root != null) root.SetActive(active);
    }

    public GameObject GetLODRoot(int level)
    {
        if (level == 0) return lod0Root;
        if (level == 1) return lod1Root;
        if (level == 2) return lod2Root;
        return null;
    }

    public void SetTrackedPlayer(VRCPlayerApi player)
    {
        _trackedPlayer = player;
        PropagateTrackedPlayer(lod0Hitboxes, player);
        PropagateTrackedPlayer(lod1Hitboxes, player);
        PropagateTrackedPlayer(lod2Hitboxes, player);
        PushLocalHealthBarUpdate();
        if (_hitboxUpdater == null) return;
        if (player != null)
            _hitboxUpdater.RegisterPlayer(gameObject);
        else
            _hitboxUpdater.UnregisterPlayer(gameObject);
    }

    void OnDisable()
    {
        if (_hitboxUpdater != null && _trackedPlayer != null)
            _hitboxUpdater.UnregisterPlayer(gameObject);
    }

    public UCS_PlayerHitbox[] GetLODHitboxes(int level)
    {
        if (level == 0) return lod0Hitboxes;
        if (level == 1) return lod1Hitboxes;
        if (level == 2) return lod2Hitboxes;
        return null;
    }

    private void PropagateTrackedPlayer(UCS_PlayerHitbox[] hitboxes, VRCPlayerApi player)
    {
        if (hitboxes == null) return;
        foreach (UCS_PlayerHitbox hitbox in hitboxes)
        {
            if (hitbox != null)
                hitbox.SetTrackedPlayer(player);
        }
    }

    [NetworkCallable]
    public void ApplyDamage(int damage, int BlamedPlayerID, int TeamID, int damageType)
    {
        bool sameValidTeam = TeamID != NoTeam && PlayerTeamID != NoTeam && TeamID == PlayerTeamID;
        if (!friendlyFireEnabled && sameValidTeam)
        {
            return;
        }

        shadowHealth -= damage;
        shadowHealth = Mathf.Clamp(shadowHealth, 0f, maxHealth);
        PushLocalHealthBarUpdate();

        lastBlamedPlayerID = BlamedPlayerID;
        lastDamageType = damageType;

        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        health -= damage;
        health = Mathf.Clamp(health, 0f, maxHealth);

        if (health <= 0f)
        {
            TryHandleDeath();
        }
        else
        {
            deathHandled = false;
        }

        healthDirty = true;
        QueueHealthSync();
    }

    private void TryHandleDeath()
    {
        if (deathHandled || _deathHandler == null)
        {
            return;
        }

        VRCPlayerApi targetPlayer = _trackedPlayer;
        if (!Utilities.IsValid(targetPlayer))
        {
            targetPlayer = Networking.LocalPlayer;
        }

        if (!Utilities.IsValid(targetPlayer))
        {
            return;
        }

        deathHandled = true;
        _deathHandler.HandlePlayerDeath(targetPlayer);

        // Schedule health restoration to match the legacy combat system's respawn delay.
        // Since the legacy system provides no respawn callback, we use an equally-timed
        // delayed event so the player respawns with full health.
        SendCustomEventDelayedSeconds(nameof(RespawnRestoreHealth), respawnDelaySeconds);
    }

    public void RespawnRestoreHealth()
    {
        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        health = maxHealth;
        shadowHealth = maxHealth;
        deathHandled = false;
        healthDirty = true;
        QueueHealthSync();
        PushLocalHealthBarUpdate();
    }

    private void PushLocalHealthBarUpdate()
    {
        if (_localHealthBar == null || !Utilities.IsValid(Networking.LocalPlayer))
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        if (_trackedPlayer == null || !_trackedPlayer.IsValid())
        {
            return;
        }

        if (_trackedPlayer.playerId != Networking.LocalPlayer.playerId)
        {
            return;
        }

        _localHealthBar.PushHealthUpdate(shadowHealth, maxHealth);
    }

    public void QueueHealthSync()
    {
        if (!Networking.IsOwner(gameObject) || healthSyncQueued)
        {
            return;
        }

        healthSyncQueued = true;
        SendCustomEventDelayedSeconds(nameof(FlushHealthSync), syncIntervalSeconds);
    }

    public void FlushHealthSync()
    {
        healthSyncQueued = false;

        if (!Networking.IsOwner(gameObject) || !healthDirty)
        {
            return;
        }

        healthDirty = false;
        RequestSerialization();
    }
}
