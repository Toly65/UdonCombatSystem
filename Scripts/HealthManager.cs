using System;
using UnityEngine;
using UdonSharp;
using VRC.SDKBase;
using VRC.Udon;
using System.Collections;

// USE ONLY FOR NPC'S AND WORLD OBJECTS! PLAYERS USE DIFFERENT SCRIPT!

[AddComponentMenu("")]
public class HealthManager : UdonSharpBehaviour
{
    public UdonBehaviour BuffManager;
    public float CurrentHealth = 100.0f;
    [HideInInspector]
    public float RespawnHealth;
    public GameObject Healthbar;
    public Transform self;
    public float Modifier = 0f;
    //private String ObjectTag = "[Object]";

    public bool RespawnTimer = true;

    public Transform RespawnPoint;
    public Transform DeathPoint;
    public float RespawnTime = 5f;

    private bool isTimerComplete = false;
    private bool StartTimer = false;
    private float currentTime;
    private float wantedTime;

    private void Start()
    {
        RespawnHealth = CurrentHealth;
    }

    public void ModifyHealth()
    {
        CurrentHealth += Modifier;
    }

    private void Update()
    {
        Debug.Log("Health = " + CurrentHealth);

        Healthbar.SetActive(CurrentHealth < RespawnHealth);

        if (CurrentHealth <= 0f)
        {
            CurrentHealth = RespawnHealth;
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
            Die();
        }
    }

    public void Die()
    {
        if (RespawnTimer)
        {
            if (!isTimerComplete)
            {
                self.SetPositionAndRotation(DeathPoint.position, DeathPoint.rotation);
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

    private void RespawnObject()
    {
        self.SetPositionAndRotation(RespawnPoint.position, RespawnPoint.rotation);
    }

    public void Timer()
    {
        currentTime = Time.time;
        wantedTime = currentTime + RespawnTime;
        StartTimer = true;
    }
}
