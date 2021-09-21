
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class DamageDetector : UdonSharpBehaviour
{
    public ImprovedHitBoxManager manager;
    public float DamageModifier;
    [HideInInspector] public float Modifier=0;

    public void ModifyHealth()
    {
        Debug.Log("ModifyHealth Triggered, Damage applied on" + manager.assignedPlayer.displayName);
        manager.attemptDamageApplication(DamageModifier * Modifier);
        //manager.assignedPlayer.CombatSetCurrentHitpoints(manager.assignedPlayer.CombatGetCurrentHitpoints()+(DamageModifier * Modifier));
    }
}
