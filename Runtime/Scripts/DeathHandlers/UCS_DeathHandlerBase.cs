using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_DeathHandlerBase : UdonSharpBehaviour
{
    [Header("Respawn Settings")]
    [SerializeField] protected Transform[] validRespawnTransforms;
    [SerializeField] protected bool pickRandomRespawn;
    [SerializeField] protected int specificRespawnIndex;

    // Override target set via SetRespawnTransform(); takes priority over the array
    private Transform overrideRespawnTransform;

    public virtual void HandlePlayerDeath()
    {
    }

    public virtual void HandlePlayerDeath(VRCPlayerApi player)
    {
    }

    /// <summary>
    /// Override the current respawn logic to always use the given transform.
    /// Pass null to clear the override and fall back to the array/index system.
    /// </summary>
    public void SetRespawnTransform(Transform spawnPoint)
    {
        overrideRespawnTransform = spawnPoint;
    }

    /// <summary>
    /// Toggle random selection from the validRespawnTransforms array on or off.
    /// </summary>
    public void SetRandomRespawn(bool enable)
    {
        pickRandomRespawn = enable;
    }

    public virtual Transform GetRespawnTransform()
    {
        // Explicit override takes priority
        if (overrideRespawnTransform != null)
        {
            return overrideRespawnTransform;
        }

        if (validRespawnTransforms == null || validRespawnTransforms.Length == 0)
        {
            return null;
        }

        if (pickRandomRespawn)
        {
            int randomIndex = Random.Range(0, validRespawnTransforms.Length);
            return validRespawnTransforms[randomIndex];
        }

        int clampedIndex = Mathf.Clamp(specificRespawnIndex, 0, validRespawnTransforms.Length - 1);
        return validRespawnTransforms[clampedIndex];
    }
}
