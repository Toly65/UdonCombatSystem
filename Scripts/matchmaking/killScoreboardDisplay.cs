
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class killScoreboardDisplay : UdonSharpBehaviour
{
    public GameObject Template;
    public Transform contentParent;
    public void GenerateList(string[] playernames, int[] playerKills, int[]playerDeaths)
    {
        //kill all children
        for (int i = 0; i < contentParent.childCount; i++)
        {
            Destroy(contentParent.GetChild(i).gameObject);
        }
        for (int i = 0; i < playernames.Length; i++)
        {
            GameObject newThing = VRCInstantiate(Template);
            killDisplayUnit displayUnit = newThing.GetComponent<killDisplayUnit>();
            displayUnit.NameDisplay.text = playernames[i];
            displayUnit.KillDisplay.text = "Kills: " + playerKills[i];
            displayUnit.DeathDisplay.text = "Deaths: " + playerDeaths[i];
            displayUnit.KDdisplay.text = "KD: " + (double)((double)playerKills[i] / (double)playerDeaths[i]);

            //positioning and parenting
            newThing.transform.position = Template.transform.position;
            newThing.transform.rotation = Template.transform.rotation;
            newThing.transform.SetParent(contentParent);
            newThing.transform.localScale = Template.transform.localScale;
            newThing.SetActive(true);
        }
    }
}
