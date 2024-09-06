﻿
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class ImprovedHitBoxManager : UdonSharpBehaviour
{

    
    [HideInInspector] public VRCPlayerApi assignedPlayer;

    [UdonSynced] public float damageApplied;
    

    // udon doesn't support syncing a VRCPlayerApi so to know who the player taking damage is we'll set them to own an empty gameobject
    
    public PlayerHealthManager healthManager;
    VRCPlayerApi localplayer;
    public improvedHitbox[] managedHitboxes;
    public Transform HeadTransform;
    [HideInInspector]public int playerID;
    [UdonSynced]public int LastPlayerWhoDamagedID;
    public Transform TeamIndicatorParent;
    [HideInInspector]public int teamID;

    public void SetAssignedPlayer(VRCPlayerApi player)
    {

        assignedPlayer = player;
        playerID = player.playerId;
        for (int i = 0; i < managedHitboxes.Length; i++)
        {
            managedHitboxes[i].assignedPlayer = player;
        }
    }

    //damage application stuffD
    private void Start()
    {
        localplayer = Networking.LocalPlayer;
        damageApplied = 0;
    }

    public void attemptDamageApplication(float damage)
    {
        Debug.Log("Damage Applicaiton Attempted");
        Networking.SetOwner(localplayer,gameObject);
        damageApplied = damage;
        LastPlayerWhoDamagedID = localplayer.playerId;
        RequestSerialization();
    }
    public void OnPlayerLeft(VRCPlayerApi player)
    {
        damageApplied = 0;
        RequestSerialization();
    }

    public void OnDeserialization()
    {
        VRCPlayerApi targetedPlayer = VRCPlayerApi.GetPlayerById(playerID);
        if (targetedPlayer != null)
        {
            if(!targetedPlayer.IsValid())
            {
                Debug.Log("invalid playerID transmitted");
                return;
            } 
        }

        if (localplayer == targetedPlayer)
        {
            Debug.Log("networkedDamageApplied to" + localplayer.displayName);
            healthManager.ModifyHealth(damageApplied);
        }
    }
}

