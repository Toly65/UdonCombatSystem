
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
public class KillCounter : UdonSharpBehaviour
{
    public killTracker KillTracker;
    public Text killCounterText;
    private int originalKillCount = 0;

    public void DisplayCurrentKills()
    {
        killCounterText.text = "" + (KillTracker.getKillCount(Networking.LocalPlayer.playerId) - originalKillCount);
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
