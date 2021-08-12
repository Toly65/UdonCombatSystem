
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;

public class StatManager : UdonSharpBehaviour
{
    public PlayerHealthManager playerHealthManager;
    public Image healthBar;
    public Image hungerBar;
    
    public void Update()
    {
        hungerBar.fillAmount = playerHealthManager.CurrentHunger / 100;
        healthBar.fillAmount = playerHealthManager.CurrentHealth / 100;
    }

    
}
