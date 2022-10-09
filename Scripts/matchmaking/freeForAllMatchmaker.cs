
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public enum endCon {killLimit, Timer, killLimitAndTimer};
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class freeForAllMatchmaker : UdonSharpBehaviour
{

    [Header("config")]
    public endCon endCondition;
    [SerializeField] private int maxKillForMatchEnd;
    [SerializeField] private float maxTimeInSeconds;
    [Header("requirements")]
    public PlayerHealthManager healthManager;
    public Button startButton;
    public Transform[] spawnLocations;
    public Transform LobbySpawn;
    public killTracker Killtracker;
    public killScoreboardDisplay killScoreboardDisplay;
    public BetterObjectPool[] objectPools;
    [Header("win Display Stuff")]
    public GameObject WinDisplay;
    public GameObject nameDisplayTemplate;
    public Transform nameDisplayParent;
    public Text winDisplayText;
    public float winDisplayTime = 4.5f;
    public string singulerWinnerText = "winner";
    public string multipleWinnerText = "winners";
    [Header("player feedback")]
    [SerializeField] private Text errorText;
    [SerializeField] private string fullMatchMessage;

    [Header("optional addons")]
    public GameObject matchUI;
    public KillCounter killCounter;
    public Text timeDisplay;
    public loadoutmanager LoadoutManager;

    [Header("set these to be the player limit, don't change the contents")]
    [SerializeField][UdonSynced] private int[] optedplayerIDs = new int[12];
    [SerializeField][UdonSynced] private bool[] playingPlayers = new bool[12];
    [SerializeField][UdonSynced] private int[] originalPlayerKills = new int[12];
    [SerializeField][UdonSynced] private int[] originalPlayerDeaths = new int[12];
    private Transform[] originalSpawnpoints;

    private VRCPlayerApi localPlayer;
    private bool optedIn;
    private bool inMatch;
    private int ID;
    [UdonSynced] private bool matchrunning = false;
    private float WinTime;
    private float TimeRemaining;

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }
    public void _SwitchOptState()
    {
        //all stored IDs are shifted by one so that 0 is void
        optedIn = !optedIn;
        if (optedIn)
        {
            _OptIn();
        }
        else
        {
            _OptOut();
        }

        //finalise and release changes
        
    }
    public void _OptIn()
    {

        /*
        //check for unused places
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            if (VRCPlayerApi.GetPlayerById(optedplayerIDs[i]) != null)
            {
                //location is used see if it is valid
                Debug.Log("non-empty space in array");
                if (!VRCPlayerApi.GetPlayerById(optedplayerIDs[i] - 1).IsValid())
                {
                    Debug.Log("populating space " + i + " with ID: " + localPlayer.playerId);
                    optedplayerIDs[i] = localPlayer.playerId + 1;
                    Debug.Log("shifting ID to: " + optedplayerIDs[i]);
                    if (!matchrunning)
                    {
                        startButton.interactable = true;
                    }
                    break;
                }
            }
            else
            {
                //the space is free either way
                Debug.Log("populating space " + i + " with ID: " + localPlayer.playerId);
                optedplayerIDs[i] = localPlayer.playerId + 1;
                Debug.Log("shifting ID to: " + optedplayerIDs[i]);
                if (!matchrunning)
                {
                    startButton.interactable = true;
                }
                break;
            }
        }
        */

        
        //check for unused places
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            if (!Utilities.IsValid(optedplayerIDs[i] - 1)||optedplayerIDs[i] == 0)
            {
                Debug.Log("populating space " + i + " with ID: " + localPlayer.playerId);
                optedplayerIDs[i] = localPlayer.playerId + 1;
                Debug.Log("shifting ID to: " + optedplayerIDs[i]);
                if (!matchrunning)
                {
                    startButton.interactable = true;
                }
                if (LoadoutManager != null)
                {
                    if (LoadoutManager.SelectionButton.interactable == true)
                    {
                        LoadoutManager.DisplayLoadout();
                        LoadoutManager.SelectDisplayedLoadout();
                    }
                }
                Networking.SetOwner(localPlayer, gameObject);
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(UpdateKillScoreboard));
                RequestSerialization();
                return;
            }
        }
        //if this is reached there are no free places
        optedIn = false;
    }
    public void _OptOut()
    {
        //remove self from list
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            if (optedplayerIDs[i] - 1 == localPlayer.playerId)
            {
                optedplayerIDs[i] = 0;
                startButton.interactable = false;
            }
        }
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(UpdateKillScoreboard));
        Networking.SetOwner(localPlayer, gameObject);

        RequestSerialization();
    }

    public void TeleportOptedPlayers()
    {
        if (optedIn)
        {
            inMatch = true;
            healthManager.SetOptState(true);
            //check if the loadoutmanager exists and apply the selected loadout
            if (LoadoutManager != null)
            {
                LoadoutManager.ApplySelectedLoadout();
            }
            //run locally
            ID = findID();
            int TeleportIndex = Random.Range(0, spawnLocations.Length - 1);
            localPlayer.TeleportTo(spawnLocations[TeleportIndex].position, spawnLocations[TeleportIndex].rotation);
            originalSpawnpoints = healthManager.RespawnPoint;
            healthManager.RespawnPoint = spawnLocations;
            TimeRemaining = maxTimeInSeconds;
            if (matchUI)
            {
                matchUI.SetActive(true);
            }
        }
        
    }

    public int findID()
    {
        int playerID = localPlayer.playerId;
        
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            if(optedplayerIDs[i]-1 == playerID)
            {
                return i;
            }
        }

        return -1;
    }

    public void _StartMatch()
    {
        
        //check if there are enough players
        int playercount = 0;
        playingPlayers = new bool[optedplayerIDs.Length];
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            //check they are valid
            if(optedplayerIDs[i] != 0)
            {
                //since they are valid they can add to the count of players
                Debug.Log("found valid player");
                playingPlayers[i] = true;
                playercount++;
            }
        }
        Debug.Log("playercount: " + playercount);
        if(playercount < 2)
        {
            Debug.Log("not enough players for match to start");
            return;
        }
        Networking.SetOwner(localPlayer, gameObject);
        matchrunning = true;
        //find all the players' current kills
        for (int i = 0; i < optedplayerIDs.Length; i++)
        {
            originalPlayerKills[i] = Killtracker.getKillCount(optedplayerIDs[i]-1);
            originalPlayerDeaths[i] = Killtracker.getDeathCount(optedplayerIDs[i] - 1);
        }
        if(matchUI)
        {
            matchUI.SetActive(true);
        }
        if (killCounter)
        {
            killCounter.SetOriginalKillCountFromKillTracker();
            killCounter.DisplayCurrentKills();
        }
        //have all objectpools fulfill all orders
        for (int i = 0; i < objectPools.Length; i++)
        {
            objectPools[i].FulfillAllPreOrders();
        }
        RequestSerialization();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(TeleportOptedPlayers));
    }
    // make an end match function
    public void _DeclareMatchEnd()
    {
        //for timer events only have the owner call this event
        matchrunning = false;
        Networking.SetOwner(localPlayer, gameObject);
        RequestSerialization();
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(EndMatch));
    }
    public void addWinnerToDisplay(int playerID)
    {
        VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerID);
        GameObject newNameDisplayTag = Object.Instantiate(nameDisplayTemplate);
        Text nameText = (Text)newNameDisplayTag.transform.GetChild(0).GetComponent(typeof(Text));
        nameText.text = player.displayName;

        newNameDisplayTag.transform.SetParent(nameDisplayParent);
        newNameDisplayTag.transform.localScale = nameDisplayTemplate.transform.localScale;
        newNameDisplayTag.transform.rotation = nameDisplayTemplate.transform.rotation;
        newNameDisplayTag.transform.position = nameDisplayTemplate.transform.position;
        newNameDisplayTag.SetActive(true);
    }
    public void EndMatch()
    {
        //clear the win display
        for (int i = 0; i < nameDisplayParent.childCount; i++)
        {
            Destroy(nameDisplayParent.GetChild(i).gameObject);
        }
        //get current amount of kills
        int[] kills = new int[originalPlayerKills.Length];
        for (int i = 0; i < kills.Length; i++)
        {
            kills[i] = Killtracker.getKillCount(optedplayerIDs[i] - 1) - originalPlayerKills[i];
        }
        //check the highest amount of kills
        int highestKillCount = Mathf.Max(kills);
        //check how many people have that amount of kills and generate a win banner for them
        int winnerCount = 0;
        for (int i = 0; i < kills.Length; i++)
        {
            if(kills[i] == highestKillCount)
            {
                addWinnerToDisplay(optedplayerIDs[i] - 1);
                winnerCount++;
            }
        }
        if(winnerCount > 1)
        {
            winDisplayText.text = multipleWinnerText;
        }
        else
        {
            winDisplayText.text = singulerWinnerText;
        }
        //enable the win display
        WinDisplay.SetActive(true);
        WinTime = winDisplayTime;
        if(matchUI)
        {
            matchUI.SetActive(false);
        }
        //teleport players back
        if(inMatch)
        {
            healthManager.RespawnPoint = originalSpawnpoints;
            localPlayer.TeleportTo(LobbySpawn.position, LobbySpawn.rotation);
            healthManager.SetOptState(false);
            inMatch = false;
        }
        //reset all items
        LoadoutManager.returnAllItems();
    }
    public void leaveMatch()
    {
        if (matchrunning&&optedIn)
        {
            _OptOut();
            healthManager.SetOptState(false);
            healthManager.RespawnPoint = originalSpawnpoints;
            inMatch = false;
            LoadoutManager.returnAllItems();
        }
    }
    public void OnPlayerRespawn(VRCPlayerApi player)
    {
        if(player == localPlayer)
        {
            if(optedIn&&matchrunning)
            {
                //here we make sure the player is actually in the match
                int checkedID = findID();
                if(checkedID != -1)
                {
                    leaveMatch();
                }
            }
        }
    }
    public void _KillTrackerKillRecieved()
    {
        if(killCounter)
        {
            killCounter.DisplayCurrentKills();
        }
        if(endCondition == endCon.killLimit||endCondition == endCon.killLimitAndTimer)
        {
            //check if player is opted
            int internalID = findID();
            if (ID != -1)
            {//check if localplayer has hit the kill limit

                if ((Killtracker.getKillCount(localPlayer.playerId) - originalPlayerKills[internalID]  ) >= maxKillForMatchEnd)
                {
                    //player has enough kills call for end of match
                    _DeclareMatchEnd();
                }
            }
            else
            {
                Debug.Log("player not opted in");
            }
        }
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(UpdateKillScoreboard));
    }
    public void TestWinDisplay()
    {
        WinDisplay.SetActive(true);
        WinTime = winDisplayTime;
    }
    public void UpdateKillScoreboard()
    {
        
        if (killScoreboardDisplay)
        {
            string[] validNames = new string[optedplayerIDs.Length];
            bool[] validIDs = new bool[optedplayerIDs.Length];
            int validPlayerCounter = 0;
            //first, find the amount of valid players, grab their names, mark their location on the list
            for (int i = 0; i < optedplayerIDs.Length; i++)
            {
                //first check if the player ID is a fake null
                if (optedplayerIDs[i] != 0)
                {
                    //get the true ID and validate it
                    int playerID = optedplayerIDs[i] - 1;
                    VRCPlayerApi player = VRCPlayerApi.GetPlayerById(playerID);
                    if (Utilities.IsValid(player))
                    {
                        
                        validNames[i] = player.displayName;
                        validIDs[i] = true;
                        validPlayerCounter++;
                    }
                }
            }

            //create and fill submission arrays
            //make them the correct length because I don't have lists, yay!
            string[] namesToSubmit = new string[validPlayerCounter];
            int[] killsToSubmit = new int[validPlayerCounter];
            int[] deathsToSubmit = new int[validPlayerCounter];
            int submissionCounter = 0;
            for (int i = 0; i < optedplayerIDs.Length; i++)
            {
                if (validIDs[i])
                {
                    namesToSubmit[submissionCounter] = validNames[i];
                    deathsToSubmit[submissionCounter] = Killtracker.getDeathCount(optedplayerIDs[i] - 1) - originalPlayerDeaths[i];
                    killsToSubmit[submissionCounter] = Killtracker.getKillCount(optedplayerIDs[i] - 1) - originalPlayerKills[i];
                    submissionCounter++;
                }
            }
            killScoreboardDisplay.GenerateList(namesToSubmit, killsToSubmit, deathsToSubmit);
        }
    }
    public void Update()
    {
        if (WinDisplay.activeSelf)
        {
            WinTime -= Time.deltaTime;
            if (WinTime <= 0)
            {
                WinDisplay.SetActive(false);
            }
        }
        if (matchrunning && (endCondition == endCon.Timer || endCondition == endCon.killLimitAndTimer))
        {
            if(TimeRemaining > 0)
            {

                TimeRemaining -= Time.deltaTime;
                if (TimeRemaining < 0)
                {
                    TimeRemaining = 0;
                }
                if (timeDisplay)
                {
                    //display time in minutes and seconds
                    int minutes = (int)TimeRemaining / 60;
                    int seconds = (int)TimeRemaining % 60;
                    timeDisplay.text = minutes.ToString("00") + ":" + seconds.ToString("00");
                }
            }
            else
            {
                if(localPlayer == Networking.GetOwner(gameObject))
                {
                    _DeclareMatchEnd();
                }
            }
        }
        
    }
    public void OnDeserialization()
    {
        //do stuff with synced variables

        //start button shit
        if (optedIn && !matchrunning) 
        { startButton.interactable = true; }
        else { startButton.interactable = false; }
        if (matchrunning)
        {
            
        }
        UpdateKillScoreboard();
    }
}
