
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]

[AddComponentMenu("")]
public class PlayerHealthManager : UdonSharpBehaviour
{


    [HideInInspector] public float CurrentHealth = 100.0f;
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
    [SerializeField] private bool dropItemsOnDeath;
    [SerializeField] private bool returnItemsToPoolOnDeath;
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

        //record the starting states of the objects
        optToggleObjectsStates = new bool[optToggledObjects.Length];
        for (int i = 0; i < optToggledObjects.Length; i++)
        {
            optToggleObjectsStates[i] = optToggledObjects[i].activeSelf;
        }
    }

    public void SetOptState(bool state)
    {
        optState = state;
        if(state)
        {
            //toggle the objects
            for (int i = 0; i < optToggledObjects.Length; i++)
            {
                optToggledObjects[i].SetActive(optToggleObjectsStates[i]);
            }
        }
        else
        {
            //toggle the objects
            for (int i = 0; i < optToggledObjects.Length; i++)
            {
                optToggledObjects[i].SetActive(!optToggleObjectsStates[i]);
            }
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
                OnDamageBehaviour.SetProgramVariable(DamageInputVariable, Damage);
            }
            else
            {
                CurrentHealth += Damage;
                //cap the health at max health
                Mathf.Clamp(CurrentHealth, 0.0f, RespawnHealth);
                if (CurrentHealth > RespawnHealth)
                {
                    CurrentHealth = RespawnHealth;
                }
            }
        }else
        {
            Debug.Log("local player is not opted into combat!");
        }
    }



    private void Update()
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
            Debug.Log("killed by playerid " + hitBoxAssigner.getHitBoxByPlayerID(localPlayer.playerId).LastPlayerWhoDamagedID);
            Killtracker.addkill(hitBoxAssigner.getHitBoxByPlayerID(localPlayer.playerId).LastPlayerWhoDamagedID);
        }
        if(dropItemsOnDeath)
        {
            //get the items in the localplayers hands
            VRC_Pickup leftHandItem = localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Left);
            VRC_Pickup rightHandItem = localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Right);
            //drop the items
            if (leftHandItem)
            {
                leftHandItem.Drop();
            }
            if (rightHandItem)
            {
                rightHandItem.Drop();
            }
            if (returnItemsToPoolOnDeath)
            {
                if (leftHandItem)
                {
                    ReturnItemToPool(leftHandItem.gameObject);
                }
                if (rightHandItem)
                {
                    ReturnItemToPool(rightHandItem.gameObject);
                }
            }
        }
    }
    private void ReturnItemToPool(GameObject item)
    {
        GameObject parentGameobject = item.transform.parent.gameObject;
        if (parentGameobject.GetComponent<VRCObjectPool>())
        {
            Networking.SetOwner(localPlayer, parentGameobject);
            parentGameobject.GetComponent<VRCObjectPool>().Return(item);
            return;
        }
        if(parentGameobject.GetComponent<BetterObjectPool>())
        {
            Networking.SetOwner(localPlayer, parentGameobject);
            parentGameobject.GetComponent<BetterObjectPool>().ReturnObject(item);
            return;
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