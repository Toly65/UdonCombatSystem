
// =============================================================================
// UCS_FluidTrail — DRAFT, ring buffer of recently-deposited pools with
// generation guards. Igniting the trail cascades fire newest-to-oldest along
// the path the spout walked.
//
// Trail itself is local-only. The actual ignite events go through the manager,
// so cascade propagation is naturally network-correct (every client's manager
// hears one IgniteLocal call per slot in the cascade).
// =============================================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_FluidTrail : UdonSharpBehaviour
{
    [Header("Trail Config")]
    [Tooltip("Number of recent pool registrations to remember.")]
    [SerializeField, Min(1)] private int trailLength = 16;

    [Tooltip("Seconds between igniting each pool in the cascade. 0 = ignite all at once.")]
    public float cascadeIntervalSeconds = 0.05f;

    [Tooltip("Reset the buffer when EndTrail is called (true) or persist across firings (false).")]
    public bool clearOnEndTrail = false;

    public bool isActive;

    // ---------------- Ring buffer ----------------
    private int[] _slots;
    private int[] _gens;
    private UCS_FluidManager[] _mgrs;
    private int _writeIndex;
    private int _count;

    // ---------------- Cascade state ----------------
    private int _cascadeCursor;
    private int _cascadeRemaining;

    private void Start()
    {
        EnsureBuffer();
        isActive = false;
    }

    private void EnsureBuffer()
    {
        if (_slots != null && _slots.Length == trailLength) return;
        _slots = new int[trailLength];
        _gens  = new int[trailLength];
        _mgrs  = new UCS_FluidManager[trailLength];
        for (int i = 0; i < trailLength; i++) _slots[i] = -1;
        _writeIndex = 0;
        _count = 0;
    }

    public void BeginTrail()
    {
        EnsureBuffer();
        isActive = true;
    }

    public void EndTrail()
    {
        isActive = false;
        if (clearOnEndTrail) Clear();
    }

    public void Clear()
    {
        EnsureBuffer();
        for (int i = 0; i < trailLength; i++)
        {
            _slots[i] = -1;
            _gens[i] = 0;
            _mgrs[i] = null;
        }
        _writeIndex = 0;
        _count = 0;
    }

    /// <summary>Spout calls this when it transitions off a pool.</summary>
    public void RegisterPool(UCS_FluidPool pool)
    {
        if (pool == null || pool.fluidManager == null) return;
        EnsureBuffer();

        UCS_FluidManager mgr = pool.fluidManager;
        int slot = mgr.FindSlotForPool(pool);
        if (slot < 0) return;
        int gen = mgr.GetSlotGeneration(slot);

        // De-dupe with the most recent entry so repeated registers of the same
        // active slot don't waste buffer space.
        if (_count > 0)
        {
            int last = (_writeIndex - 1 + trailLength) % trailLength;
            if (_slots[last] == slot && _mgrs[last] == mgr && _gens[last] == gen) return;
        }

        _slots[_writeIndex] = slot;
        _gens[_writeIndex]  = gen;
        _mgrs[_writeIndex]  = mgr;
        _writeIndex = (_writeIndex + 1) % trailLength;
        if (_count < trailLength) _count++;
    }

    /// <summary>Ignite all stored pools, optionally with a per-step delay.</summary>
    public void IgniteTrackedPools()
    {
        EnsureBuffer();
        if (_count == 0) return;

        if (cascadeIntervalSeconds <= 0f)
        {
            // Walk newest → oldest, ignite all immediately.
            for (int i = 0; i < _count; i++)
            {
                int idx = (_writeIndex - 1 - i + trailLength * 2) % trailLength;
                TryIgniteAt(idx);
            }
            return;
        }

        _cascadeCursor    = (_writeIndex - 1 + trailLength) % trailLength;
        _cascadeRemaining = _count;
        _CascadeStep();
    }

    public void _CascadeStep()
    {
        if (_cascadeRemaining <= 0) return;
        TryIgniteAt(_cascadeCursor);
        _cascadeRemaining--;
        _cascadeCursor = (_cascadeCursor - 1 + trailLength) % trailLength;
        if (_cascadeRemaining > 0)
        {
            SendCustomEventDelayedSeconds(nameof(_CascadeStep), cascadeIntervalSeconds);
        }
    }

    private void TryIgniteAt(int idx)
    {
        int slot = _slots[idx];
        UCS_FluidManager mgr = _mgrs[idx];
        if (slot < 0 || mgr == null) return;
        // Generation guard: skip if the slot has been recycled.
        if (mgr.GetSlotGeneration(slot) != _gens[idx]) return;
        mgr.IgniteLocal(slot, _gens[idx]);
    }
}
