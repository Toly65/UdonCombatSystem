
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

public class UCS_HeadTracker : UdonSharpBehaviour
{
    VRCPlayerApi localPlayer;
    VRCCameraSettings screenCamera;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        screenCamera = VRCCameraSettings.ScreenCamera;
    }

    public override void PostLateUpdate()
    {
        if(Utilities.IsValid(localPlayer))
        {
            if (Utilities.IsValid(screenCamera) && screenCamera.Active)
            {
                transform.position = screenCamera.Position;
                transform.rotation = screenCamera.Rotation;
                return;
            }

            transform.position = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            transform.rotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        }
    }

}
