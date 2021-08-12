using System;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

//USE ONLY FOR PLAYERS! OBJECTS AND NPCS USE A DIFFERENT SCRIPT

[AddComponentMenu("")]
public class PlayerHealthManager : UdonSharpBehaviour
{
    public UdonBehaviour BuffManager;
    public float CurrentHealth = 100.0f;
    public float CurrentHunger = 100.0f;
    public float RespawnHealth;
    public float Modifier = 0f;

    private VRCPlayerApi localPLayer;
    // private String PlayerTag = "[Player]";

    public bool RespawnTimer = true;
    [Header("respawn Related Stuff")]
    public Transform RespawnPoint;
    public Transform DeathPoint;
    public float RespawnTime = 5f;

    private bool isTimerComplete = false;
    private bool StartTimer = false;
    private float currentTime;
    private float wantedTime;

    [Header("team and damage releated stuff")]
    [HideInInspector] public bool blue;
    [HideInInspector] public bool red;
    public bool unassignTeamOnDeath;


    //time management for the hunger
    [Header("hunger related variable")]
    private float lastDepletion;
    public bool hunger = true;
    public float hungerInterval;
    public float hungerConsumption;
    public bool allowSatiation;
    private bool satiated;
    public float satiationTime;
    public AudioSource hungerNoise;
    [HideInInspector] public float addedHunger;
    [HideInInspector] public bool Dead = false;
    private float maxHunger;
    private void Start()
    {
        RespawnHealth = CurrentHealth;
        localPLayer = Networking.LocalPlayer;
        maxHunger = CurrentHunger;
        if(allowSatiation == false)
        {
            satiated = false;
        }
    }

    public void ModifyHealth()
    {
        CurrentHealth += Modifier;
    }



    private void Update()
    {
       // Debug.Log("Player Health = " + CurrentHealth);
        //hunger stuff
        if (hunger == true)
        {
           // Debug.Log("hunger true");
            if (Time.time - lastDepletion > hungerInterval)
            {
                //Debug.Log("depleting Food");
                CurrentHunger -= hungerConsumption;
                if(CurrentHunger <= 0.25*maxHunger&&hungerNoise != null)
                {
                    hungerNoise.Play();
                }
                if(CurrentHunger < 0)
                {
                    CurrentHunger = 0;
                    CurrentHealth -= 30;
                }
                lastDepletion = Time.time;
            }

        }
        //health stuff
        if (CurrentHealth <= 0f)
        {
            
            
            Die();
        }

        if (!StartTimer) return;
        if (currentTime < wantedTime)
        {
            currentTime += Time.deltaTime;
        }
        else
        {
            isTimerComplete = true;
            StartTimer = false;
        }
        

    }



    public void Die()
    {
        Dead = true;

        
        if(unassignTeamOnDeath)
        {
            red = false;
            blue = false;
        }
        if (RespawnTimer)
        {
            Timer();
            if (!isTimerComplete)
            {
                Dead = true;
                if (DeathPoint != null)
                {
                    localPLayer.TeleportTo(DeathPoint.position, DeathPoint.rotation);
                }

                Debug.Log("You have died.");
            }
            else
            {
                isTimerComplete = false;
                RespawnObject();
                BuffManager.SendCustomEvent("ResetBleeding");
                BuffManager.SendCustomEvent("ResetPoison");
            }
        }
        else
        {
            RespawnObject();
            BuffManager.SendCustomEvent("ResetBleeding");
            BuffManager.SendCustomEvent("ResetPoison");
        }
    }

    public void RespawnObject()
    {
        Debug.Log("respawn Triggerd");
        localPLayer.TeleportTo(RespawnPoint.position, RespawnPoint.rotation);
        CurrentHunger = 100.0f;
        CurrentHealth = RespawnHealth;
        Dead = false;
    }

    public void Timer()
    {
        if(!StartTimer)
        {
            currentTime = Time.time;
            wantedTime = currentTime + RespawnTime;
            StartTimer = true;
        }
        
    }
}