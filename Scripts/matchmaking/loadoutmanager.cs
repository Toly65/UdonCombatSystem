
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadoutmanager : UdonSharpBehaviour
{
    [SerializeField] private loadout[] loadouts;
    private int currentLoadout = 0;
    private int previousLoadout = 0;
    public GameObject loadoutDisplayTemplate;
    public Transform contentParent;
    public Button SelectionButton;
    private bool intialised = false;
    private BetterObjectPool[] objectPools;

    //create a function which clears the content manager and displays the selected loadout's contents on the loadoutDisplayTemplates
    public void DisplayLoadout()
    {
        loadouts[previousLoadout].CancelAllPreOrders();
        if (SelectionButton)
        {
            SelectionButton.interactable = true;
        }
        
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
        previousLoadout = currentLoadout;
    }
    private void Start()
    {
        FindAllUniqueObjectPools();
    }
    public void FindAllUniqueObjectPools()
    {
        //count how many unique objects pools the loadouts have

        int uniqueObjectPools = 0;
        foreach (loadout loadout in loadouts)
        {
            foreach (ItemManager item in loadout.items)
            {
                if (item.objectPool != null)
                {
                    uniqueObjectPools++;
                }
            }
        }
        //create an array of object pools
        BetterObjectPool[] AllobjectPools = new BetterObjectPool[uniqueObjectPools];
        //find all the unique object pools and store them in the objectPools array
        int objectPoolsCounter = 0;
        foreach (loadout loadout in loadouts)
        {
            foreach (ItemManager item in loadout.items)
            {
                if (item.objectPool != null)
                {
                    AllobjectPools[objectPoolsCounter] = item.objectPool;
                    objectPoolsCounter++;
                }
            }
        }
        //remove any duplicate object pools from the AllObjectPools array
        for (int i = 0; i < AllobjectPools.Length; i++)
        {
            for (int j = i + 1; j < AllobjectPools.Length; j++)
            {
                if (AllobjectPools[i] == AllobjectPools[j])
                {
                    AllobjectPools[j] = null;
                }
            }
        }
        //count how many non null object pools are in the AllObjectPools array
        int nonNullObjectPools = 0;
        for (int i = 0; i < AllobjectPools.Length; i++)
        {
            if (AllobjectPools[i] != null)
            {
                nonNullObjectPools++;
            }
        }
        objectPools = new BetterObjectPool[nonNullObjectPools];
        //store the non null object pools in the objectPools array
        int objectPoolsCounter2 = 0;
        for (int i = 0; i < AllobjectPools.Length; i++)
        {
            if (AllobjectPools[i] != null)
            {
                objectPools[objectPoolsCounter2] = AllobjectPools[i];
                objectPoolsCounter2++;
            }
        }

    }

    public void ResetItems()
    {
        unlockAllItems();
    
        if (Networking.LocalPlayer == Networking.GetOwner(gameObject))
        {
            for (int i = 0; i < objectPools.Length; i++)
            {
                objectPools[i].ReturnAllObjects();
            }
        }
       
    }
    public void unlockAllItems()
    {
        StowPoint[] stowPoints = loadouts[0].TargetStowPoints;
        for (int i = 0; i < stowPoints.Length; i++)
        {
            stowPoints[i].ForceReleaseItemLock();
        }
    }

    public void returnAllItems()
    {
        if (Networking.LocalPlayer == Networking.GetOwner(gameObject))
        {
            for (int i = 0; i < objectPools.Length; i++)
            {
                objectPools[i].PlayerReturnAllOrderedObjects();
            }
        }
    }
    public void SelectDisplayedLoadout()
    {
        loadouts[previousLoadout].CancelAllPreOrders();
        loadouts[currentLoadout].PreOrderItems();
        if(SelectionButton)
        {
            SelectionButton.interactable = false;
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
