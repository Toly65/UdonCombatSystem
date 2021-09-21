
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
public class SimpleObjcetSpawner : UdonSharpBehaviour
{
    public  VRCObjectPool objectPool;
    public Transform spawnLocation;
    public void OnInteract()
    {
        Networking.SetOwner(Networking.LocalPlayer, objectPool.gameObject);
        Transform child = objectPool.TryToSpawn().transform;
        Networking.SetOwner(Networking.LocalPlayer, child.gameObject);
        child.SetPositionAndRotation(spawnLocation.position, spawnLocation.rotation);
    }
}
