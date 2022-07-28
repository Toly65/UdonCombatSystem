
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
    private GameObject[] OrderedItems;
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
        //create a 2D array of ints to store the stowpoint IDs of the unique items
        for (int i = 0; i < uniqueItems.Length; i++)
        {
            for (int j = 0; j < items.Length; j++)
            {
                if (items[j] == uniqueItems[i])
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

    public void PreOrderItems()
    {
        //order items from the itemManagers' object pools
        
    }
    public void ApplyLoadout()
    {
        for (int i = 0; i < items.Length; i++)
        {
            VRC_Pickup targetPickup = items[i].objectPool.TryToSpawn().GetComponent<VRC_Pickup>();
            TargetStowPoints[i].ForceItemLock(targetPickup);
        }
    }
}