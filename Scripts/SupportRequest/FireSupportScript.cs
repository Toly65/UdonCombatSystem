
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class FireSupportScript : UdonSharpBehaviour
{
    public UdonTurretAim turret;
    public Gun gun;
    public Transform Pivot;
    public Transform TestAimLocation;

    public bool activeAim;
    public bool activelySupporting;

    [UdonSynced]private bool fufillingRequest;
    private int shotsLeft;
    private float timeSinceLastShot;
    [Header("gun")]
    public int fireAmount;
    

    [UdonSynced]public Vector3 targetLocation;
    //this is used for one time distance support

    private void Start()
    {
        if(TestAimLocation != null)
        {
            targetLocation = TestAimLocation.position;
        }
    }
    public void RequestSupport(Vector3 RequestLocation)
    {
        targetLocation = RequestLocation;
        fufillingRequest = true;
    }
    public float CalculateVerticalAngle()
    {
        Vector3 OriginLocation = transform.position;
        float gravity = -Physics.gravity.magnitude;
        float distance = (OriginLocation - targetLocation).magnitude;
        float targetHeightDifference = targetLocation.y - transform.position.y;

        float angle = Mathf.Atan(
            (Mathf.Pow(gun.fireVelocity, 2) - 
            Mathf.Sqrt(
                Mathf.Pow(gun.fireVelocity, 4) - gravity * (gravity * Mathf.Pow(distance, 2) + 2 * targetHeightDifference * Mathf.Pow(gun.fireVelocity,2)
                ))/(gravity*distance)
            ));
        return angle*Mathf.Rad2Deg;
    }
    public void RotateBase()
    {
        var lookPos = targetLocation;
        lookPos.y = 0;
        Pivot.rotation = Quaternion.LookRotation(lookPos, transform.up);
    }
    public void requestSupportNoLocation()
    {
        fufillingRequest = true;
        shotsLeft = fireAmount;
        //targetLocation = TestAimLocation.position;
    }
    public void requestSupport(Vector3 designatedlocation)
    {
        fufillingRequest = true;
        shotsLeft = fireAmount;
        targetLocation = designatedlocation;
    }

    private void Update()
    {
        if(fufillingRequest && !gun.RaycastDamage)
        {
            Debug.Log("fulfilling Request");
            turret.IsIdle = false;
            RotateBase();
            Debug.Log("angle required " + CalculateVerticalAngle());
            float angle = CalculateVerticalAngle();
            Pivot.rotation = Quaternion.Euler(-angle, Pivot.eulerAngles.y, Pivot.eulerAngles.z);
           
            if (shotsLeft > 0 && Time.time - timeSinceLastShot > gun.CycleTime && turret.isAimed)
            {
                Debug.Log("firing");
                timeSinceLastShot = Time.time;
                gun.Fire();
                shotsLeft--;
            }
            if(shotsLeft ==0)
            {
                fufillingRequest = false;
                turret.IsIdle = true;
            }
        }
        else if(fufillingRequest && gun.RaycastDamage)
        {
            turret.AimPosition = targetLocation;
            if (shotsLeft > 0 && Time.time - timeSinceLastShot > gun.CycleTime&&turret.isAimed)
            {
                timeSinceLastShot = Time.time;
                gun.Fire();
                shotsLeft--;
            }
        }
        if (activelySupporting)
        {
            turret.AimPosition = targetLocation;
        }
    }
}
