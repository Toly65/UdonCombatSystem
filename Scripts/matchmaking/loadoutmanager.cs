
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadoutmanager : UdonSharpBehaviour
{
    [SerializeField] private loadout[] loadouts;
    private int currentLoadout = 0;
    public GameObject loadoutDisplayTemplate;
    public Transform contentParent;

    private void Start()
    {
        DisplayLoadout();
    }

    //create a function which clears the content manager and displays the selected loadout's contents on the loadoutDisplayTemplates
    public void DisplayLoadout()
    {
        //clear the content manager
        foreach (Transform child in contentParent)
        {
            Destroy(child.gameObject);
        }
        //display the selected loadout's contents on the loadoutDisplayTemplates
        foreach (ItemManager item in loadouts[currentLoadout].items)
        {
            GameObject newItem = Instantiate(loadoutDisplayTemplate, contentParent);
            newItem.GetComponent<loadoutItemDisplay>().DisplayItem(item);
            newItem.SetActive(true);
        }
    }

    public void DisplayNextLoadout()
    {
        currentLoadout++;
        if (currentLoadout >= loadouts.Length)
        {
            currentLoadout = 0;
        }
        DisplayLoadout();
    }

    public void DisplayPreviousLoadout()
    {
        currentLoadout--;
        if (currentLoadout < 0)
        {
            currentLoadout = loadouts.Length - 1;
        }
        DisplayLoadout();
    }
    
    public void ApplySelectedLoadout()
    {
        loadouts[currentLoadout].ApplyLoadout();
    }
}
