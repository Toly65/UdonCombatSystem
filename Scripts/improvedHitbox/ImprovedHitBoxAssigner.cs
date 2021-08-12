
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ImprovedHitBoxAssigner : UdonSharpBehaviour
{
    private VRCPlayerApi[] players = new VRCPlayerApi[80];
    public ImprovedHitBoxManager[] hitboxArray;
    //hitbox assignement stuff
    private void assignHitboxes()
    {
        VRCPlayerApi.GetPlayers(players);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] != null)
            {
                hitboxArray[i].gameObject.SetActive(true);
                hitboxArray[i].SetAssignedPlayer(players[i]);
            }
        }
        for (int i = 0; i < hitboxArray.Length; i++)
        {
            if(hitboxArray[i].assignedPlayer == null)
            {
                hitboxArray[i].gameObject.SetActive(false);
            }
        }
    }

    private void Start()
    {
        assignHitboxes();
    }
    private void OnPlayerJoined(VRCPlayerApi player)
    {
        assignHitboxes();
    }
    private void OnPlayerLeft(VRCPlayerApi player)
    {
        for (int i = 0; i < hitboxArray.Length; i++)
        {
            if (hitboxArray[i].assignedPlayer == player)
            {
                hitboxArray[i].gameObject.SetActive(false);
            }
        }
        //assignHitboxes();
    }
}
