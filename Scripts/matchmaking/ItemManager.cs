
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[RequireComponent(typeof(BetterObjectPool))]
public class ItemManager : UdonSharpBehaviour
{
    public Sprite thumbnail;
    public string Name;
    public string itemClass;
    [TextArea(15, 20)]
    public string description;
    public BetterObjectPool objectPool;
    [Header("these may be configured automatically if you use the combat system's guns")]
    public float damage;
    public float fireRate;
    

    private void Start()
    {
        objectPool = gameObject.GetComponent<BetterObjectPool>();
        //check the first child of the first child of the object pool for a gun script
        if (objectPool.transform.GetChild(0).GetChild(0).GetComponent<Gun>())
        {
            Gun gun = objectPool.transform.GetChild(0).GetChild(0).GetComponent<Gun>();
            damage = gun.RaycastDamageAmount;
            fireRate = 1 / gun.CycleTime;
        }

    }
}