using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// Renders an object to a sprite for the spawn menu's item tiles. Ported from the VRCRP ItemClass "Bake Preview":
// same camera angle (25 degrees down, 45 degrees round), PNG into Assets/ItemPreviews.
// On a BulkPreOrderPool: the Bake Icon button under Item Icon, or Tools/Combat System/Bake All Pool Icons
// for every pool in the open scene. VRCRP's ItemClass inspector calls BakeObject too (by reflection, since it
// lives in the default assembly and can't reference this editor-only one).
public static class UCS_PoolIconBaker
{
    private const string OutFolder = "Assets/ItemPreviews";
    private const int PreviewLayer = 31; // temporarily used so the camera sees only the preview copy

    [MenuItem("Tools/Combat System/Bake All Pool Icons")]
    private static void BakeAll()
    {
        int baked = 0;
        foreach (BulkPreOrderPool pool in Object.FindObjectsOfType<BulkPreOrderPool>(true))
            if (Bake(pool, 512) != null) baked++;
        EditorUtility.DisplayDialog("Bake All Pool Icons", "Baked " + baked + " pool icon(s).", "OK");
    }

    /// <summary>Bakes the pool's first object and assigns the result as the pool's icon. Returns the sprite, or null on failure.</summary>
    public static Sprite Bake(BulkPreOrderPool pool, int size)
    {
        if (pool == null || pool.GetObjectCount() == 0 || pool.GetObjectAt(0) == null)
        {
            Debug.LogWarning("[UCS_PoolIconBaker] Pool has no managed objects to bake.", pool);
            return null;
        }

        var so = new SerializedObject(pool);
        SerializedProperty icon = so.FindProperty("itemIcon");
        SerializedProperty nameProp = so.FindProperty("itemName");
        // no name yet: use the first object's, minus a trailing " (3)" or " 3" copy number
        if (string.IsNullOrEmpty(nameProp.stringValue))
            nameProp.stringValue = Regex.Replace(pool.GetObjectAt(0).name, @"\s*\(\d+\)$|\s+\d+$", "");
        string baseName = string.IsNullOrEmpty(pool.GetItemName()) ? pool.name : pool.GetItemName();
        Sprite sprite = BakeObject(pool.GetObjectAt(0), baseName, AssetDatabase.GetAssetPath(icon.objectReferenceValue), size);
        if (sprite == null) return null;

        icon.objectReferenceValue = sprite;
        so.ApplyModifiedProperties(); // also keeps the name set above
        UdonSharpEditor.UdonSharpEditorUtility.CopyProxyToUdon(pool);
        EditorUtility.SetDirty(pool);
        return sprite;
    }

    /// <summary>
    /// Renders a copy of <paramref name="source"/> (scene object or prefab) to a PNG sprite named after
    /// <paramref name="baseName"/>. If <paramref name="existingIconPath"/> is already in the previews folder it is
    /// overwritten instead of making a new file. Returns null on failure.
    /// </summary>
    public static Sprite BakeObject(GameObject source, string baseName, string existingIconPath, int size)
    {
        return BakeObjectAt(source, baseName, existingIconPath, size, 25f, -45f, 45f);
    }

    /// <summary>
    /// <see cref="BakeObject"/> with the camera's pitch and yaw in degrees, and how far the object is turned from the
    /// camera's yaw (45 with yaw -45 shows an item three-quarters on; -camYaw turns the object to face +Z).
    /// </summary>
    public static Sprite BakeObjectAt(GameObject source, string baseName, string existingIconPath, int size, float camPitch, float camYaw, float objectYawOffset)
    {
        var tempRoot = new GameObject("__PreviewRoot_TEMP__") { hideFlags = HideFlags.HideAndDontSave };
        GameObject instance = null, camGO = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        try
        {
            instance = Object.Instantiate(source); // a copy, so inactive pooled objects bake too
            instance.SetActive(true);
            instance.transform.SetParent(tempRoot.transform, true);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localScale = Vector3.one;

            // camera 25 down, -45 yaw; object turned 45 to the right of that
            var camRotation = Quaternion.Euler(camPitch, camYaw, 0f);
            instance.transform.rotation = Quaternion.Euler(0f, camYaw + objectYawOffset, 0f);

            // only the visible model: particle and line renderers report bounds at the world origin and would
            // stretch the framing until the object vanishes
            Bounds bounds = new Bounds(instance.transform.position, Vector3.zero);
            bool any = false;
            foreach (Renderer r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (any) bounds.Encapsulate(r.bounds); else { bounds = r.bounds; any = true; }
            }
            if (!any) bounds = new Bounds(instance.transform.position, Vector3.one * 0.5f);

            foreach (Transform t in instance.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = PreviewLayer;

            camGO = new GameObject("__PreviewCam_TEMP__") { hideFlags = HideFlags.HideAndDontSave };
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.cullingMask = 1 << PreviewLayer;
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 2000f;
            cam.allowHDR = false;
            cam.allowMSAA = false;

            // distance that fits the bounding sphere, so a grenade fills the frame as well as a rifle does
            float distance = bounds.extents.magnitude / Mathf.Sin(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.1f;
            cam.transform.rotation = camRotation;
            cam.transform.position = bounds.center - cam.transform.forward * distance;

            rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 1 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            tex.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            if (!AssetDatabase.IsValidFolder(OutFolder)) AssetDatabase.CreateFolder("Assets", "ItemPreviews");
            foreach (char c in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(c, '_');
            string path = existingIconPath;
            if (string.IsNullOrEmpty(path) || !path.StartsWith(OutFolder + "/"))
                path = AssetDatabase.GenerateUniqueAssetPath(OutFolder + "/" + baseName + ".png");
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            Debug.Log("[UCS_PoolIconBaker] Baked " + path);
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[UCS_PoolIconBaker] Bake failed: " + e);
            return null;
        }
        finally
        {
            if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
            if (tex != null) Object.DestroyImmediate(tex);
            if (camGO != null) Object.DestroyImmediate(camGO);
            if (instance != null) Object.DestroyImmediate(instance);
            Object.DestroyImmediate(tempRoot);
        }
    }
}
