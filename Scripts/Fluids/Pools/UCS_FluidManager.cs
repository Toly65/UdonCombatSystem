using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[System.Flags]
public enum UCS_FluidProperty
{
    None = 0,
    Flammable = 1,
    Water = 2,
    Smothering = 4,
    Gas = 8,
    GasFlammable = 16
}

public class UCS_FluidManager : UdonSharpBehaviour
{
    [Header("Fluid Properties")]
    public UCS_FluidProperty fluidProperties = UCS_FluidProperty.None;

    [Header("Pool Setup")]
    public Transform fluidPoolParent;
    [SerializeField] private GameObject poolExample;
    [SerializeField, Min(1)] private int poolSize = 4;
    [SerializeField] private UCS_FluidPool[] _pools;

    [Header("Pool Defaults")]
    public UCS_FireNodeManager poolFireNodeManager;
    [SerializeField, Min(0f)] private float poolMaxVolume = 1f;
    [SerializeField, Min(0f)] private float poolIgniteVolumeThreshold = 0.05f;
    [SerializeField, Min(0f)] private float poolBurnRate = 0.05f;
    [SerializeField] private bool poolAllowIgnition = true;

    [Tooltip("Distance under which a new flow merges into an existing live pool.")]
    public float depositMergeRadius = 0.4f;

    [Tooltip("Max serialize rate while events are pending (Hz inverse).")]
    public float networkTickRate = 0.1f;

    [Tooltip("Forced shadow refresh interval to bound prediction drift.")]
    public float shadowPulseSec = 5f;

    public int StartFlowLocal(Vector3 worldPos, float volumePerSecond)
    {
        int slot = FindLiveSlotNear(worldPos, depositMergeRadius);
        if (slot < 0)
        {
            slot = AllocateFreeSlot();
            if (slot < 0)
            {
                slot = EvictOldestLive();
            }
            if (slot < 0) return -1;

            UCS_FluidPool allocated = GetPool(slot);
            if (allocated == null) return -1;

            allocated.BeginLive(worldPos);
            allocated.SetFlowRate(volumePerSecond);
            return slot;
        }

        UCS_FluidPool pool = GetPool(slot);
        if (pool == null) return -1;

        pool.SnapshotShadow();
        pool.SetFlowRate(volumePerSecond);
        return slot;
    }

    public void StopFlowLocal(int slot, int expectedGeneration)
    {
        UCS_FluidPool pool = GetPool(slot);
        if (pool == null || pool.GetGeneration() != expectedGeneration) return;

        pool.SnapshotShadow();
        pool.SetFlowRate(0f);
    }

    public void IgniteLocal(int slot, int expectedGeneration)
    {
        UCS_FluidPool pool = GetPool(slot);
        if (pool == null || pool.GetGeneration() != expectedGeneration) return;
        if (!CanIgnite()) return;

        pool.SnapshotShadow();
        pool.SetBurning(true);
    }

    public void ExtinguishLocal(int slot, int expectedGeneration)
    {
        UCS_FluidPool pool = GetPool(slot);
        if (pool == null || pool.GetGeneration() != expectedGeneration) return;

        pool.SnapshotShadow();
        pool.SetBurning(false);
    }

    public int SplashLocal(Vector3 worldPos, float volume, bool igniteOnAdd)
    {
        int slot = FindLiveSlotNear(worldPos, depositMergeRadius);
        if (slot < 0)
        {
            slot = AllocateFreeSlot();
            if (slot < 0)
            {
                slot = EvictOldestLive();
            }
            if (slot < 0) return -1;

            UCS_FluidPool allocated = GetPool(slot);
            if (allocated == null) return -1;

            allocated.BeginLive(worldPos);
            allocated.AddVolume(volume);
            if (igniteOnAdd && CanIgnite())
            {
                allocated.SetBurning(true);
            }
            return slot;
        }

        UCS_FluidPool pool = GetPool(slot);
        if (pool == null) return -1;

        pool.SnapshotShadow();
        pool.AddVolume(volume);
        if (igniteOnAdd && CanIgnite())
        {
            pool.SetBurning(true);
        }
        return slot;
    }

    private UCS_FluidPool GetPool(int slot)
    {
        if (_pools == null || slot < 0 || slot >= _pools.Length) return null;
        return _pools[slot];
    }

    private int FindLiveSlotNear(Vector3 worldPos, float radius)
    {
        if (_pools == null) return -1;

        float r2 = radius * radius;
        int best = -1;
        float bestD = float.PositiveInfinity;

        for (int i = 0; i < _pools.Length; i++)
        {
            UCS_FluidPool pool = _pools[i];
            if (pool == null || !pool.IsActivePool()) continue;

            float d2 = (pool.transform.position - worldPos).sqrMagnitude;
            if (d2 <= r2 && d2 < bestD)
            {
                best = i;
                bestD = d2;
            }
        }

        return best;
    }

    private int AllocateFreeSlot()
    {
        if (_pools == null) return -1;

        for (int i = 0; i < _pools.Length; i++)
        {
            UCS_FluidPool pool = _pools[i];
            if (pool != null && !pool.IsActivePool()) return i;
        }

        return -1;
    }

    private int EvictOldestLive()
    {
        if (_pools == null) return -1;

        int best = -1;
        int bestGen = int.MaxValue;

        for (int i = 0; i < _pools.Length; i++)
        {
            UCS_FluidPool pool = _pools[i];
            if (pool == null || !pool.IsActivePool()) continue;

            int gen = pool.GetGeneration();
            if (gen < bestGen)
            {
                best = i;
                bestGen = gen;
            }
        }

        if (best < 0) return -1;

        UCS_FluidPool bestPool = _pools[best];
        if (bestPool == null) return -1;

        bestPool.Recycle();
        return best;
    }

    public bool HasProperty(UCS_FluidProperty property)
    {
        int props = (int)fluidProperties;
        int mask = (int)property;
        return (props & mask) == mask;
    }

    public UCS_FluidProperty GetFluidProperties() { return fluidProperties; }

    public bool CanIgnite()
    {
        return HasProperty(UCS_FluidProperty.Flammable) || HasProperty(UCS_FluidProperty.GasFlammable);
    }

    public int FindNearbySlot(Vector3 worldPos, float radius)
    {
        return FindLiveSlotNear(worldPos, radius);
    }

    public int GetSlotGeneration(int slot)
    {
        UCS_FluidPool pool = GetPool(slot);
        if (pool == null) return -1;
        return pool.GetGeneration();
    }

    public bool IsSlotLive(int slot)
    {
        UCS_FluidPool pool = GetPool(slot);
        return pool != null && pool.IsActivePool();
    }

    public bool IsSlotBurning(int slot)
    {
        UCS_FluidPool pool = GetPool(slot);
        return pool != null && pool.IsBurning;
    }

    public UCS_FluidPool GetLocalPoolBySlot(int slot)
    {
        return GetPool(slot);
    }

    public int FindSlotForPool(UCS_FluidPool pool)
    {
        if (_pools == null || pool == null) return -1;

        for (int i = 0; i < _pools.Length; i++)
        {
            if (_pools[i] == pool) return i;
        }

        return -1;
    }
}