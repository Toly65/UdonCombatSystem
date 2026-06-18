using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_FluidPool : UdonSharpBehaviour
{
    private const byte SLOT_EMPTY = 0;
    private const byte SLOT_LIVE = 1;
    private const byte SLOT_BURNING = 2;

    [Header("Pool Links")]
    public UCS_FluidManager fluidManager;
    public UCS_FireNodeManager fireNodeManager;

    [Header("Visuals")]
    public Transform fluidVisualRoot;
    public Transform fireVisualRoot;

    [Header("Fluid Config")]
    public float maxVolume = 1f;
    public float igniteVolumeThreshold = 0.05f;
    public float burnRate = 0.05f;
    public bool allowIgnition = true;

    [HideInInspector] public float volume = 0f;
    [HideInInspector] public bool isBurning;

    [UdonSynced] private Vector3 _syncPosition;
    [UdonSynced] private float _syncShadowVolume;
    [UdonSynced] private int _syncShadowTimeMs;
    [UdonSynced] private float _syncFillRate;
    [UdonSynced] private byte _syncFlags;
    [UdonSynced] private int _syncGeneration;

    private Vector3 _baseVisualScale = Vector3.one;
    private Vector3 _baseFireVisualScale = Vector3.one;
    private Vector3 _baseFireVisualPosition = Vector3.zero;
    private bool _scaleCached;
    private float _serializeTimer;
    private float _pulseTimer;
    private bool _dirty;

    private void Start()
    {
        CacheVisualScale();
        ApplySyncedState();
    }

    private void Update()
    {
        float predicted = ComputePredictedVolume();
        if (predicted != volume)
        {
            volume = predicted;
            RefreshVisuals();
        }

        if (!Networking.IsOwner(gameObject))
        {
            return;
        }

        if (IsBurning && predicted <= 0f)
        {
            Recycle();
            return;
        }

        if (fluidManager == null)
        {
            return;
        }

        _serializeTimer += Time.deltaTime;
        _pulseTimer += Time.deltaTime;

        if (_pulseTimer >= fluidManager.shadowPulseSec)
        {
            _pulseTimer = 0f;
            SnapshotShadow();
            _dirty = true;
        }

        if (_dirty && _serializeTimer >= fluidManager.networkTickRate)
        {
            _serializeTimer = 0f;
            _dirty = false;
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        ApplySyncedState();
    }

    public float ComputePredictedVolume()
    {
        if ((_syncFlags & SLOT_LIVE) == 0) return 0f;
        if (_syncShadowTimeMs == 0) return Mathf.Clamp(_syncShadowVolume, 0f, maxVolume);

        int nowMs = Networking.GetServerTimeInMilliseconds();
        float elapsedSec = (nowMs - _syncShadowTimeMs) / 1000f;
        if (elapsedSec < 0f) elapsedSec = 0f;

        float netRate = _syncFillRate - (isBurning ? burnRate : 0f);
        return Mathf.Clamp(_syncShadowVolume + netRate * elapsedSec, 0f, maxVolume);
    }

    public void PrepareForUse(Vector3 position, UCS_FluidManager manager)
    {
        if (!_scaleCached) CacheVisualScale();
        fluidManager = manager;
        _syncPosition = position;
        transform.position = position;
        ApplySyncedState();
    }

    public void BeginLive(Vector3 position)
    {
        EnsureOwnership();
        if (!_scaleCached) CacheVisualScale();

        if (isBurning)
        {
            SetBurningVisual(false);
        }

        _syncPosition = position;
        transform.position = position;
        _syncFlags = SLOT_LIVE;
        _syncShadowVolume = 0f;
        _syncShadowTimeMs = Networking.GetServerTimeInMilliseconds();
        _syncFillRate = 0f;
        _syncGeneration = _syncGeneration + 1;
        volume = 0f;
        _dirty = true;
        _serializeTimer = 0f;
        _pulseTimer = 0f;
        RefreshVisuals();
    }

    public void Recycle()
    {
        EnsureOwnership();

        if (isBurning)
        {
            SetBurningVisual(false);
        }

        _syncFlags = SLOT_EMPTY;
        _syncShadowVolume = 0f;
        _syncShadowTimeMs = Networking.GetServerTimeInMilliseconds();
        _syncFillRate = 0f;
        _syncGeneration = _syncGeneration + 1;
        volume = 0f;
        _dirty = true;
        _serializeTimer = 0f;
        _pulseTimer = 0f;
        RefreshVisuals();
    }

    public void ResetForPool()
    {
        if (isBurning)
        {
            SetBurningVisual(false);
        }

        _syncFlags = SLOT_EMPTY;
        _syncShadowVolume = 0f;
        _syncShadowTimeMs = 0;
        _syncFillRate = 0f;
        volume = 0f;
        _dirty = false;
        _serializeTimer = 0f;
        _pulseTimer = 0f;
        RefreshVisuals();
    }

    public float SnapshotShadow()
    {
        float predicted = ComputePredictedVolume();
        _syncShadowVolume = predicted;
        _syncShadowTimeMs = Networking.GetServerTimeInMilliseconds();
        volume = predicted;
        RefreshVisuals();
        return predicted;
    }

    public void SetFlowRate(float rate)
    {
        if ((_syncFlags & SLOT_LIVE) == 0) return;

        EnsureOwnership();
        SnapshotShadow();
        _syncFillRate = rate;
        _dirty = true;
        _serializeTimer = 0f;
        volume = ComputePredictedVolume();
        RefreshVisuals();
    }

    public void SetBurning(bool burning)
    {
        if ((_syncFlags & SLOT_LIVE) == 0) return;
        if (burning == isBurning) return;
        if (burning && !allowIgnition) return;
        if (burning && !CanBurn()) return;
        if (burning && volume < igniteVolumeThreshold) return;

        EnsureOwnership();
        SnapshotShadow();
        SetBurningVisual(burning);
        _dirty = true;
        _serializeTimer = 0f;
        volume = ComputePredictedVolume();
        RefreshVisuals();
    }

    public void AddVolume(float amount)
    {
        if (amount <= 0f) return;

        if ((_syncFlags & SLOT_LIVE) != 0)
        {
            EnsureOwnership();
            SnapshotShadow();
            _syncShadowVolume = Mathf.Clamp(_syncShadowVolume + amount, 0f, maxVolume);
            _dirty = true;
            _serializeTimer = 0f;
            volume = ComputePredictedVolume();
            RefreshVisuals();
            return;
        }

        _syncShadowVolume = Mathf.Clamp(_syncShadowVolume + amount, 0f, maxVolume);
        _syncShadowTimeMs = Networking.GetServerTimeInMilliseconds();
        volume = _syncShadowVolume;
        RefreshVisuals();
    }

    public void Ignite()
    {
        if (!allowIgnition || !CanBurn() || volume < igniteVolumeThreshold) return;
        if (isBurning) return;
        if (fluidManager == null) return;

        int slot = fluidManager.FindSlotForPool(this);
        if (slot < 0) return;
        int gen = fluidManager.GetSlotGeneration(slot);
        fluidManager.IgniteLocal(slot, gen);
    }

    public void Extinguish()
    {
        if (!isBurning) return;
        if (fluidManager == null) return;

        int slot = fluidManager.FindSlotForPool(this);
        if (slot < 0) return;
        int gen = fluidManager.GetSlotGeneration(slot);
        fluidManager.ExtinguishLocal(slot, gen);
    }

    public bool CanBurn()
    {
        return HasFluidProperty(UCS_FluidProperty.Flammable) || HasFluidProperty(UCS_FluidProperty.GasFlammable);
    }

    public bool IsBurning { get { return isBurning; } }

    public bool IsActivePool()
    {
        return (_syncFlags & SLOT_LIVE) != 0;
    }

    public int GetGeneration()
    {
        return _syncGeneration;
    }

    public void ApplyNetworkShadow(float shadowVolume, int shadowTimeMs, float fillRate, bool burning)
    {
        _syncShadowVolume = Mathf.Clamp(shadowVolume, 0f, maxVolume);
        _syncShadowTimeMs = shadowTimeMs;
        _syncFillRate = fillRate;
        SetBurningVisual(burning);
        volume = ComputePredictedVolume();
        RefreshVisuals();
    }

    private void ApplySyncedState()
    {
        if (!_scaleCached)
        {
            CacheVisualScale();
        }

        bool live = (_syncFlags & SLOT_LIVE) != 0;
        SetBurningVisual(live && ((_syncFlags & SLOT_BURNING) != 0));

        if (!live)
        {
            volume = 0f;
            RefreshVisuals();
            return;
        }

        volume = ComputePredictedVolume();
        RefreshVisuals();
    }

    private void EnsureOwnership()
    {
        if (!Networking.IsOwner(gameObject))
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }

    private void SetBurningVisual(bool burning)
    {
        Transform fireTarget = fireVisualRoot != null ? fireVisualRoot : transform;

        if (burning)
        {
            if (isBurning) return;
            isBurning = true;
            _syncFlags = (byte)(_syncFlags | SLOT_BURNING);
            if (fireNodeManager != null)
            {
                fireNodeManager.AttachFireToPool(fireTarget);
            }
            return;
        }

        if (!isBurning)
        {
            _syncFlags = (byte)(_syncFlags & ~SLOT_BURNING);
            return;
        }

        isBurning = false;
        _syncFlags = (byte)(_syncFlags & ~SLOT_BURNING);
        if (fireNodeManager != null)
        {
            fireNodeManager.DetachFireFromPool(fireTarget);
        }
    }

    private void CacheVisualScale()
    {
        if (fluidVisualRoot != null)
        {
            _baseVisualScale = fluidVisualRoot.localScale;
        }

        if (fireVisualRoot != null)
        {
            _baseFireVisualScale = fireVisualRoot.localScale;
            _baseFireVisualPosition = fireVisualRoot.localPosition;
        }

        _scaleCached = true;
    }

    private void RefreshVisuals()
    {
        if (fluidVisualRoot != null)
        {
            float normalizedVolume = maxVolume > 0f ? Mathf.Clamp01(volume / maxVolume) : 0f;
            Vector3 scale = _baseVisualScale;
            scale.x = Mathf.Max(0.01f, _baseVisualScale.x * normalizedVolume);
            scale.z = Mathf.Max(0.01f, _baseVisualScale.z * normalizedVolume);
            fluidVisualRoot.localScale = scale;
            fluidVisualRoot.gameObject.SetActive(volume > 0f);
        }

        if (fireVisualRoot != null)
        {
            float normalizedVolume = maxVolume > 0f ? Mathf.Clamp01(volume / maxVolume) : 0f;
            Vector3 fireScale = _baseFireVisualScale;
            fireScale.x = Mathf.Max(0.01f, _baseFireVisualScale.x * normalizedVolume);
            fireScale.y = Mathf.Max(0.01f, _baseFireVisualScale.y * normalizedVolume);
            fireScale.z = Mathf.Max(0.01f, _baseFireVisualScale.z * normalizedVolume);
            fireVisualRoot.localScale = fireScale;

            Vector3 firePosition = _baseFireVisualPosition;
            firePosition.y = _baseFireVisualPosition.y * normalizedVolume;
            fireVisualRoot.localPosition = firePosition;

            fireVisualRoot.gameObject.SetActive(isBurning);
        }
    }

    private bool HasFluidProperty(UCS_FluidProperty property)
    {
        if (fluidManager != null) return fluidManager.HasProperty(property);
        return false;
    }
}