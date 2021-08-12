
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ScopeManager : UdonSharpBehaviour
{
    public Transform ScopeCameraPosition;
    public float CameraFOV;
    private GameObject CameraObject;
    private Camera ScopeCamera;

    private void Start()
    {
         CameraObject = GameObject.Find("ScopeCam");
        ScopeCamera = CameraObject.GetComponent<Camera>();
    }
    public void ManageScope()
    {
        ScopeCamera.fieldOfView = CameraFOV;
        CameraObject.transform.SetPositionAndRotation(ScopeCameraPosition.position, ScopeCameraPosition.rotation);
        CameraObject.transform.SetParent(ScopeCameraPosition);
    }
}
