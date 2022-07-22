
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadout : UdonSharpBehaviour
{
    [SerializeField] private StowPoint[] TargetStowPoints;
    [SerializeField] private ItemManager[] Items;

    public void ApplyLoadout()
    {
        for (int i = 0; i < TargetStowPoints.Length; i++)
        {
            VRC_Pickup targetPickup = Items[i].objectPool.TryToSpawn().GetComponent<VRC_Pickup>();
            TargetStowPoints[i].ForceItemLock(targetPickup);
        }
    }
}