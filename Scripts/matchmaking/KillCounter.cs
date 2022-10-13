
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class KillCounter : UdonSharpBehaviour
{
    public killTracker KillTracker;
    public string prefix = "kills: ";
    public Text killCounterText;
    private int originalKillCount = 0;

    public void DisplayCurrentKills()
    {
        killCounterText.text = prefix + (KillTracker.getKillCount(Networking.LocalPlayer.playerId) - originalKillCount);
    }
    public int GetLocalKillCount()
    {
        return KillTracker.getKillCount(Networking.LocalPlayer.playerId) - originalKillCount;
    }
    public void SetOriginalKillCount(int killcount)
    {
        originalKillCount = killcount;
    }

    public void SetOriginalKillCountFromKillTracker()
    {
        originalKillCount = KillTracker.getKillCount(Networking.LocalPlayer.playerId);
    }
}
