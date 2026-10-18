using UdonSharp;
using UnityEngine;

// Interact target for a UCS_PoolDispenser: sits on the Button child (needs a collider) and forwards the press.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_DispenserButton : UdonSharpBehaviour
{
    [SerializeField] private UCS_PoolDispenser dispenser;

    public override void Interact()
    {
        if (dispenser != null) dispenser._Dispense();
    }
}
