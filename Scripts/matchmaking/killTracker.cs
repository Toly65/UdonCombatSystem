
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class killTracker : UdonSharpBehaviour
{
    [UdonSynced] private int[] kills = new int[80];
    [UdonSynced] private int[] deaths = new int[80];
    [UdonSynced] public int[] playerIDs = new int[80];
    public ImprovedHitBoxAssigner hitboxPool;
    public killScoreboardDisplay killDisplay;

    private ImprovedHitBoxManager manager;
    private VRCPlayerApi localplayer;
    private int localplayerKills = 0;

    public UdonBehaviour onKillBehaviour;
    public string killMethod;

    [Header("usually used by a matchmaker")]
    public UdonBehaviour DeserializationBehaviour;
    public string methodName;
    private int KillTrackerID;
    private void Start()
    {
        localplayer = Networking.LocalPlayer;
        manager = hitboxPool.hitboxArray[localplayer.playerId];
        if (killDisplay)
        {
            UpdateKillDisplay();
        }
    }
    public int getKillCount(int playerID)
    {
        int killerID = GetKillerID(playerID);
        if (!(killerID < 0))
        {
            return kills[GetKillerID(playerID)];
        }
        return 0; 
    }
    public int getDeathCount(int playerID)
    {
        int killerID = GetKillerID(playerID);
        if(!(killerID < 0))
        {
            if(deaths[killerID] != null)
            {

                return deaths[killerID];
            }
            return 0;
        }
        return 0;
    }
    public void addkill(int KillingPlayer)
    {
        KillingPlayer++;
        Debug.Log("adding kill");
        int localplayerKillerID = GetKillerID(localplayer.playerId);
        int killingPlayerID = GetKillerID(KillingPlayer);
        //players who submit a kill are the same people who are dying
        if(deaths[localplayerKillerID] == null)
        {
            deaths[localplayer.playerId] = 0;
        }
        deaths[localplayerKillerID] += 1;

        if(killingPlayerID != -1)
        {
            
            Debug.Log("giving kill to player " + (KillingPlayer));
            if (kills[killingPlayerID] == null)
            {
                kills[killingPlayerID] = 0;
            }
            kills[killingPlayerID] += 1;
        }
        else
        {
            Debug.Log("invalid player ID: " + KillingPlayer);
        }
       

        Networking.SetOwner(localplayer, gameObject);
        RequestSerialization();
        if (killDisplay)
        {
            UpdateKillDisplay();
        }
    }
    public int GetKillerID(int playerID)
    {
        Debug.Log("ID given to check " + playerID);
        //log the contents of playerIDs
        string log = "playerIDs: ";
        for (int i = 0; i < playerIDs.Length; i++)
        {
            log += playerIDs[i] + ", ";
        }
        Debug.Log(log);
        for (int i = 0; i < playerIDs.Length; i++)
        {
            if (playerIDs[i]== playerID)
            {
                return i;
            }
        }
        //log the contents of playerIDs
        Debug.Log("playerIDs: " + playerIDs);
        return -1;
    }
    public void OnPlayerLeft(VRCPlayerApi player)
    {
        //find the players killerID and clear that index
        int killerID = GetKillerID(player.playerId);
        if (killerID > 0)
        {
            kills[killerID] = 0;
            deaths[killerID] = 0;
            playerIDs[killerID] = 0;
        }
        if (localplayer == Networking.GetOwner(gameObject))
        {
            RequestSerialization();
            if (killDisplay)
            {
                UpdateKillDisplay();
            }
        }
        
    }
    public void OnPlayerJoined(VRCPlayerApi player)
    {
        
        //check for empty space in array and assign it to the player
        for (int i = 0; i < playerIDs.Length; i++)
        {
            if (playerIDs[i] == 0)
            {
                //space is empty, fill it
                playerIDs[i] = player.playerId;
                Debug.Log("playerIDs " + i + "filled with" + (player.playerId));
                kills[i] = 0;
                deaths[i] = 0;
                //send changes to network
                break;
            }
        }
        if (localplayer == Networking.GetOwner(gameObject))
        {
            RequestSerialization();
        }
        if (killDisplay)
        {
            UpdateKillDisplay();
        }
    }

    private void UpdateKillDisplay()
    {
        //generate the list
        int playerCount = VRCPlayerApi.GetPlayerCount();
        string[] playernamesToSubmit = new string[playerCount];
        int[] playerkillsToSubmit = new int[playerCount];
        int[] playerDeathsToSubmit = new int[playerCount];
        VRCPlayerApi[] players = new VRCPlayerApi[playerCount+1];
        VRCPlayerApi.GetPlayers(players);
        for (int i = 0; i < playerCount; i++)
        {
            int playerID = players[i].playerId;
            int PlayerKillID = GetKillerID(playerID);
            Debug.Log("processingPlayerForDisplay");
            if(PlayerKillID != -1)
            {
                Debug.Log("player killerID " + PlayerKillID);
                Debug.Log("player name " + players[i].displayName);
                Debug.Log("player kills" + kills[PlayerKillID]);
                Debug.Log("player Deaths" + deaths[PlayerKillID]);
                playernamesToSubmit[i] = players[i].displayName;
                playerkillsToSubmit[i] = kills[PlayerKillID];
                playerDeathsToSubmit[i] = deaths[PlayerKillID];
            }
            
        }

        killDisplay.GenerateList(playernamesToSubmit, playerkillsToSubmit, playerDeathsToSubmit);
    }
    public void KillSync()
    {
        if(DeserializationBehaviour)
        {
            DeserializationBehaviour.SendCustomEvent(methodName);
        }
    }
    public void OnDeserialization()
    {
        if(killDisplay)
        {
            UpdateKillDisplay();
        }

        if(onKillBehaviour)
        {
            //check if localplayer got a kill
            int killerID = GetKillerID(localplayer.playerId);
            if(killerID > -1)
            {
                int currentKills = kills[GetKillerID(localplayer.playerId)];
                if (currentKills > localplayerKills)
                {
                    onKillBehaviour.SendCustomEvent(killMethod);
                    localplayerKills = currentKills;
                }
            }
            
        }
    }

}
