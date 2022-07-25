
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class DamageStatsUIManager : UdonSharpBehaviour
{
    [SerializeField] private Text DamageText;
    [SerializeField] private Text RateOfFireText;
    [SerializeField] private Text DPSText;

    public void UpdateDamageText(float damage, float FireRate)
    {
        DamageText.text = "Damage: " + damage;
        RateOfFireText.text = "Fire rate: " + Mathf.Round(FireRate);
        DPSText.text = "DPS: " + Mathf.Round(damage * FireRate);
    }
}
