
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]
public class UCS_TeleportDeathHandler : UCS_DeathHandlerBase
{
    [Header("Teleport Death Settings")]
    [Tooltip("If enabled, the player is teleported to deathBoxTransform on death before respawning.")]
    [SerializeField] private bool useDeathBox = true;

    [Tooltip("Transform that the player is teleported to on death (e.g. below the map).")]
    [SerializeField] private Transform deathBoxTransform;

    [Tooltip("Seconds after death before the player is teleported back to the respawn point.")]
    [SerializeField] private float respawnDelaySeconds = 5f;

    // Cached references to avoid allocating local variables at runtime
    private VRCPlayerApi _playerCache;
    private Transform _transformCache;

    /// <summary>
    /// Teleport the local player to the death box on death.
    /// </summary>
    public override void HandlePlayerDeath()
    {
        _playerCache = Networking.LocalPlayer;
        if (!Utilities.IsValid(_playerCache))
        {
            return;
        }

        HandlePlayerDeath(_playerCache);
    }

    /// <summary>
    /// Teleport the specified player to the death box on death.
    /// Only teleports if the player is the local player (remote players
    /// cannot be teleported by other clients in VRChat).
    /// </summary>
    public override void HandlePlayerDeath(VRCPlayerApi player)
    {
        if (!Utilities.IsValid(player))
        {
            return;
        }

        if (useDeathBox && deathBoxTransform != null)
        {
            player.TeleportTo(deathBoxTransform.position, deathBoxTransform.rotation);
            SendCustomEventDelayedSeconds(nameof(TeleportToSpawn), respawnDelaySeconds);
        }
        else
        {
            TeleportToSpawnWithPlayer(player);
        }
    }

    /// <summary>
    /// Teleport the local player back to the configured respawn transform.
    /// Called automatically after the respawn delay when using the death box.
    /// </summary>
    public void TeleportToSpawn()
    {
        _playerCache = Networking.LocalPlayer;
        if (!Utilities.IsValid(_playerCache))
        {
            return;
        }

        TeleportToSpawnWithPlayer(_playerCache);
    }

    private void TeleportToSpawnWithPlayer(VRCPlayerApi player)
    {
        _transformCache = GetRespawnTransform();
        if (_transformCache == null)
        {
            return;
        }

        player.TeleportTo(_transformCache.position, _transformCache.rotation);
    }

}
