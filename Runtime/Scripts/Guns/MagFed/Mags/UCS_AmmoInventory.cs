
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_AmmoInventory : UdonSharpBehaviour
{
    [SerializeField] private string[] ammoTypeIds;

    [SerializeField] private int[] ammoCounts;
    [SerializeField] private bool[] ammoCountsDirty;

    private bool inventoryDirty;
    private int dirtyVersion;
    private int lastDirtyTypeIndex = -1;

    private void Start()
    {
        EnsureStorage();
    }

    private void EnsureStorage()
    {
        int count = ammoTypeIds != null ? ammoTypeIds.Length : 0;

        if (ammoCounts == null || ammoCounts.Length != count)
        {
            int[] newCounts = new int[count];
            if (ammoCounts != null)
            {
                int copyCount = Mathf.Min(ammoCounts.Length, newCounts.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    newCounts[i] = ammoCounts[i];
                }
            }
            ammoCounts = newCounts;
        }

        if (ammoCountsDirty == null || ammoCountsDirty.Length != count)
        {
            bool[] newDirty = new bool[count];
            if (ammoCountsDirty != null)
            {
                int copyCount = Mathf.Min(ammoCountsDirty.Length, newDirty.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    newDirty[i] = ammoCountsDirty[i];
                }
            }
            ammoCountsDirty = newDirty;
        }
    }

    private int FindTypeIndex(string ammoTypeId)
    {
        EnsureStorage();

        if (string.IsNullOrEmpty(ammoTypeId))
        {
            return -1;
        }

        if (ammoTypeIds == null)
        {
            return -1;
        }

        for (int i = 0; i < ammoTypeIds.Length; i++)
        {
            if (ammoTypeIds[i] == ammoTypeId)
            {
                return i;
            }
        }

        return -1;
    }

    private int EnsureTypeSlot(string ammoTypeId)
    {
        EnsureStorage();

        int existingIndex = FindTypeIndex(ammoTypeId);
        if (existingIndex >= 0)
        {
            return existingIndex;
        }

        if (string.IsNullOrEmpty(ammoTypeId))
        {
            return -1;
        }

        int oldLength = ammoTypeIds != null ? ammoTypeIds.Length : 0;
        string[] newIds = new string[oldLength + 1];
        int[] newCounts = new int[oldLength + 1];
        bool[] newDirty = new bool[oldLength + 1];

        for (int i = 0; i < oldLength; i++)
        {
            newIds[i] = ammoTypeIds[i];
            if (ammoCounts != null && i < ammoCounts.Length)
            {
                newCounts[i] = ammoCounts[i];
            }
            if (ammoCountsDirty != null && i < ammoCountsDirty.Length)
            {
                newDirty[i] = ammoCountsDirty[i];
            }
        }

        newIds[oldLength] = ammoTypeId;
        ammoTypeIds = newIds;
        ammoCounts = newCounts;
        ammoCountsDirty = newDirty;
        return oldLength;
    }

    public int GetAmmoCount(string ammoTypeId)
    {
        int index = FindTypeIndex(ammoTypeId);
        return index >= 0 ? ammoCounts[index] : 0;
    }

    public void SetAmmoCount(string ammoTypeId, int count)
    {
        int index = EnsureTypeSlot(ammoTypeId);
        if (index < 0)
        {
            return;
        }

        ammoCounts[index] = Mathf.Max(0, count);
        MarkAmmoCountDirty(ammoTypeId);
    }

    public void AddAmmo(string ammoTypeId, int amount)
    {
        int index = EnsureTypeSlot(ammoTypeId);
        if (index < 0 || amount <= 0)
        {
            return;
        }

        ammoCounts[index] += amount;
        MarkAmmoCountDirty(ammoTypeId);
    }

    public bool ConsumeAmmo(string ammoTypeId, int amount)
    {
        int index = FindTypeIndex(ammoTypeId);
        if (index < 0 || amount <= 0)
        {
            return false;
        }

        if (ammoCounts[index] < amount)
        {
            return false;
        }

        ammoCounts[index] -= amount;
        MarkAmmoCountDirty(ammoTypeId);
        return true;
    }

    public void MarkAmmoCountDirty(string ammoTypeId)
    {
        int index = EnsureTypeSlot(ammoTypeId);
        if (index < 0)
        {
            return;
        }

        ammoCountsDirty[index] = true;
        inventoryDirty = true;
        dirtyVersion++;
        lastDirtyTypeIndex = index;
    }

    public bool IsAmmoCountDirty(string ammoTypeId)
    {
        int index = FindTypeIndex(ammoTypeId);
        return index >= 0 && ammoCountsDirty[index];
    }

    public bool IsInventoryDirty()
    {
        return inventoryDirty;
    }

    public int GetDirtyVersion()
    {
        return dirtyVersion;
    }

    public int GetLastDirtyTypeIndex()
    {
        return lastDirtyTypeIndex;
    }

    public void ClearDirtyFlags()
    {
        EnsureStorage();

        for (int i = 0; i < ammoCountsDirty.Length; i++)
        {
            ammoCountsDirty[i] = false;
        }

        inventoryDirty = false;
        lastDirtyTypeIndex = -1;
    }
}
