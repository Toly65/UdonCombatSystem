
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]

public class UCS_FluidPoolSpawner : UdonSharpBehaviour
{
    [Header("Source")]
    public UCS_FluidManager targetFluidManager;
    public Transform spawnPoint;

    [Header("Spawn")]
    [SerializeField, Min(0f)] private float spawnVolume = 0.5f;
    [SerializeField] private bool igniteOnSpawn = false;
    [SerializeField] private bool spawnOnStart = false;

    [Header("Surface Raycast")]
    [SerializeField, Min(0.1f)] private float raycastDistance = 10f;
    [SerializeField] private LayerMask surfaceMask = ~0;

    [Header("Runtime")]
    [SerializeField] private int lastSpawnedSlot = -1;
    [SerializeField] private int lastSpawnedGeneration = -1;
    [UdonSynced] private bool _hasSpawned;

    public void Start()
    {
        if (!spawnOnStart || _hasSpawned)
        {
            return;
        }

        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        SpawnPool();
    }

    public void SpawnPool()
    {
        if (targetFluidManager == null || _hasSpawned)
        {
            return;
        }

        Vector3 castOrigin = spawnPoint != null ? spawnPoint.position : transform.position;
        if (!TryGetFloorSpawnPosition(castOrigin, out Vector3 position))
        {
            return;
        }

        int slot = targetFluidManager.SplashLocal(position, Mathf.Max(0f, spawnVolume), igniteOnSpawn);
        if (slot < 0)
        {
            return;
        }

        lastSpawnedSlot = slot;
        lastSpawnedGeneration = targetFluidManager.GetSlotGeneration(slot);
        _hasSpawned = true;
        RequestSerialization();
    }

    private bool TryGetFloorSpawnPosition(Vector3 castOrigin, out Vector3 floorPosition)
    {
        Vector3 rayStart = castOrigin + Vector3.up * 0.05f;
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, raycastDistance, surfaceMask, QueryTriggerInteraction.Ignore))
        {
            floorPosition = hit.point;
            return true;
        }

        floorPosition = Vector3.zero;
        return false;
    }
}
