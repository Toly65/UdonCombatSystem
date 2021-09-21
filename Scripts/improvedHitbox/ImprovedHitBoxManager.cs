
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ImprovedHitBoxManager : UdonSharpBehaviour
{

    [Header("FOR THE LOVE OF GOD MAKE ME MANUAL SYNC")]
    [HideInInspector] public VRCPlayerApi assignedPlayer;

    [UdonSynced] public float damageApplied;


    // udon doesn't support syncing a VRCPlayerApi so to know who the player taking damage is we'll set them to own an empty gameobject
    public GameObject playerSync;
    public PlayerHealthManager healthManager;
    VRCPlayerApi localplayer;
    public improvedHitbox[] managedHitboxes;
    public Transform HeadTransform;
    [UdonSynced]public int AltPlayerSync;
    private bool damageBeingInflicted;

    //this is conflicting code you can delete savely but I can't
    //public bool useBoxingSystem;
    //public boxingHealth boxHealth;

    public void SetAssignedPlayer(VRCPlayerApi player)
    {

        assignedPlayer = player;
        for (int i = 0; i < managedHitboxes.Length; i++)
        {
            managedHitboxes[i].assignedPlayer = player;
        }
    }

    //damage application stuff
    private void Start()
    {
        localplayer = Networking.LocalPlayer;
    }

    public void attemptDamageApplication(float damage)
    {
        Debug.Log("Damage Applicaiton Attempted");
        damageApplied = damage;
        //one method of syncing players
        // Networking.SetOwner(assignedPlayer, playerSync);
        //another method of syncing players
        AltPlayerSync = assignedPlayer.playerId;
        Debug.Log("player attempted to be damaged ID " + AltPlayerSync);
        Networking.SetOwner(localplayer,gameObject);
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "NetworkedDamage");
        RequestSerialization();
    }
    public void NetworkedDamage()
    {
        Debug.Log("damage Infliction Set");
        damageBeingInflicted = true;
    }

    public void OnDeserialization()
    {
        Debug.Log("hitbox Deserialising");
        if(damageBeingInflicted)
        {
            Debug.Log("Current Player ID being damaged " + AltPlayerSync);
            Debug.Log("playerName: " + VRCPlayerApi.GetPlayerById(AltPlayerSync));
            VRCPlayerApi targetedPlayer = VRCPlayerApi.GetPlayerById(AltPlayerSync);
            if (!targetedPlayer.IsValid())
            {
                Debug.Log("invalid playerID transmitted");
                return;
            }

            if (localplayer == targetedPlayer)
            {
                Debug.Log("networkedDamageApplied to" + localplayer.displayName);

                //this is conflicting code you can delete safely but I can't
                /*
                if(useBoxingSystem)
                {

                    if(boxHealth.guarding)
                    {
                        boxHealth.guard += damageApplied;
                    }
                    else
                    {
                        boxHealth.health += damageApplied;
                    }


                    boxHealth.currentStaminaSpooledRegenTime = Time.time;
                    boxHealth.currentStaminaSpooledRegenTime = 0.0f;
                }
                else
                {
                    healthManager.CurrentHealth += damageApplied;
                }*/

                healthManager.CurrentHealth += damageApplied;
            }
        }
        damageBeingInflicted = false;
    }
    

}

