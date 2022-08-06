
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadout : UdonSharpBehaviour
{
    public StowPoint[] TargetStowPoints;
    public ItemManager[] items;
    private int[][] UniqueItemsStowPointIDs;
    private ItemManager[] uniqueItems;
    private VRC_Pickup[] OrderedItems;
    private void Start()
    {
        //remove duplicates from items and output the result into uniqueItems
        uniqueItems = new ItemManager[items.Length];
        int uniqueItemCount = 0;
        for (int i = 0; i < items.Length; i++)
        {
            bool isUnique = true;
            for (int j = 0; j < uniqueItems.Length; j++)
            {
                if (items[i] == uniqueItems[j])
                {
                    isUnique = false;
                    break;
                }
            }
            if (isUnique)
            {
                uniqueItems[uniqueItemCount] = items[i];
                uniqueItemCount++;
            }
        }
        //remove null spaces from uniqueItems
        ItemManager[] tempUniqueItems = new ItemManager[uniqueItemCount];
        for (int i = 0; i < uniqueItemCount; i++)
        {
            tempUniqueItems[i] = uniqueItems[i];
        }
        uniqueItems = tempUniqueItems;
        //create UniqueItemsStowPointIDs
        UniqueItemsStowPointIDs = new int[uniqueItems.Length][];
        //create a 2D array of ints to store the stowpoint IDs of the unique items
        for (int i = 0; i < uniqueItems.Length; i++)
        {
            for (int j = 0; j < items.Length; j++)
            {
                if (items[j].name == uniqueItems[i].name)
                {
                    //check if array exists within the array
                    if (UniqueItemsStowPointIDs[i] == null)
                    {
                        UniqueItemsStowPointIDs[i] = new int[1];
                        UniqueItemsStowPointIDs[i][0] = j;
                    }
                    else
                    {
                        int[] temp = new int[UniqueItemsStowPointIDs[i].Length + 1];
                        for (int k = 0; k < UniqueItemsStowPointIDs[i].Length; k++)
                        {
                            temp[k] = UniqueItemsStowPointIDs[i][k];
                        }
                        temp[temp.Length - 1] = j;
                        UniqueItemsStowPointIDs[i] = temp;
                    }
                }
            }
        }
    }
    public void CancelAllPreOrders()
    {
        for (int i = 0; i < uniqueItems.Length; i++)
        {
            uniqueItems[i].objectPool.PlayerCancelPreOrders();
        }
    }
    public void PreOrderItems()
    {
        OrderedItems = new VRC_Pickup[items.Length];
        for (int i = 0; i < uniqueItems.Length; i++)
        {
            if (UniqueItemsStowPointIDs[i].Length > 1)
            {
                //create a new array to store the ordered items
                GameObject[] tempOrderedItems = uniqueItems[i].objectPool.PlayerPreOrderMultiple(UniqueItemsStowPointIDs[i].Length);
                //put them in the required places in the orderedItemsArray
                //check if the ordered items exist
                if (tempOrderedItems != null)
                {
                    for (int j = 0; j < UniqueItemsStowPointIDs[i].Length; j++)
                    {
                        //check if the orderedItem has a pickup component
                        if (tempOrderedItems[j].GetComponent<VRC_Pickup>() != null)
                        {
                            OrderedItems[UniqueItemsStowPointIDs[i][j]] = tempOrderedItems[j].GetComponent<VRC_Pickup>();
                        }
                        else
                        {
                            Debug.LogError("Item " + tempOrderedItems[j].name + " does not have a VRC_Pickup component.");
                        }
                    }
                }else
                {
                    Debug.LogError("Item " + uniqueItems[i].name + " does not have any pre-ordered items.");
                }

            }
            if (UniqueItemsStowPointIDs[i].Length == 1)
            {
                OrderedItems[UniqueItemsStowPointIDs[i][0]] = uniqueItems[i].objectPool.PlayerPreOrderSingle().GetComponent<VRC_Pickup>();
            }
        }
    }
    public void ApplyLoadout()
    {
        for (int i = 0; i < items.Length; i++)
        {
            //force lock the ordered items onto the respective stowpoints
            if (OrderedItems[i] != null)
            {
                TargetStowPoints[i].ForceItemLock(OrderedItems[i]);
            }
        }
    }
}