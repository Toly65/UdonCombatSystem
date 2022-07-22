using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
[RequireComponent(typeof(Rigidbody))]
public class PlayerMelee : UdonSharpBehaviour
{
    
    public UdonBehaviour target;
    public float BaseDamage;
    public float maxDamage;
    public float cooldownTime = 1f;
    public bool scaleDamageWithVelocity;
    public float averageVelocityOfAFist = 7.0f;

    
   
    public float averageStaminaUsage = 2.0f;

    private bool isReady = true;

    private float currentTime;
    private float wantedTime;
    private bool StartTimer = false;
    private Rigidbody objectRigidbody;
    private VRCPlayerApi localPlayer;
    private float speed;
    private Vector3 lastPosition = Vector3.zero;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        objectRigidbody = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
    }

    private void OnTriggerEnter(Collider collision)
    {
        Debug.Log("playerMelee punching detected");
        var RealDamage = BaseDamage;
       
        if (scaleDamageWithVelocity)
        {

            //RealDamage = BaseDamage * (speed / averageVelocityOfAFist);
            RealDamage = Mathf.Clamp(BaseDamage * (speed / averageVelocityOfAFist), 0.0f, maxDamage);
        }

        if (!isReady) return;
        var otherObject = collision.gameObject;
        
        if (otherObject.name.Contains("hitbox"))
        {
            if ((UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour)) == null) return;
            target = (UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour));
            UdonBehaviour targetParent = (UdonBehaviour)otherObject.transform.parent.GetComponent(typeof(UdonBehaviour));
            VRCPlayerApi targetAssignedPlayer = (VRCPlayerApi)targetParent.GetProgramVariable("assignedPlayer");
            if (!(targetAssignedPlayer == localPlayer))
            {
                //damaging the other player
                target.SetProgramVariable("Modifier", (float)(RealDamage * -1));
                target.SendCustomEvent("ModifyHealth");
                isReady = false;
                Cooldown();
            }

            
        }
        if(otherObject.name.Contains("testbox"))
        {   
            //making a testing hitter
            if ((UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour)) == null) return;
            target = (UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour));
            //TODO make the testingHitbox
            target.SetProgramVariable("damage", RealDamage);
            target.SetProgramVariable("velocity", speed);

        }
        
        
    }
   
    public void FixedUpdate()
    {
        speed = (transform.position - lastPosition).magnitude;
        lastPosition = transform.position;
    }

    private void Update()
    {
        //Debug.Log(Target.gameObject.name);
        if (!StartTimer) return;
        if (currentTime < wantedTime)
        {
            currentTime += Time.deltaTime;
        }
        else
        {
            isReady = true;
            StartTimer = false;
        }
    }

    private void Cooldown()
    {
        currentTime = Time.time;
        wantedTime = currentTime + cooldownTime;
        StartTimer = true;
    }
}
