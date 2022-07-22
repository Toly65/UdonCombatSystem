
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UdonTurretAim : UdonSharpBehaviour
{
    
    public Transform turretBase = null;
    public Transform barrels = null;
    public float ElevationSpeed = 30f;
    public float MaxElevation = 60f;
    public float MaxDepression = 5f;
    public float TraverseSpeed = 60f;
    public Transform AimPos;
    public Vector3 AimPosition;
    private float aimedThreshold = 5f;
    private float AngleToTarget;

    private float angleToTarget = 0f;
    private float elevation = 0f;

    public bool IsIdle = false;
    public bool isAimed = false;
    private bool isBaseAtRest = false;
    private bool isBarrelAtRest = false;
    public bool IsTurretAtRest;

    private void Update()
    {
        if(AimPos != null)
        {
            AimPosition = AimPos.position;
        }
        
        if (IsIdle)
        {
            if (!IsTurretAtRest)
                RotateTurretToIdle();
            isAimed = false;
        }
        else
        {
            RotateBaseToFaceTarget(AimPosition);


            RotateBarrelsToFaceTarget(AimPosition);

            // Turret is considered "aimed" when it's pointed at the target.
            angleToTarget = GetTurretAngleToTarget(AimPosition);

            // Turret is considered "aimed" when it's pointed at the target.
            isAimed = angleToTarget < aimedThreshold;

            isBarrelAtRest = false;
            isBaseAtRest = false;
        }
    }

    private float GetTurretAngleToTarget(Vector3 targetPosition)
    {
        float angle = 999f;


        Vector3 flattenedTarget = Vector3.ProjectOnPlane(
            targetPosition - turretBase.position,
            turretBase.up);

        angle = Vector3.Angle(
            flattenedTarget - turretBase.position,
            turretBase.forward);


        return angle;
    }

    private void RotateTurretToIdle()
    {
        // Rotate the base to its default position.


        turretBase.rotation = Quaternion.RotateTowards(
            turretBase.rotation,
            transform.rotation,
            TraverseSpeed * Time.deltaTime);

        isBaseAtRest = Mathf.Abs(turretBase.localEulerAngles.y) < Mathf.Epsilon;


        elevation = Mathf.MoveTowards(elevation, 0f, ElevationSpeed * Time.deltaTime);
        if (Mathf.Abs(elevation) > Mathf.Epsilon)
            barrels.localEulerAngles = Vector3.right * -elevation;
        else
            isBarrelAtRest = true;

    }


    private void RotateBarrelsToFaceTarget(Vector3 targetPosition)
    {
        Vector3 localTargetPos = turretBase.InverseTransformDirection(targetPosition - barrels.position);
        Vector3 flattenedVecForBarrels = Vector3.ProjectOnPlane(localTargetPos, Vector3.up);

        float targetElevation = Vector3.Angle(flattenedVecForBarrels, localTargetPos);
        targetElevation *= Mathf.Sign(localTargetPos.y);

        targetElevation = Mathf.Clamp(targetElevation, -MaxDepression, MaxElevation);
        elevation = Mathf.MoveTowards(elevation, targetElevation, ElevationSpeed * Time.deltaTime);

        if (Mathf.Abs(elevation) > Mathf.Epsilon)
            barrels.localEulerAngles = Vector3.right * -elevation;

    }

    private void RotateBaseToFaceTarget(Vector3 targetPosition)
    {
        Vector3 turretUp = transform.up;

        Vector3 vecToTarget = targetPosition - turretBase.position;
        Vector3 flattenedVecForBase = Vector3.ProjectOnPlane(vecToTarget, turretUp);

        
        turretBase.rotation = Quaternion.RotateTowards(
            Quaternion.LookRotation(turretBase.forward, turretUp),
            Quaternion.LookRotation(flattenedVecForBase, turretUp),
            TraverseSpeed * Time.deltaTime);
        

    }

}
