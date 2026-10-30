using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UdonSharpEditor;

// Builds the stow HUD under the same canvas as the ammo panel, styled like it (dark rounded panels, a copy of its key badge,
// the same UI material), and wires it to the scene's stow manager.
// Re-running replaces the existing "StowHud".
public static class UCS_StowHudBuilder
{
    // the ammo panel's assets in HealthSystem.prefab
    private const string UiMaterialGuid = "ed17a658e69be6b4fa259c3144656eb0";

    private static readonly Color PanelColor = new Color(0.14f, 0.14f, 0.16f, 0.85f);
    private static readonly Color GreyText = new Color(0.78f, 0.78f, 0.8f, 1f);

    [MenuItem("Tools/Combat System/Create Stow HUD")]
    private static void Create()
    {
        var health = Object.FindObjectOfType<UCS_HealthBarsManager>();
        var stow = Object.FindObjectOfType<PlayerStowPointManager>();
        if (health == null || stow == null)
        {
            EditorUtility.DisplayDialog("Create Stow HUD", "Needs a UCS_HealthBarsManager with an Ammo Panel and a PlayerStowPointManager in the open scene.", "OK");
            return;
        }

        // inside a prefab instance (Combat System Setup) the HUD belongs to the prefab: editing the instance would only add
        // scene overrides that fight the prefab's own copy
        string prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(health.gameObject);
        if (!string.IsNullOrEmpty(prefabPath) && prefabPath == PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(stow.gameObject))
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(prefabPath);
            bool built = Build(contents.GetComponentInChildren<UCS_HealthBarsManager>(true), contents.GetComponentInChildren<PlayerStowPointManager>(true));
            if (built) PrefabUtility.SaveAsPrefabAsset(contents, prefabPath);
            PrefabUtility.UnloadPrefabContents(contents);
            Debug.Log("[UCS_StowHudBuilder] Rebuilt the stow HUD in " + prefabPath);
            return;
        }

        if (!Build(health, stow))
        {
            EditorUtility.DisplayDialog("Create Stow HUD", "The health manager needs an Ammo Panel assigned.", "OK");
        }
    }

    /// <summary>Builds the HUD under the health manager's ammo panel canvas. Works on scene objects or prefab contents. Returns false if the inputs are missing.</summary>
    public static bool Build(UCS_HealthBarsManager health, PlayerStowPointManager stow)
    {
        GameObject ammo = health == null ? null : (GameObject)new SerializedObject(health).FindProperty("ammoPanel").objectReferenceValue;
        if (ammo == null || stow == null) return false;

        Transform canvas = ammo.transform.parent;
        Transform old;
        while ((old = canvas.Find("StowHud")) != null) Undo.DestroyObjectImmediate(old.gameObject);

        Sprite rounded = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        Sprite knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(UiMaterialGuid));

        // top-centre row of columns, level with the top edge of VRChat's desktop HUD (which sits in the corners), adjust the anchored position to taste
        GameObject strip = NewUI("StowHud", canvas, new Vector2(0.5f, 1f), new Vector2(0, -25), Vector2.zero);
        var row = strip.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8;
        row.childAlignment = TextAnchor.UpperCenter; // columns hang from the top, so expanding grows downward
        row.childControlWidth = row.childControlHeight = false;
        row.childForceExpandWidth = row.childForceExpandHeight = false;
        Fit(strip);

        GameObject column = NewUI("ColumnTemplate", strip.transform, new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);
        var stack = column.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 6;
        stack.childAlignment = TextAnchor.UpperCenter;
        stack.childControlWidth = stack.childControlHeight = false;
        stack.childForceExpandWidth = stack.childForceExpandHeight = false;
        Fit(column);

        GameObject tile = NewUI("TileTemplate", column.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(100, 100));
        Panel(tile, rounded, material, PanelColor);

        GameObject icon = NewUI("Icon", tile.transform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(78, 78));
        Image iconImage = icon.AddComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;
        iconImage.material = material;

        GameObject dot = NewUI("SizeDot", tile.transform, new Vector2(1, 1), new Vector2(-10, -10), new Vector2(18, 18));
        Image dotImage = dot.AddComponent<Image>();
        dotImage.sprite = knob;
        dotImage.color = new Color(GreyText.r, GreyText.g, GreyText.b, 0.9f);
        dotImage.raycastTarget = false;
        dotImage.material = material;

        // a copy of the ammo panel's reload key badge, so the key looks exactly like it
        Transform badgeSource = ammo.transform.Find("ReloadKeyBadge");
        GameObject badge = Object.Instantiate(badgeSource.gameObject, tile.transform, false);
        badge.name = "KeyBadge";
        badge.SetActive(true);
        var badgeRect = (RectTransform)badge.transform;
        badgeRect.anchorMin = badgeRect.anchorMax = badgeRect.pivot = Vector2.zero;
        badgeRect.anchoredPosition = new Vector2(8, 8);
        badgeRect.sizeDelta = new Vector2(36, 36);
        TextMeshProUGUI keyText = badge.GetComponentInChildren<TextMeshProUGUI>(true);
        keyText.name = "Key";
        keyText.fontSize = 26;

        // item name's first letter, shown when an item has no icon
        GameObject label = Object.Instantiate(keyText.gameObject, tile.transform, false);
        label.name = "Label";
        label.transform.SetSiblingIndex(1); // behind the key badge
        var labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = labelRect.anchorMax = labelRect.pivot = new Vector2(0.5f, 0.5f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(78, 78);
        var labelText = label.GetComponent<TextMeshProUGUI>();
        labelText.fontSize = 48;
        labelText.color = GreyText;
        labelText.text = "";

        UCS_StowHud hud = UdonSharpUndo.AddComponent<UCS_StowHud>(strip);
        var so = new SerializedObject(hud);
        so.FindProperty("stowManager").objectReferenceValue = stow;
        so.FindProperty("columnTemplate").objectReferenceValue = column;
        so.FindProperty("tileTemplate").objectReferenceValue = tile;
        so.FindProperty("tileContainer").objectReferenceValue = strip.transform;
        so.ApplyModifiedProperties();
        UdonSharpEditorUtility.CopyProxyToUdon(hud);

        Selection.activeGameObject = strip;
        Undo.RegisterCreatedObjectUndo(strip, "Create Stow HUD");
        return true;
    }

    private static void Fit(GameObject go)
    {
        var fit = go.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    // rounded, 9-sliced panel like the ammo panel's
    private static void Panel(GameObject go, Sprite sprite, Material material, Color color)
    {
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
        image.pixelsPerUnitMultiplier = 0.6f;
        image.color = color;
        image.raycastTarget = false;
        image.material = material;
    }

    // pivot = anchor, so the anchored position is an offset from that point of the parent
    private static GameObject NewUI(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return go;
    }
}
