
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class TeamManager : UdonSharpBehaviour
{
    public GameObject[] teamIndicators;
    public int[] teamMaxPlayerCounts;
    public ImprovedHitBoxAssigner hitboxAssigner;
    [UdonSynced] private int[][] TeamIDs;
    public int LocalTeamID;
    public bool labelEveryone;
    public Transform[] teamSpawns;
    private bool inTeam;

    [HideInInspector]public VRCPlayerApi[][] playerApis;

    private VRCPlayerApi localplayer;
    private void Start()
    {
        localplayer = Networking.LocalPlayer;
        //initialisation of all the arrays
        TeamIDs = new int[teamIndicators.Length][];
        playerApis = new VRCPlayerApi[teamIndicators.Length][];
        for (int i = 0; i < teamIndicators.Length; i++)
        {
            TeamIDs[i] = new int[teamMaxPlayerCounts[i]];
            //this should be handled in deserialisation
            //playerApis[i] = new VRCPlayerApi[teamMaxPlayerCounts[i]];
            for (int j = 0; j < TeamIDs[i].Length; j++)
            {
                //set them to an impossible value for the ID;
                TeamIDs[i][j] = 900;
            }

        }
    }
    public void RemovePlayerFromAllTeams(VRCPlayerApi player, bool syncChange)
    {
        Networking.SetOwner(localplayer, gameObject);
        inTeam = false;
        //check if the player is in any other team
        int playerID = player.playerId;
        for (int i = 0; i < teamIndicators.Length; i++)
        {
            for (int j = 0; j < TeamIDs[i].Length; j++)
            {
                if (TeamIDs[i][j] == playerID)
                {
                    //this invalidates the ID so that the player isn't on any team
                    TeamIDs[i][j] = 900;
                }
            }
        }

    }
    public void SetPlayerToTeam(VRCPlayerApi player, int TeamID)
    {
        
        RemovePlayerFromAllTeams(player, false);
        LocalTeamID = TeamID;
        inTeam = true;
        bool added = false;
        for (int i = 0; i < TeamIDs[TeamID].Length; i++)
        {

            if(!added)
            {
                VRCPlayerApi playerslot = VRCPlayerApi.GetPlayerById(TeamIDs[TeamID][i]);
                if(!playerslot.IsValid())
                {
                    //this means that this slot is free
                    TeamIDs[TeamID][i] = localplayer.playerId;
                    added = true;
                }
            }
        }
        RequestSerialization();
    }
    public void GeneratePlayerListFromIDs()
    {
        //first check valid IDs and create the size of the arrays based on that
        for (int i = 0; i < teamIndicators.Length; i++)
        {
            int validPlayers = 0;
            for (int j = 0; j < TeamIDs[i].Length; j++)
            {
                VRCPlayerApi playerslot = VRCPlayerApi.GetPlayerById(TeamIDs[i][j]);
                if(playerslot.IsValid())
                {
                    validPlayers++;
                }
            }
            playerApis[i] = new VRCPlayerApi[validPlayers];
        }

        //now to assign the players to the fresh arrays
        for (int i = 0; i < teamIndicators.Length; i++)
        {
            int playerslot = 0;
            for (int j = 0; j < TeamIDs[i].Length; j++)
            {
                playerApis[i][playerslot] = VRCPlayerApi.GetPlayerById(TeamIDs[i][j]);
                playerslot++;
            }
        }
    }
    public void AssignTeamIDsToHitBoxes()
    {
        for (int i = 0; i < teamIndicators.Length; i++)
        {
            for (int j = 0; j < playerApis.Length; j++)
            {
                foreach(ImprovedHitBoxManager manager in hitboxAssigner.hitboxArray)
                {
                    if(manager.assignedPlayer == playerApis[i][j])
                    {
                        manager.teamID = i;
                    }
                }
            }
        }
    }
    public void OnDeserialization()
    {
        GeneratePlayerListFromIDs();
        AssignTeamIDsToHitBoxes();
    }
    public void RemoveAllIndicators(Transform parentTransform)
    {
        for (int i = 0; i < parentTransform.childCount; i++)
        {
            Destroy(parentTransform.GetChild(i).gameObject);
        }
    }
    public void createIndicator(int indicatorID,Transform parentTransform)
    {
        RemoveAllIndicators(parentTransform);
        GameObject indicator = GameObject.Instantiate(teamIndicators[indicatorID]);
        Transform indicatorTransform = indicator.transform;
        indicatorTransform.SetParent(parentTransform);
        indicatorTransform.SetPositionAndRotation(parentTransform.position, parentTransform.rotation);
        indicator.SetActive(true);
    }
    public void CreateTeamIndicators()
    {
        if(!labelEveryone)
        {
            //only need to label everyone who is on the team
            for (int i = 0; i < hitboxAssigner.hitboxArray.Length; i++)
            {
                //SET THE FUCKIN TEAM IDs
                if(hitboxAssigner.hitboxArray[i].teamID == LocalTeamID)
                {
                    //use createIndicator
                    createIndicator(LocalTeamID, hitboxAssigner.hitboxArray[i].TeamIndicatorParent);
                }
            }
        }else
        {
            for (int i = 0; i < hitboxAssigner.hitboxArray.Length; i++)
            {
                createIndicator(hitboxAssigner.hitboxArray[i].teamID, hitboxAssigner.hitboxArray[i].TeamIndicatorParent);
            }
        }
    }
    public void SendStartMatchEvent()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartMatch");
    }
    public void StartMatch()
    {
        //send all respective players to their spawns
        if(inTeam)
        {
            localplayer.TeleportTo(teamSpawns[LocalTeamID].position,teamSpawns[LocalTeamID].rotation);
        }

        //assign everyone an indicator
        CreateTeamIndicators();
    }
}
