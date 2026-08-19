
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
    [SerializeField] private UCS_PhysbonePoller physbonePoller;
    [SerializeField] private float simDuration = 0.12f;

    [SerializeField] private float ejectionThreshold = 0.35f;
    [SerializeField] private float fullRearThreshold = 0.95f;

    int ParamSlideStretch = Animator.StringToHash("SlideStretch");
    int ParamSlideLocked = Animator.StringToHash("SlideLocked");
    int ParamSlideGrabbed = Animator.StringToHash("SlideGrabbed");
    int ParamSlidePosed = Animator.StringToHash("SlidePosed");

    private bool slideLockedBack = false;

    public void LockSlideBack()
    {
        slideLockedBack = true;
        gunAnimator.SetBool(ParamSlideLocked, true);
    }


    public void ReleaseSlide()
    {
        slideLockedBack = false;
        gunAnimator.SetBool(ParamSlideLocked, false);
    }

    // Manually latch the charging handle to the locked (rear) position. Goes
    // through LockSlideBack so the shared SlideLocked bool drives the animator to
    // the locked state on both the slide layer and the charge-handle grab layer.
    public void LockChargeHandle()
    {
        LockSlideBack();
    }

    // Release a manually locked charging handle so it springs forward.
    public void ReleaseChargeHandle()
    {
        ReleaseSlide();
    }

    public void SetSlideGrabbed(bool grabbed)
    {
        if (gunAnimator != null)
        {
            gunAnimator.SetBool(ParamSlideGrabbed, grabbed);
        }
    }

    public void SetSlidePosed(bool posed)
    {
        if (gunAnimator != null)
        {
            gunAnimator.SetBool(ParamSlidePosed, posed);
        }
    }

    // Fired by the physbone when a pose is applied. If the posed slide reaches
    // (or passes) the full-rear threshold the slide is set to the locked state;
    // if it is posed short of the threshold we release the pose immediately so
    // the slide does not stick in a partial pose.
    public override void OnPhysBonePosed(VRC.Dynamics.PhysBonePosedInfo physBoneInfo)
    {
        float stretch = physBone != null ? physBone.Stretch : 0f;
        if (stretch >= fullRearThreshold)
        {
            SetSlidePosed(true);
            LockSlideBack();
            if (complexGun != null) complexGun.SetSlideLockedFromPose(true);
        }
        else
        {
            SetSlidePosed(false);
            ReleaseSlide();
            if (complexGun != null) complexGun.SetSlideLockedFromPose(false);
        }
    }

    // Fired by the physbone when a pose is cleared. A pose that reached the
    // full-rear threshold keeps the slide locked back; anything else releases
    // the pose immediately so the slide springs forward.
    public override void OnPhysBoneUnPosed(VRC.Dynamics.PhysBoneUnPosedInfo physBoneInfo)
    {
        if (!slideLockedBack)
        {
            SetSlidePosed(false);
            ReleaseSlide();
            if (complexGun != null) complexGun.SetSlideLockedFromPose(false);
        }
    }

    // Programmatic simulation helpers for desktop smooth reload visuals
    public void SimulatePullBack()
    {
        SimulatePullBack(simDuration);
    }

    public void SimulatePullBack(float duration)
    {
        ReleaseSlide();
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.BeginSimulation(fullRearThreshold, Mathf.Max(0.01f, duration), true);
        }
    }

    public void SimulateRelease()
    {
        SimulateRelease(simDuration);
    }

    public void SimulateRelease(float duration)
    {
        ReleaseSlide();
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.BeginSimulation(0f, Mathf.Max(0.01f, duration), false);
        }
    }

    void Start()
    {
        if (physbonePoller == null)
        {
            physbonePoller = GetComponentInChildren<UCS_PhysbonePoller>(true);
        }

        if (gunAnimator != null)
        {
            gunAnimator.SetBool(ParamSlideGrabbed, false);
            gunAnimator.SetBool(ParamSlidePosed, false);
        }
    }

    public override void OnPhysBoneGrabbed(VRC.Dynamics.PhysBoneGrabbedInfo physBoneInfo)
    {
        SetSlideGrabbed(true);
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.HandlePhysBoneGrabbed();
        }
    }

    public override void OnPhysBoneReleased(VRC.Dynamics.PhysBoneReleasedInfo physBoneInfo)
    {
        SetSlideGrabbed(false);

        // Safety net for the grab path: if the slide isn't locked back when the
        // grab ends, release the pose immediately so the slide springs forward.
        if (!slideLockedBack)
        {
            SetSlidePosed(false);
            ReleaseSlide();
            if (complexGun != null) complexGun.SetSlideLockedFromPose(false);
        }

        if (physbonePoller != null)
        {
            physbonePoller.HandlePhysBoneReleased();
        }
    }

    public void EnsurePollerActive()
    {
        if (physbonePoller == null)
        {
            return;
        }

        if (!physbonePoller.gameObject.activeSelf)
        {
            physbonePoller.gameObject.SetActive(true);
        }
    }

    public VRCPhysBone GetSlidePhysBone()
    {
        return physBone;
    }

    public Animator GetGunAnimator()
    {
        return gunAnimator;
    }

    public UCS_ComplexGun GetComplexGun()
    {
        return complexGun;
    }

    public void SetOwnerDesktopMode()
    {
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.SetOwnerDesktopMode();
        }
    }

    public void SetOwnerVrMode()
    {
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.SetOwnerVrMode();
        }
    }

    public void OnGunDropped()
    {
        SetSlideGrabbed(false);
        SetSlidePosed(false);
        if (physbonePoller != null)
        {
            physbonePoller.OnGunDropped();
        }
    }

    public float GetEjectionThreshold()
    {
        return ejectionThreshold;
    }

    public float GetFullRearThreshold()
    {
        return fullRearThreshold;
    }

}
