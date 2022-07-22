
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.Animations;

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
        //CameraObject.transform.SetPositionAndRotation(ScopeCameraPosition.position, ScopeCameraPosition.rotation);
        //CameraObject.transform.SetParent(ScopeCameraPosition);
        ConstraintSource constraintSource = new ConstraintSource();
        constraintSource.sourceTransform = ScopeCameraPosition;
        constraintSource.weight = 1;
        ParentConstraint constraint = CameraObject.GetComponent<ParentConstraint>();
        // constraint.AddSource
        constraint.SetSource(0, constraintSource);
        CameraObject.transform.SetPositionAndRotation(ScopeCameraPosition.position, ScopeCameraPosition.rotation);
        constraint.locked = true;
        constraint.constraintActive = true;
    }
}
