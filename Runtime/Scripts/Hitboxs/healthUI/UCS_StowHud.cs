
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;

/// <summary>
/// Desktop HUD listing the local player's stow points, Half-Life 2 style. Slots that share a key form a column:
/// collapsed it shows one tile (the item most recently stowed or cycled to, or the first slot when empty) with the key badge; while that
/// key is being used the column expands downward to show every slot, and the slot in front of the player is lit.
/// Each tile has the item's icon, a circle in the top-right for the slot's size class, and the key badge.
/// Icons come from the item's StowableManager (a BulkPreOrderPool fills them in from its Item Icon), falling back to
/// the first letter of its name. Hidden in VR, where the stow points are physical.
///
/// Needs two templates (Tools > Combat System > Create Stow HUD builds both). The column template is a vertical layout
/// group. The tile template's own Image is the background and it needs the children (looked up by name) "Icon" (Image),
/// "Label" (TextMeshProUGUI), "SizeDot" and "KeyBadge" with a "Key" (TextMeshProUGUI) inside it.
/// </summary>
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class UCS_StowHud : UdonSharpBehaviour
{
    [SerializeField] private PlayerStowPointManager stowManager;
    [Tooltip("Optional: with several stow layouts, the HUD follows the switcher's active one. Also add this HUD to the switcher's Changed Event Targets.")]
    [SerializeField] private StowLayoutSwitcher switcher;
    [Tooltip("Shown for stowed items with no icon of their own, instead of the name's first letter.")]
    [SerializeField] private Sprite fallbackIcon;

    [Header("UI")]
    [SerializeField] private GameObject columnTemplate;
    [SerializeField] private GameObject tileTemplate;
    [Tooltip("Parent of the columns (a horizontal layout group)")]
    [SerializeField] private Transform tileContainer;
    [Tooltip("A column stays expanded this long after its key was last used, so mashing the key doesn't flicker it closed")]
    [SerializeField] private float expandLingerSeconds = 1.2f;
    [SerializeField] private Color emptyColor = new Color(0.14f, 0.14f, 0.16f, 0.4f);
    [SerializeField] private Color filledColor = new Color(0.14f, 0.14f, 0.16f, 0.85f);
    [SerializeField] private Color selectedColor = new Color(0.24f, 0.26f, 0.3f, 0.95f);
    [SerializeField] private Color keyColor = Color.white;
    [SerializeField] private Color selectedKeyColor = new Color(0.2f, 0.85f, 0.55f, 1f); // VRChat HUD's active-key green
    [SerializeField] private float pollSeconds = 0.1f;

    private int[] groupFirst;      // per slot: the first slot that shares its key, which identifies its column
    private GameObject[] tiles;
    private GameObject[] badges;
    private Image[] backgrounds;
    private Image[] icons;
    private TMPro.TextMeshProUGUI[] labels;
    private TMPro.TextMeshProUGUI[] keyLabels;
    private VRC_Pickup[] shownPickups;
    private int[] shownState;      // 0 empty, 1 filled, 2 selected
    private bool[] tileShown;
    private bool[] badgeShown;
    private int[] topSlot;         // per group: the slot most recently stowed into or cycled to, shown when collapsed
    private GameObject[] columnObjects;
    private bool started;
    private bool firstPoll = true;
    private int expanded = -1;     // group (first slot) currently open
    private float expandedUntil;

    private void Start()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (switcher != null) stowManager = switcher.GetActiveManager();
        if (!Utilities.IsValid(localPlayer) || localPlayer.IsUserInVR() || stowManager == null)
        {
            gameObject.SetActive(false);
            return;
        }

        Build();
        started = true;
        SendCustomEventDelayedSeconds(nameof(_Poll), pollSeconds);
    }

    // Add this behaviour to the switcher's Changed Event Targets; the tiles are rebuilt for the new layout.
    public void OnStowLayoutChanged()
    {
        if (!started || switcher == null) return;

        PlayerStowPointManager next = switcher.GetActiveManager();
        if (next == null || next == stowManager) return;

        stowManager = next;
        Build();
    }

    private void Build()
    {
        if (columnObjects != null)
        {
            for (int i = 0; i < columnObjects.Length; i++)
            {
                if (columnObjects[i] == null) continue;
                columnObjects[i].SetActive(false);
                Destroy(columnObjects[i]);
            }
        }

        firstPoll = true;
        expanded = -1;
        int count = stowManager.GetStowPointCount();
        groupFirst = new int[count];
        tiles = new GameObject[count];
        badges = new GameObject[count];
        backgrounds = new Image[count];
        icons = new Image[count];
        labels = new TMPro.TextMeshProUGUI[count];
        keyLabels = new TMPro.TextMeshProUGUI[count];
        shownPickups = new VRC_Pickup[count];
        shownState = new int[count];
        tileShown = new bool[count];
        badgeShown = new bool[count];
        topSlot = new int[count];

        columnTemplate.SetActive(false);
        tileTemplate.SetActive(false);
        GameObject[] columns = new GameObject[count]; // indexed by a group's first slot
        columnObjects = columns;
        for (int i = 0; i < count; i++)
        {
            groupFirst[i] = i;
            topSlot[i] = i;
            for (int j = 0; j < i; j++)
            {
                if (stowManager.GetMappedKey(j) == stowManager.GetMappedKey(i))
                {
                    groupFirst[i] = j;
                    break;
                }
            }

            if (groupFirst[i] == i)
            {
                columns[i] = Instantiate(columnTemplate);
                columns[i].transform.SetParent(tileContainer, false);
                columns[i].SetActive(true);
            }

            GameObject tile = Instantiate(tileTemplate);
            tile.transform.SetParent(columns[groupFirst[i]].transform, false);
            tile.SetActive(true);

            tiles[i] = tile;
            tileShown[i] = true;
            backgrounds[i] = tile.GetComponent<Image>();
            icons[i] = tile.transform.Find("Icon").GetComponent<Image>();
            icons[i].enabled = false;
            labels[i] = tile.transform.Find("Label").GetComponent<TMPro.TextMeshProUGUI>();
            labels[i].text = "";
            badges[i] = tile.transform.Find("KeyBadge").gameObject;
            badgeShown[i] = true;
            keyLabels[i] = badges[i].transform.Find("Key").GetComponent<TMPro.TextMeshProUGUI>();
            keyLabels[i].text = KeyLabel(stowManager.GetMappedKey(i));
            shownState[i] = -1;

            StowPoint stow = stowManager.GetStowPoint(i);
            if (stow != null)
            {
                // size classes are 0-3, so the dot grows from 40% to 100% of its authored size
                tile.transform.Find("SizeDot").localScale = Vector3.one * ((stow.GetSizeClass() + 2) * 0.2f);
            }
        }
    }

    // ponytail: polls the stow manager at 10 Hz instead of stow points pushing events, so the stow system needs no HUD wiring
    public void _Poll()
    {
        int count = tiles.Length;
        int active = stowManager.GetActiveStow();
        if (active >= 0)
        {
            expanded = groupFirst[active];
            expandedUntil = Time.time + expandLingerSeconds;
        }
        else if (Time.time > expandedUntil)
        {
            expanded = -1;
        }

        for (int first = 0; first < count; first++)
        {
            if (groupFirst[first] != first) continue;

            for (int i = first; i < count; i++)
            {
                if (groupFirst[i] == first) RefreshSlot(i, active);
            }

            // collapsed, a column shows its most recent item (stowed or cycled to), else its first stowed item, else its first slot
            if (active >= 0 && groupFirst[active] == first && shownPickups[active] != null) topSlot[first] = active;
            int head = topSlot[first];
            if (shownPickups[head] == null)
            {
                head = first;
                for (int i = first; i < count; i++)
                {
                    if (groupFirst[i] == first && shownPickups[i] != null)
                    {
                        head = i;
                        break;
                    }
                }
            }

            bool badgeTaken = false; // the key badge sits on the column's top visible tile
            for (int i = first; i < count; i++)
            {
                if (groupFirst[i] != first) continue;

                bool show = first == expanded || i == head;
                if (show != tileShown[i])
                {
                    tileShown[i] = show;
                    tiles[i].SetActive(show);
                }

                bool badge = show && !badgeTaken;
                badgeTaken = badgeTaken || show;
                if (badge != badgeShown[i])
                {
                    badgeShown[i] = badge;
                    badges[i].SetActive(badge);
                }
            }
        }

        firstPoll = false;
        SendCustomEventDelayedSeconds(nameof(_Poll), pollSeconds);
    }

    private void RefreshSlot(int slot, int active)
    {
        StowPoint stow = stowManager.GetStowPoint(slot);
        VRC_Pickup pickup = stow == null ? null : stow.GetStowedPickup();

        if (pickup != shownPickups[slot])
        {
            shownPickups[slot] = pickup;
            shownState[slot] = -1; // force a redraw below
            ShowIcon(slot, pickup);
            // a newly stowed item becomes its column's top tile (not for what was already stowed when the HUD started)
            if (pickup != null && !firstPoll) topSlot[groupFirst[slot]] = slot;
        }

        int state = slot == active ? 2 : (pickup != null ? 1 : 0);
        if (state != shownState[slot])
        {
            shownState[slot] = state;
            backgrounds[slot].color = state == 2 ? selectedColor : (state == 1 ? filledColor : emptyColor);
            keyLabels[slot].color = state == 2 ? selectedKeyColor : keyColor;
        }
    }

    private void ShowIcon(int slot, VRC_Pickup pickup)
    {
        Sprite sprite = null;
        string label = "";
        if (pickup != null)
        {
            StowableManager item = stowManager.GetStowPoint(slot).GetStowedManager();
            if (item != null) sprite = item.GetStowIcon();
            if (sprite == null) sprite = fallbackIcon;
            if (sprite == null)
            {
                string itemName = item == null ? "" : item.GetStowName();
                label = string.IsNullOrEmpty(itemName) ? "?" : itemName.Substring(0, 1);
            }
        }

        icons[slot].sprite = sprite;
        icons[slot].enabled = sprite != null;
        labels[slot].text = label;
    }

    // Alpha keys are 48-57 (0-9) and letters 97-122; those are all the stow system offers. Anything else shows a dash.
    private string KeyLabel(int keyCode)
    {
        if (keyCode >= 48 && keyCode <= 57) return "0123456789".Substring(keyCode - 48, 1);
        if (keyCode >= 97 && keyCode <= 122) return "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Substring(keyCode - 97, 1);
        return "-";
    }
}
