
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[RequireComponent(typeof(VRCObjectPool))]
public class ItemManager : UdonSharpBehaviour
{
    public Sprite thumbnail;
    public string Name;
    [TextArea(15, 20)]
    public string Description;
    [HideInInspector] public VRCObjectPool objectPool;


    private void Start()
    {
        objectPool = (VRCObjectPool)gameObject.GetComponent(typeof(VRCObjectPool));
    }
}