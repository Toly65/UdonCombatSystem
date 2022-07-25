
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class BulkOrderObjectPool : UdonSharpBehaviour
{
    [SerializeField]private GameObject[] managedObjects;
    [SerializeField] private GameObject[] clientScripts;
    [Header("Set array length to pool size")]
    //players that request objects
    [SerializeField][UdonSynced] private int[] playerIDsRequesting;
    //scripts that request objects
    [SerializeField][UdonSynced] private int[] clientScriptsIDsRequesting;
    private VRCPlayerApi localPlayer;
    //gameobjects that have been assigned to the local player
    private GameObject[] locallyAssignedGameobjects;
    
    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
    }
    public bool PlayerPreOrderObject()
    {
        int truePlayerID = localPlayer.playerId;
        //find an unclaimed object
        for (int i = 0; i < managedObjects.Length; i++)
        {
            if (playerIDsRequesting[i] == 0&& clientScriptsIDsRequesting[i] == 0)
            {
                playerIDsRequesting[i] = truePlayerID+1;               
                GameObject[] newLocallyAssignedGameobjects = new GameObject[locallyAssignedGameobjects.Length + 1];
                for (int j = 0; j < locallyAssignedGameobjects.Length; j++)
                {
                    newLocallyAssignedGameobjects[j] = locallyAssignedGameobjects[j];
                }
                locallyAssignedGameobjects = newLocallyAssignedGameobjects;
                //send changes to the network
                Networking.SetOwner(localPlayer, gameObject);
                RequestSerialization();
                return true;
            }
        }
        return false;
    }

    public void CancelPlayerPreOrder()
    {
        int fakePlayerID = localPlayer.playerId + 1;
        for (int i = 0; i < playerIDsRequesting.Length; i++)
        {
            //find every instance of the fake playerID in playerIDsRequesting and set them to 0
            if (playerIDsRequesting[i] == fakePlayerID)
            {
                playerIDsRequesting[i] = 0;
            }
            //send changes to network
            Networking.SetOwner(localPlayer, gameObject);
            RequestSerialization();
        }
    }
    
    public bool clientScriptPreOrder(int scriptID)
    {
        //find an unclaimed object
        for (int i = 0; i < managedObjects.Length; i++)
        {
            if (playerIDsRequesting[i] == 0 && clientScriptsIDsRequesting[i] == 0)
            {
                clientScriptsIDsRequesting[i] = scriptID;

                //send changes to the network
                Networking.SetOwner(localPlayer, gameObject);
                RequestSerialization();
                return true;
            }
        }
        return false;
    }
}
