
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

public class DespawnOnDrop : UdonSharpBehaviour
{
    Transform spawnPoint;
    private void Start()
    {
        spawnPoint = gameObject.transform;
    }
    public void OnDrop()
    {
        transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
    }
}
