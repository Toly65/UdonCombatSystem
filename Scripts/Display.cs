
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class Display : UdonSharpBehaviour
{
    private VRCPlayerApi localPlayer;
    public GameObject leftHandPosition;
    public GameObject rightHandPosition;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }

    void Update()
    {
        leftHandPosition.transform.SetPositionAndRotation(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position, localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).rotation);
        rightHandPosition.transform.SetPositionAndRotation(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position, localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).rotation);
    }
}
