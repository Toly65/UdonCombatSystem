using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_FluidSpout : UdonSharpBehaviour
{
    [Header("Fluid Config")]
    public UCS_FluidManager fluidManager;
    public bool autoIgniteFlammableFluid;
    public float muzzleVelocity = 15f;
    public float gravityScale = 1f;

    [Tooltip("Volume per SECOND while flowing into a pool.")]
    public float flowRate = 2.0f;

    [Tooltip("Contact-ignite probe radius for AttemptIgniteFlammableOutput.")]
    public float pressureRadius = 0.3f;

    [Header("Arc")]
    public int arcSamples = 12;
    public float arcStepSize = 0.05f;

    [Header("Flow Decisions")]
    [Tooltip("Hz at which the spout re-evaluates which slot to flow into.")]
    public float slotDecisionRate = 0.2f; // 5 Hz
    [Tooltip("Extra distance past depositMergeRadius before we abandon a current slot. Adds hysteresis.")]
    public float slotStickyRadius = 0.15f;

    [Header("Trail")]
    public UCS_FluidTrail fluidTrail;

    [Header("Visuals")]
    public LineRenderer arcLineRenderer;
    public Transform muzzleTransform;

    // ---------------- Runtime ----------------
    private bool _isFiring;
    private Vector3 _lastImpactPoint;
    private bool _hasImpact;

    private int _activeSlot = -1;
    private int _activeGen  = -1;
    private float _decisionTimer;

    public void StartFiring()
    {
        _isFiring = true;
        _decisionTimer = 0f;
        if (fluidTrail != null) fluidTrail.BeginTrail();
    }

    public void StopFiring()
    {
        _isFiring = false;
        StopActiveFlow();
        SetLineRendererVisible(false);
        if (fluidTrail != null) fluidTrail.EndTrail();
    }

    private void OnDisable()
    {
        // Defensive: never leave a slot stuck filling because the spout went away.
        if (_activeSlot >= 0) StopActiveFlow();
    }

    void Update()
    {
        if (!_isFiring)
        {
            return;
        }

        _hasImpact = SampleArc(out _lastImpactPoint);
        UpdateLineRenderer(_hasImpact, _lastImpactPoint);

        _decisionTimer += Time.deltaTime;
        if (_decisionTimer >= slotDecisionRate)
        {
            _decisionTimer = 0f;
            EvaluateFlowTarget();
        }

        if (autoIgniteFlammableFluid && fluidManager != null && _activeSlot >= 0
            && fluidManager.CanIgnite())
        {
            UCS_FluidPool p = fluidManager.GetLocalPoolBySlot(_activeSlot);
            if (p != null && !p.IsBurning && p.volume >= p.igniteVolumeThreshold)
            {
                fluidManager.IgniteLocal(_activeSlot, _activeGen);
            }
        }
    }

    private void EvaluateFlowTarget()
    {
        if (fluidManager == null) return;

        if (!_hasImpact)
        {
            StopActiveFlow();
            return;
        }

        if (_activeSlot >= 0)
        {
            // Validate the slot is still ours (not recycled).
            if (fluidManager.GetSlotGeneration(_activeSlot) != _activeGen
                || !fluidManager.IsSlotLive(_activeSlot))
            {
                _activeSlot = -1;
                _activeGen  = -1;
            }
            else
            {
                UCS_FluidPool active = fluidManager.GetLocalPoolBySlot(_activeSlot);
                float stickyR = fluidManager.depositMergeRadius + slotStickyRadius;
                if (active != null && (active.transform.position - _lastImpactPoint).sqrMagnitude <= stickyR * stickyR)
                {
                    return; // still flowing into the same slot, no event
                }
            }
        }

        // Transition: stop old flow, push old slot to trail, start new.
        if (_activeSlot >= 0)
        {
            if (fluidTrail != null)
            {
                UCS_FluidPool oldPool = fluidManager.GetLocalPoolBySlot(_activeSlot);
                if (oldPool != null) fluidTrail.RegisterPool(oldPool);
            }
            fluidManager.StopFlowLocal(_activeSlot, _activeGen);
            _activeSlot = -1;
            _activeGen  = -1;
        }

        int newSlot = fluidManager.StartFlowLocal(_lastImpactPoint, flowRate);
        if (newSlot >= 0)
        {
            _activeSlot = newSlot;
            _activeGen  = fluidManager.GetSlotGeneration(newSlot);
        }
    }

    private void StopActiveFlow()
    {
        if (_activeSlot < 0 || fluidManager == null) { _activeSlot = -1; _activeGen = -1; return; }
        if (fluidTrail != null)
        {
            UCS_FluidPool p = fluidManager.GetLocalPoolBySlot(_activeSlot);
            if (p != null) fluidTrail.RegisterPool(p);
        }
        fluidManager.StopFlowLocal(_activeSlot, _activeGen);
        _activeSlot = -1;
        _activeGen  = -1;
    }
    
    private bool SampleArc(out Vector3 impactPoint)
    {
        Vector3 origin = muzzleTransform.position;
        Vector3 velocity = muzzleTransform.forward * muzzleVelocity;
        impactPoint = Vector3.zero;

        for (int i = 0; i < arcSamples - 1; i++)
        {
            float t0 = i * arcStepSize;
            float t1 = (i + 1) * arcStepSize;
            Vector3 from = ArcPoint(origin, velocity, t0);
            Vector3 to   = ArcPoint(origin, velocity, t1);

            if (Physics.Linecast(from, to, out RaycastHit hit))
            {
                impactPoint = hit.point;
                return true;
            }
        }

        impactPoint = ArcPoint(origin, velocity, (arcSamples - 1) * arcStepSize);
        return false;
    }

    private Vector3 ArcPoint(Vector3 origin, Vector3 velocity, float t)
    {
        return origin + velocity * t + 0.5f * (Physics.gravity * gravityScale) * t * t;
    }

    private void UpdateLineRenderer(bool hasImpact, Vector3 impactPoint)
    {
        if (arcLineRenderer == null) return;

        Vector3 origin = muzzleTransform.position;
        Vector3 velocity = muzzleTransform.forward * muzzleVelocity;

        if (hasImpact)
        {
            int clipIdx = arcSamples;
            float impactDistSqr = (impactPoint - origin).sqrMagnitude;
            for (int i = 0; i < arcSamples; i++)
            {
                Vector3 p = ArcPoint(origin, velocity, i * arcStepSize);
                if ((p - origin).sqrMagnitude >= impactDistSqr)
                {
                    clipIdx = i;
                    break;
                }
            }
            int count = Mathf.Max(2, clipIdx + 1);
            arcLineRenderer.positionCount = count;
            for (int i = 0; i < count - 1; i++)
            {
                arcLineRenderer.SetPosition(i, ArcPoint(origin, velocity, i * arcStepSize));
            }
            arcLineRenderer.SetPosition(count - 1, impactPoint);
        }
        else
        {
            arcLineRenderer.positionCount = arcSamples;
            for (int i = 0; i < arcSamples; i++)
            {
                arcLineRenderer.SetPosition(i, ArcPoint(origin, velocity, i * arcStepSize));
            }
        }

        SetLineRendererVisible(true);
    }

    private void SetLineRendererVisible(bool visible)
    {
        if (arcLineRenderer != null) arcLineRenderer.enabled = visible;
    }

    public void AttemptIgniteFlammableOutput()
    {
        if (fluidManager == null) return;
        Vector3 probePoint = muzzleTransform != null ? muzzleTransform.position : transform.position;
        int slot = fluidManager.FindNearbySlot(probePoint, pressureRadius);
        if (slot < 0) return;

        UCS_FluidPool pool = fluidManager.GetLocalPoolBySlot(slot);
        if (pool == null || !pool.CanBurn()) return;

        int gen = fluidManager.GetSlotGeneration(slot);
        fluidManager.IgniteLocal(slot, gen);
    }
}
