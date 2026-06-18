
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;
using VRC.SDK3.UdonNetworkCalling;
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UCS_LegacyRagdollDeathHandler : UCS_DeathHandlerBase
{
    [Header("Legacy Combat Respawn")]
    [SerializeField] private bool respawnOnDeath = true;
    [SerializeField] private float respawnDelaySeconds = 5f;

    [SerializeField] private GameObject overrideDamageGraphic;

    private VRCPlayerApi localPlayer;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        InitializeLegacyCombatSystem(localPlayer);
    }

    public override void HandlePlayerDeath()
    {
        if (!Utilities.IsValid(localPlayer))
        {
            localPlayer = Networking.LocalPlayer;
        }

        if (!Utilities.IsValid(localPlayer))
        {
            return;
        }

        HandlePlayerDeath(localPlayer);
    }

    public override void HandlePlayerDeath(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player))
        {
            return;
        }

        SendCustomNetworkEvent(NetworkEventTarget.All, nameof(ApplyNetworkedDeath), player.playerId);
    }

    [NetworkCallable]
    public void ApplyNetworkedDeath(int playerId)
    {
        VRCPlayerApi targetPlayer = VRCPlayerApi.GetPlayerById(playerId);
        if (!Utilities.IsValid(targetPlayer))
        {
            return;
        }

        Transform respawnTransform = GetRespawnTransform();
        targetPlayer.CombatSetRespawn(respawnOnDeath, respawnDelaySeconds, respawnTransform);

        targetPlayer.CombatSetCurrentHitpoints(0);
    }

    private void InitializeLegacyCombatSystem(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player))
        {
            return;
        }

        // CombatSetup() is what actually creates the combat helper. Every other
        // Combat* call is a no-op until it exists, so it MUST run first - otherwise
        // CombatSetDamageGraphic below is silently discarded.
        player.CombatSetup();

        // Suppress the damage vignette. A null graphic does NOT suppress it: in the
        // editor/ClientSim a null prefab makes the helper fall back to the stock
        // VRCPlayerVisualDamage prefab, and the VRC_VisualDamage script is destroyed
        // on spawn so it can never fade itself out - leaving the vignette stuck on.
        // Passing a non-null empty object instead means nothing visible is ever shown.
        player.CombatSetDamageGraphic(overrideDamageGraphic);

        player.CombatSetMaxHitpoints(100f);
        Transform spawnPoint = GetRespawnTransform();
        player.CombatSetRespawn(true, respawnDelaySeconds, spawnPoint);
        player.CombatSetCurrentHitpoints(100f);
    }
}
