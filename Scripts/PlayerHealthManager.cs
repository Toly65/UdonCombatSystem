
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

[AddComponentMenu("")]
public class PlayerHealthManager : UdonSharpBehaviour
{
    

    [HideInInspector]public float CurrentHealth = 100.0f;
    public float RespawnHealth = 100.0f;

    [Header("HealthMangerAddons")]



    public UdonBehaviour OnDamageBehaviour;
    public string DamageFunction;
    public string DamageInputVariable;

    public UdonBehaviour OnDeathBehaviour;
    public string DeathFunction;
    private VRCPlayerApi localPlayer;
    // private String PlayerTag = "[Player]";

    public bool RespawnTimer = true;
    [Header("respawn Related Stuff")]
    public Transform[] RespawnPoint;
    public Transform DeathPoint;
    public float RespawnTime = 5f;
    public killTracker Killtracker;
    public ImprovedHitBoxAssigner hitBoxAssigner;

    private float deathTimerStart;

    [SerializeField] private bool optState = true;
    [SerializeField] private GameObject[] optToggledObjects;
    private bool[] optToggleObjectsStates;
    [HideInInspector] public bool Dead;

    
    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        CurrentHealth = RespawnHealth;

        //localPLayer.CombatSetMaxHitpoints(CurrentHealth);
        //localPLayer.CombatSetCurrentHitpoints(CurrentHealth);
    }

    public void SetOptState(bool state)
    {
        optState = state;
        if(state)
        {

        }
        else
        {

        }
    }
    public bool GetOptState()
    {
        return optState;
    }

    public void ModifyHealth(float Damage)
    {
        Debug.Log("modify health Recieved by health Manager");
        if(optState)
        {
            if (OnDamageBehaviour)
            {
                //MAKE BEHAVIOUR STUFF HAPPEN
                OnDamageBehaviour.SendCustomEvent(DamageFunction);
            }
            else
            {
                CurrentHealth += Damage;
            }
        }else
        {
            Debug.Log("local player is not opted into combat!");
        }
    }



    private void FixedUpdate()
    {
        //health stuff
        if (CurrentHealth <= 0f && !Dead)
        {
            Die();
        }
        if(Dead)
        {
            CurrentHealth = RespawnHealth;
            if (Time.time - deathTimerStart > RespawnTime)
            {
                Dead = false;
            }
        }

    }
    public void Die()
    {
        //REDO DEATH
        Dead = true;
        deathTimerStart = Time.time;
        RespawnObject();
        if (OnDeathBehaviour)
        {
            OnDeathBehaviour.SendCustomEvent(DeathFunction);
        }
        if(Killtracker)
        {
            //this line is an issue
            //Killtracker.addkill(hitBoxAssigner.hitboxArray[localPLayer.playerId].LastPlayerWhoDamagedID);
            Killtracker.addkill(hitBoxAssigner.getHitBoxByPlayerID(localPlayer.playerId).LastPlayerWhoDamagedID);
        }
    }

    public void RespawnObject()
    {
        Debug.Log("respawn Triggerd");
        int respawnIndex = Random.Range(0, RespawnPoint.Length-1);
        localPlayer.TeleportTo(RespawnPoint[respawnIndex].position, RespawnPoint[respawnIndex].rotation);
        
        CurrentHealth = RespawnHealth;
        //localPLayer.CombatSetCurrentHitpoints(RespawnHealth);
        
    }
}