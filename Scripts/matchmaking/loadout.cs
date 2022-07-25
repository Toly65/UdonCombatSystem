
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadout : UdonSharpBehaviour
{
    public StowPoint[] TargetStowPoints;
    public ItemManager[] items;

    public void ApplyLoadout()
    {
        for (int i = 0; i < items.Length; i++)
        {
            VRC_Pickup targetPickup = items[i].objectPool.TryToSpawn().GetComponent<VRC_Pickup>();
            TargetStowPoints[i].ForceItemLock(targetPickup);
        }
    }
}