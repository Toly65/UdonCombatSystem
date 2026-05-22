
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Dynamics.PhysBone.Components;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_SliderHandler : UdonSharpBehaviour
{
    [SerializeField] private VRCPhysBone physBone;
    [SerializeField] private Animator gunAnimator;

    [SerializeField] private UCS_ComplexGun complexGun;

    private VRCPlayerApi localPlayer;

    // simulation state for desktop smooth animation
    private bool simulating = false;
    private float simStartStretch = 0f;
    private float simTargetStretch = 0f;
    private float simDuration = 0.12f;
    private float simElapsed = 0f;

    float currentStretch;
    float previousStretch;
    bool passedEjectionThreshold = false;

    bool passedFullRearThreshold = false;

    bool slideforwardeventfired = false; //we use this to prevent the slide forward event from firing multiple times due to stretch value jitter when the slide is fully forward

    [SerializeField] private float ejectionThreshold = 0.35f;
    [SerializeField] private float fullRearThreshold = 0.95f;

    int ParamSlideStretch = Animator.StringToHash("SlideStretch");
    int ParamSlideLocked = Animator.StringToHash("SlideLocked");

    public void LockSlideBack()
    {
        gunAnimator.SetBool(ParamSlideLocked, true);
    }


    public void ReleaseSlide()
    {
        gunAnimator.SetBool(ParamSlideLocked, false);
    }

    // Programmatic simulation helpers for desktop smooth reload visuals
    public void SimulatePullBack()
    {
        SimulatePullBack(simDuration);
    }

    public void SimulatePullBack(float duration)
    {
        // ensure the animator isn't locked so the stretch parameter is visible
        ReleaseSlide();
        simStartStretch = currentStretch;
        simTargetStretch = fullRearThreshold;
        simDuration = Mathf.Max(0.01f, duration);
        simElapsed = 0f;
        simulating = true;

        // reset thresholds so events fire as the simulated stretch crosses them
        passedEjectionThreshold = false;
        passedFullRearThreshold = false;
        slideforwardeventfired = false;
    }

    public void SimulateRelease()
    {
        SimulateRelease(simDuration);
    }

    public void SimulateRelease(float duration)
    {
        // ensure the animator isn't locked so the stretch parameter is visible
        ReleaseSlide();
        simStartStretch = currentStretch;
        simTargetStretch = 0f;
        simDuration = Mathf.Max(0.01f, duration);
        simElapsed = 0f;
        simulating = true;

        // keep flags so forward event can fire appropriately when returning
        // do not immediately call slide forward; let simulation drive the callbacks
    }

    void Start()
    {
         // cache local player reference
        if (localPlayer == null)
        {
            localPlayer = Networking.LocalPlayer;
        }
    }

    void Update()
    {
       
        if(localPlayer == null)
        {
            return; // safety check in case local player reference is lost
        }
        // If the local player is not in VR, only run simulation updates (so desktop clients don't fight the physbone)
        if (localPlayer != null && !localPlayer.IsUserInVR())
        {
            UpdateSimulation(Time.deltaTime);
            return;
        }
        // VR path: poll the physbone and update animator as before
        currentStretch = physBone != null ? physBone.Stretch : 0f;
        gunAnimator.SetFloat(ParamSlideStretch, currentStretch);
        
        if(!passedEjectionThreshold && currentStretch >= ejectionThreshold)
        {
            //eject
            complexGun.OnEjectionThresholdCrossed();
            passedEjectionThreshold = true;
        }
        else if(passedEjectionThreshold && currentStretch < ejectionThreshold)
        {
            //insert
            passedEjectionThreshold = false;
            complexGun.OnSlideInsertionThesholdCrossed();
        }

        //fully rearward
        if(currentStretch >= fullRearThreshold && !passedFullRearThreshold)
        {
            complexGun.OnSlidePulledBack();
            passedFullRearThreshold = true;
            slideforwardeventfired = false; //reset the slide forward event fired flag when the slide is pulled back
        }
        else if(previousStretch > 0.05f && currentStretch <= 0.05f && passedFullRearThreshold && !slideforwardeventfired) //we use a slightly higher threshold for detecting the slide returning forward to avoid issues with the stretch value jittering around the threshold when the slide is fully forward
        {
            complexGun.OnSlideForward();
            passedFullRearThreshold = false;
            slideforwardeventfired = true;
        }
        previousStretch = currentStretch;

    }

    private void UpdateSimulation(float delta)
    {
        if (!simulating)
        {
            return;
        }

        simElapsed += delta;
        float t = Mathf.Clamp01(simElapsed / simDuration);
        // smooth step for nicer motion
        float s = Mathf.SmoothStep(0f, 1f, t);
        currentStretch = Mathf.Lerp(simStartStretch, simTargetStretch, s);
        if (gunAnimator != null)
        {
            gunAnimator.SetFloat(ParamSlideStretch, currentStretch);
        }

        // threshold events mirrored from physbone update
        if(!passedEjectionThreshold && currentStretch >= ejectionThreshold)
        {
            complexGun.OnEjectionThresholdCrossed();
            passedEjectionThreshold = true;
        }
        else if(passedEjectionThreshold && currentStretch < ejectionThreshold)
        {
            passedEjectionThreshold = false;
            complexGun.OnSlideInsertionThesholdCrossed();
        }

        if(currentStretch >= fullRearThreshold && !passedFullRearThreshold)
        {
            complexGun.OnSlidePulledBack();
            passedFullRearThreshold = true;
            slideforwardeventfired = false;
        }
        else if(previousStretch > 0.05f && currentStretch <= 0.05f && passedFullRearThreshold && !slideforwardeventfired)
        {
            complexGun.OnSlideForward();
            passedFullRearThreshold = false;
            slideforwardeventfired = true;
        }

        previousStretch = currentStretch;

        if (t >= 1f)
        {
            simulating = false;
        }
    }

}
