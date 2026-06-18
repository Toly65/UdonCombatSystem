
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Dynamics.PhysBone.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_PhysbonePoller : UdonSharpBehaviour
{
    [SerializeField] private UCS_SliderHandler sliderHandler;

    private VRCPhysBone physBone;
    private Animator gunAnimator;
    private UCS_ComplexGun complexGun;

    private float ejectionThreshold = 0.35f;
    private float fullRearThreshold = 0.95f;

    private bool simulating = false;
    private float simStartStretch = 0f;
    private float simTargetStretch = 0f;
    private float simDuration = 0.12f;
    private float simElapsed = 0f;

    private float currentStretch;
    private float previousStretch;
    private bool passedEjectionThreshold = false;
    private bool passedFullRearThreshold = false;
    private bool slideForwardEventFired = false;
    private bool ownerIsDesktopMode = false;
    private bool physBoneHeld = false;

    private int ParamSlideStretch = Animator.StringToHash("SlideStretch");

    private void Start()
    {
        CacheReferences();
        currentStretch = physBone != null ? physBone.Stretch : 0f;
        previousStretch = currentStretch;
        if (gunAnimator != null)
        {
            gunAnimator.SetFloat(ParamSlideStretch, currentStretch);
        }
    }

    private void OnEnable()
    {
        CacheReferences();
    }

    private void CacheReferences()
    {
        if (sliderHandler == null)
        {
            sliderHandler = GetComponentInParent<UCS_SliderHandler>();
        }

        if (sliderHandler != null)
        {
            physBone = sliderHandler.GetSlidePhysBone();
            gunAnimator = sliderHandler.GetGunAnimator();
            complexGun = sliderHandler.GetComplexGun();
            ejectionThreshold = sliderHandler.GetEjectionThreshold();
            fullRearThreshold = sliderHandler.GetFullRearThreshold();
        }
    }

    public void BeginSimulation(float targetStretch, float duration, bool resetThresholds)
    {
        gameObject.SetActive(true);
        simStartStretch = currentStretch;
        simTargetStretch = targetStretch;
        simDuration = Mathf.Max(0.01f, duration);
        simElapsed = 0f;
        simulating = true;

        if (resetThresholds)
        {
            passedEjectionThreshold = false;
            passedFullRearThreshold = false;
            slideForwardEventFired = false;
        }
    }

    private void Update()
    {
        if (gunAnimator == null || complexGun == null)
        {
            return;
        }

        if (ownerIsDesktopMode)
        {
            UpdateSimulation(Time.deltaTime);
        }
        else
        {
            currentStretch = physBone != null ? physBone.Stretch : 0f;
            gunAnimator.SetFloat(ParamSlideStretch, currentStretch);
            ProcessThresholdEvents();
            previousStretch = currentStretch;
        }

        if (!simulating && currentStretch <= 0.001f && !physBoneHeld)
        {
            gameObject.SetActive(false);
        }
    }

    public void SetOwnerDesktopMode()
    {
        ownerIsDesktopMode = true;
        gameObject.SetActive(true);
    }

    public void SetOwnerVrMode()
    {
        ownerIsDesktopMode = false;
        gameObject.SetActive(true);
    }

    public void OnGunDropped()
    {
        simulating = false;
        ownerIsDesktopMode = false;
    }

    public void HandlePhysBoneGrabbed()
    {
        physBoneHeld = true;
        gameObject.SetActive(true);
    }

    public void HandlePhysBoneReleased()
    {
        physBoneHeld = false;
    }

    private void UpdateSimulation(float deltaTime)
    {
        if (!simulating)
        {
            return;
        }

        simElapsed += deltaTime;
        float t = Mathf.Clamp01(simElapsed / simDuration);
        float s = Mathf.SmoothStep(0f, 1f, t);
        currentStretch = Mathf.Lerp(simStartStretch, simTargetStretch, s);
        gunAnimator.SetFloat(ParamSlideStretch, currentStretch);

        ProcessThresholdEvents();
        previousStretch = currentStretch;

        if (t >= 1f)
        {
            simulating = false;
        }
    }

    private void ProcessThresholdEvents()
    {
        if (!passedEjectionThreshold && currentStretch >= ejectionThreshold)
        {
            complexGun.OnEjectionThresholdCrossed();
            passedEjectionThreshold = true;
        }
        else if (passedEjectionThreshold && currentStretch < ejectionThreshold)
        {
            passedEjectionThreshold = false;
            complexGun.OnSlideInsertionThesholdCrossed();
        }

        if (currentStretch >= fullRearThreshold && !passedFullRearThreshold)
        {
            complexGun.OnSlidePulledBack();
            passedFullRearThreshold = true;
            slideForwardEventFired = false;
        }
        else if (previousStretch > 0.05f && currentStretch <= 0.05f && passedFullRearThreshold && !slideForwardEventFired)
        {
            complexGun.OnSlideForward();
            passedFullRearThreshold = false;
            slideForwardEventFired = true;
        }
    }
}
