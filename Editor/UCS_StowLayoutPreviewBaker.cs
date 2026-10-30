using UnityEditor;
using UnityEngine;

// Renders a stow layout's pockets to the manager's Preview Image, using UCS_PoolIconBaker's camera. Seen from behind and a
// little above, so the player's right is on the right of the picture.
// The Bake Preview button on a PlayerStowPointManager's inspector calls Bake (by reflection, from the stow package), or
// Tools/Combat System/Bake All Stow Layout Previews does every layout in the open scene.
public static class UCS_StowLayoutPreviewBaker
{
    [MenuItem("Tools/Combat System/Bake All Stow Layout Previews")]
    private static void BakeAll()
    {
        int baked = 0;
        foreach (PlayerStowPointManager manager in Object.FindObjectsOfType<PlayerStowPointManager>(true))
            if (Bake(manager, 512) != null) baked++;
        EditorUtility.DisplayDialog("Bake All Stow Layout Previews", "Baked " + baked + " layout preview(s).", "OK");
    }

    public static Sprite Bake(PlayerStowPointManager manager, int size)
    {
        if (manager == null || manager.GetStowPointCount() == 0)
        {
            Debug.LogWarning("[UCS_StowLayoutPreviewBaker] Layout has no stow points to bake.", manager);
            return null;
        }

        var so = new SerializedObject(manager);
        SerializedProperty image = so.FindProperty("previewImage");
        string path = AssetDatabase.GetAssetPath(image.objectReferenceValue);
        Sprite sprite = UCS_PoolIconBaker.BakeObjectAt(manager.gameObject, "StowLayout_" + manager.GetLayoutName(), path, size, 15f, 0f, 0f);
        if (sprite == null) return null;

        image.objectReferenceValue = sprite;
        so.ApplyModifiedProperties();
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(manager);
        EditorUtility.SetDirty(manager);
        return sprite;
    }
}
