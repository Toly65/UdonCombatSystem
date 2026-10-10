
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

    [Tooltip("Desktop only: a world-space canvas child that gets resized to exactly fill the screen whenever FOV or aspect changes, so HUD elements can use normal corner anchors.")]
    [SerializeField] private RectTransform fitCanvas;
    [Tooltip("Canvas height in UI units; width follows the screen aspect")]
    [SerializeField] private float canvasReferenceHeight = 1000f;
    private float fittedFov = -1f;
    private float fittedAspect = -1f;

    void Start()
    {
        localPlayer = Networking.LocalPlayer;
        screenCamera = VRCCameraSettings.ScreenCamera;
        // VR keeps the canvas at its authored size (center-anchored announcements); only desktop gets fitted
        if (Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR()) fitCanvas = null;
    }

    public override void PostLateUpdate()
    {
        if(Utilities.IsValid(localPlayer))
        {
            if (Utilities.IsValid(screenCamera) && screenCamera.Active)
            {
                transform.position = screenCamera.Position;
                transform.rotation = screenCamera.Rotation;
                if (fitCanvas != null && !screenCamera.StereoEnabled) FitCanvasToScreen();
                return;
            }

            transform.position = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            transform.rotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
        }
    }

    // Size the canvas to the camera frustum at its distance; only does work when FOV/aspect change.
    private void FitCanvasToScreen()
    {
        float fov = screenCamera.FieldOfView; // vertical, like Camera.fieldOfView
        float aspect = screenCamera.Aspect;
        if (fov == fittedFov && aspect == fittedAspect) return;
        fittedFov = fov;
        fittedAspect = aspect;

        float worldHeight = 2f * fitCanvas.localPosition.z * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        fitCanvas.sizeDelta = new Vector2(canvasReferenceHeight * aspect, canvasReferenceHeight);
        fitCanvas.localScale = Vector3.one * (worldHeight / canvasReferenceHeight);
    }

}
