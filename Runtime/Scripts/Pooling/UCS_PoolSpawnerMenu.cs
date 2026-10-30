using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Free-standing spawn menu for BulkPreOrderPools: one tile per pool with quantity selectors, Spawn All and a
// queue readout. Each pool needs a UCS_PoolDispenser (no button required) as its spawn engine; the menu just
// calls _Dispense on it, so reserve -> fulfill -> place, grip poses and recycling all come from the dispenser.
// Item, dispenser, name and icon arrays are parallel. Remaining counts are read from the synced pools, so
// every client's menu stays current without any events of its own.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_PoolSpawnerMenu : UdonSharpBehaviour
{
    [Header("Items (parallel arrays; name and icon come from each pool)")]
    [SerializeField] private BulkPreOrderPool[] pools;
    [SerializeField] private UCS_PoolDispenser[] dispensers;

    [Header("UI")]
    [Tooltip("Template tile. Existing UCS_PoolSpawnerTile children of the container are reused first, then copies of this are made.")]
    [SerializeField] private UCS_PoolSpawnerTile tilePrefab;
    [SerializeField] private Transform tileContainer;

    [Header("Queue Display")]
    [SerializeField] private GameObject queueDisplayTemplate;
    [SerializeField] private Transform queueDisplayContainer;
    [SerializeField] private int maxQueueDisplays = 8;

    [Header("Spawning")]
    [SerializeField] private float itemSpawnDelaySeconds = 0.5f;
    [Tooltip("How often the remaining counts are re-read from the pools.")]
    [SerializeField] private float refreshSeconds = 1f;

    private UCS_PoolSpawnerTile[] tiles;

    private int[] cartIndices = new int[50];
    private int[] cartQuantities = new int[50];
    private int cartCount;
    private int currentLine;
    private int remainingInLine;
    private bool isDispensing;

    private GameObject[] queueSlots;
    private TextMeshProUGUI[] queueSlotTexts;
    private int[] queueSlotItems; // pool index shown in each slot, -1 = free

    private void Start()
    {
        if (pools == null || pools.Length == 0) return;

        tiles = new UCS_PoolSpawnerTile[pools.Length];
        int childIndex = 0;
        for (int i = 0; i < pools.Length; i++)
        {
            UCS_PoolSpawnerTile tile = null;
            while (tile == null && childIndex < tileContainer.childCount)
            {
                tile = tileContainer.GetChild(childIndex).GetComponent<UCS_PoolSpawnerTile>();
                if (tile == null) childIndex++;
            }
            if (tile == null)
            {
                tile = Instantiate(tilePrefab.gameObject).GetComponent<UCS_PoolSpawnerTile>();
                tile.transform.SetParent(tileContainer, false);
            }
            childIndex++;

            tile.AssignItem(pools[i].GetItemName(), pools[i].GetItemIcon(), pools[i], i, this);
            tile.gameObject.SetActive(true);
            tiles[i] = tile;
        }

        queueDisplayTemplate.SetActive(false);
        queueSlots = new GameObject[maxQueueDisplays];
        queueSlotTexts = new TextMeshProUGUI[maxQueueDisplays];
        queueSlotItems = new int[maxQueueDisplays];
        for (int i = 0; i < maxQueueDisplays; i++)
        {
            queueSlots[i] = Instantiate(queueDisplayTemplate);
            queueSlots[i].transform.SetParent(queueDisplayContainer, false);
            queueSlotTexts[i] = queueSlots[i].GetComponentInChildren<TextMeshProUGUI>();
            queueSlots[i].SetActive(false);
            queueSlotItems[i] = -1;
        }

        SendCustomEventDelayedSeconds(nameof(_RefreshLoop), refreshSeconds);
    }

    public void _RefreshLoop()
    {
        RefreshAllRemainingDisplays();
        SendCustomEventDelayedSeconds(nameof(_RefreshLoop), refreshSeconds);
    }

    public void RefreshAllRemainingDisplays()
    {
        if (tiles == null) return;
        for (int i = 0; i < tiles.Length; i++) tiles[i].UpdateRemainingDisplay();
    }

    /// <summary>Spawn All button: dispense everything the tiles have selected, one item at a time.</summary>
    public void SpawnAllSelected()
    {
        if (isDispensing || tiles == null) return;

        cartCount = 0;
        for (int i = 0; i < tiles.Length; i++)
        {
            int qty = tiles[i].GetQuantitySelected();
            if (qty <= 0) continue;
            cartIndices[cartCount] = i;
            cartQuantities[cartCount] = qty;
            cartCount++;
        }
        if (cartCount == 0) return;

        isDispensing = true;
        currentLine = 0;
        remainingInLine = 0;
        DispenseNext();
    }

    public void DispenseNext()
    {
        if (currentLine >= cartCount)
        {
            isDispensing = false;
            ClearAllQueueDisplays();
            ClearSelection();
            return;
        }

        int item = cartIndices[currentLine];

        // a dispenser handles one item at a time; wait for it rather than have the press silently dropped
        if (dispensers[item].IsBusy())
        {
            SendCustomEventDelayedSeconds(nameof(DispenseNext), 0.1f);
            return;
        }

        if (remainingInLine <= 0) remainingInLine = cartQuantities[currentLine];
        dispensers[item]._Dispense();
        remainingInLine--;

        int slot = FindSlotForItem(item);
        if (slot >= 0) queueSlotTexts[slot].text = pools[item].GetItemName() + " X " + remainingInLine;

        if (remainingInLine <= 0) currentLine++;
        SendCustomEventDelayedSeconds(nameof(DispenseNext), itemSpawnDelaySeconds);
    }

    /// <summary>Instant-spawn button on a tile.</summary>
    public void SpawnItemByIndex(int index)
    {
        if (index < 0 || index >= dispensers.Length) return;
        dispensers[index]._Dispense();
    }

    public void ClearSelection()
    {
        if (tiles == null) return;
        for (int i = 0; i < tiles.Length; i++) tiles[i].ResetQuantitySelected();
    }

    /// <summary>Called by a tile when its quantity changes: create, update or free its queue slot.</summary>
    public void OnTileQuantityChanged(int itemIndex, string itemName, int newQuantity)
    {
        if (isDispensing || queueSlots == null) return;

        int slot = FindSlotForItem(itemIndex);
        if (newQuantity > 0)
        {
            if (slot < 0)
            {
                slot = FindSlotForItem(-1);
                if (slot < 0) return;
                queueSlots[slot].SetActive(true);
                queueSlotItems[slot] = itemIndex;
            }
            queueSlotTexts[slot].text = itemName + " X " + newQuantity;
        }
        else if (slot >= 0)
        {
            queueSlots[slot].SetActive(false);
            queueSlotItems[slot] = -1;
        }
    }

    private int FindSlotForItem(int itemIndex)
    {
        if (queueSlotItems == null) return -1;
        for (int i = 0; i < queueSlotItems.Length; i++)
            if (queueSlotItems[i] == itemIndex) return i;
        return -1;
    }

    private void ClearAllQueueDisplays()
    {
        if (queueSlots == null) return;
        for (int i = 0; i < queueSlots.Length; i++)
        {
            queueSlots[i].SetActive(false);
            queueSlotItems[i] = -1;
        }
    }
}
