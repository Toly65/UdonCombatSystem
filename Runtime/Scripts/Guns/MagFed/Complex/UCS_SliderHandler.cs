
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
    }

    public override void OnPhysBoneGrabbed(VRC.Dynamics.PhysBoneGrabbedInfo physBoneInfo)
    {
        EnsurePollerActive();
        if (physbonePoller != null)
        {
            physbonePoller.HandlePhysBoneGrabbed();
        }
    }

    public override void OnPhysBoneReleased(VRC.Dynamics.PhysBoneReleasedInfo physBoneInfo)
    {
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
