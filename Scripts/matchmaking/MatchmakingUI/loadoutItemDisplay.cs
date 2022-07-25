
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using UnityEngine.UI;
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class loadoutItemDisplay : UdonSharpBehaviour
{
    [SerializeField] private Text itemNameText;
    [SerializeField] private Text itemDescriptionText;
    [SerializeField] private Image itemThumbnail;
    [SerializeField] private DamageStatsUIManager damageStatsDisplay;
    public void DisplayItem(ItemManager item)
    {
        itemNameText.text = item.name + " - " + item.itemClass;
        itemDescriptionText.text = item.description;
        if (item.thumbnail != null)
            itemThumbnail.sprite = item.thumbnail;
        
        //if the item isn't a weapon then we disable the damage stats display gameobject
        if (item.damage == 0)
        {
            damageStatsDisplay.gameObject.SetActive(false);
        }
        else
        {
            damageStatsDisplay.gameObject.SetActive(true);
            damageStatsDisplay.UpdateDamageText(item.damage, item.fireRate);
        }
        
    }
}
