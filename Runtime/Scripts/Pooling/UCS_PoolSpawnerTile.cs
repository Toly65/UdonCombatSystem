using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One row of a UCS_PoolSpawnerMenu: icon, name, +/- quantity, instant spawn and how many are left in the pool.
// The menu fills it in at Start (AssignItem); remaining comes straight from the synced BulkPreOrderPool.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_PoolSpawnerTile : UdonSharpBehaviour
{
    [SerializeField] private Image itemPic;
    [SerializeField] private TextMeshProUGUI itemNameText;
    [SerializeField] private TextMeshProUGUI quantitySelectedText;
    [SerializeField] private TextMeshProUGUI quantityRemainingText;
    [SerializeField] private GameObject greyoutOverlay;
    [SerializeField] private Button decreaseQuantityButton;
    [SerializeField] private UCS_PoolSpawnerMenu menu;

    private BulkPreOrderPool pool;
    private string itemName;
    private int itemIndex;
    private int quantitySelected;

    public void AssignItem(string displayName, Sprite icon, BulkPreOrderPool itemPool, int index, UCS_PoolSpawnerMenu owner)
    {
        itemName = displayName;
        pool = itemPool;
        itemIndex = index;
        menu = owner;
        itemPic.sprite = icon;
        itemNameText.text = displayName;
        UpdateQuantityDisplay();
        UpdateRemainingDisplay();
    }

    public void IncreaseQuantity()
    {
        quantitySelected++;
        UpdateQuantityDisplay();
        menu.OnTileQuantityChanged(itemIndex, itemName, quantitySelected);
    }

    public void DecreaseQuantity()
    {
        if (quantitySelected <= 0) return;
        quantitySelected--;
        UpdateQuantityDisplay();
        menu.OnTileQuantityChanged(itemIndex, itemName, quantitySelected);
    }

    public void SpawnSingle()
    {
        menu.SpawnItemByIndex(itemIndex);
    }

    public void ResetQuantitySelected()
    {
        quantitySelected = 0;
        UpdateQuantityDisplay();
        menu.OnTileQuantityChanged(itemIndex, null, 0);
    }

    public void UpdateRemainingDisplay()
    {
        if (pool == null) return;
        int remaining = pool.GetFreeCount();
        quantityRemainingText.text = remaining.ToString();
        if (greyoutOverlay != null) greyoutOverlay.SetActive(remaining <= 0);
    }

    public int GetQuantitySelected() { return quantitySelected; }

    private void UpdateQuantityDisplay()
    {
        quantitySelectedText.text = quantitySelected.ToString();
        decreaseQuantityButton.interactable = quantitySelected > 0;
    }
}
