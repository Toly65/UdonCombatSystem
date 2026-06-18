
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Components;
using VRC.SDKBase;
using VRC.Udon;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif
[UdonBehaviourSyncMode(BehaviourSyncMode.NoVariableSync)]

public class UCS_MagPool : UdonSharpBehaviour
{
    private int nextMagId = 1;
    private UCS_Mag[] cachedMags;
    private bool cachedMagsReady;

    [Header("Pool Generation")]
    [SerializeField] private string magTypeId = "Pistol";
    [SerializeField, Min(1)] private int maxAmmo = 30;
    [SerializeField] private UCS_AmmoInventory ammoInventory;
    [SerializeField] private string ammoInventoryTypeId = "";
    [SerializeField] private GameObject poolExample;
    [SerializeField, Min(1)] private int poolSize = 4;

    void Start()
    {
        PreparePoolMags();
    }

    private void OnEnable()
    {
        PreparePoolMags();
    }

    private void CachePoolMags()
    {
        if (cachedMagsReady)
        {
            return;
        }

        int magCount = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            UCS_Mag mag = child.gameObject.GetComponent<UCS_Mag>();
            if (mag == null)
            {
                mag = child.gameObject.GetComponentInChildren<UCS_Mag>(true);
            }

            if (mag != null)
            {
                magCount++;
            }
        }

        cachedMags = new UCS_Mag[magCount];
        int writeIndex = 0;
        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            UCS_Mag mag = child.gameObject.GetComponent<UCS_Mag>();
            if (mag == null)
            {
                mag = child.gameObject.GetComponentInChildren<UCS_Mag>(true);
            }

            if (mag != null)
            {
                cachedMags[writeIndex] = mag;
                writeIndex++;
            }
        }

        cachedMagsReady = true;
    }

    private void PreparePoolMags()
    {
        CachePoolMags();

        if (cachedMags == null)
        {
            return;
        }

        for (int i = 0; i < cachedMags.Length; i++)
        {
            UCS_Mag mag = cachedMags[i];
            if (mag == null)
            {
                continue;
            }

            if (!mag.gameObject.activeSelf)
            {
                mag.gameObject.SetActive(true);
            }

            if (mag.GetMagId() < 0)
            {
                mag.SetInUse(false);
                mag.ResetForPool();
            }
            else if (!mag.IsInUse())
            {
                mag.SetInUse(false);
            }
        }
    }

    public string GetMagTypeId()
    {
        return magTypeId;
    }

    public int GetMaxAmmo()
    {
        return Mathf.Max(1, maxAmmo);
    }

    private string GetResolvedAmmoInventoryTypeId()
    {
        if (!string.IsNullOrEmpty(ammoInventoryTypeId))
        {
            return ammoInventoryTypeId;
        }

        return magTypeId;
    }

    public UCS_Mag FindActiveMagNear(Vector3 position, float maxDistance)
    {
        CachePoolMags();

        if (cachedMags == null)
        {
            return null;
        }

        for (int i = 0; i < cachedMags.Length; i++)
        {
            UCS_Mag mag = cachedMags[i];
            if (mag == null || !mag.IsInUse()) continue;
            Transform pickupRoot = mag.GetPickupRootTransform();
            if (pickupRoot == null) continue;
            if (Vector3.Distance(pickupRoot.position, position) <= maxDistance)
                return mag;
        }
        return null;
    }

    public UCS_Mag FindActiveMagById(int magId)
    {
        if (magId < 0)
        {
            return null;
        }

        CachePoolMags();

        if (cachedMags == null)
        {
            return null;
        }

        for (int i = 0; i < cachedMags.Length; i++)
        {
            UCS_Mag mag = cachedMags[i];
            if (mag != null && mag.IsInUse() && mag.GetMagId() == magId)
            {
                return mag;
            }
        }

        return null;
    }

    public void ReturnMagToPool(GameObject mag)
    {
        if (mag == null)
        {
            return;
        }

        UCS_Mag magData = mag.GetComponent<UCS_Mag>();
        if (magData == null)
        {
            magData = mag.GetComponentInChildren<UCS_Mag>(true);
        }

        if (magData == null)
        {
            return;
        }

        Networking.SetOwner(Networking.LocalPlayer, magData.gameObject);
        Transform pickupRoot = magData.GetPickupRootTransform();
        if (pickupRoot != null && pickupRoot.gameObject != magData.gameObject)
        {
            Networking.SetOwner(Networking.LocalPlayer, pickupRoot.gameObject);
        }

        magData.ResetForPool();
    }

    public void ReturnMagToPool(UCS_Mag mag)
    {
        if (mag == null)
        {
            return;
        }

        if (ammoInventory != null)
        {
            int magAmmo = Mathf.Max(0, mag.GetCurrentAmmo());
            if (magAmmo > 0)
            {
                ammoInventory.AddAmmo(mag.GetMagTypeId(), magAmmo);
            }
        }

        mag.SetHeld(false);
        mag.SetSocketed(false);
        mag.ResetForPool();
        ReturnMagToPool(mag.gameObject);
    }

    public GameObject AcquireMag()
    {
        return AcquireFullMag(null);
    }

    public GameObject AcquireFullMag(VRCPlayerApi owner)
    {
        return SpawnMagWithAmmo(GetMaxAmmo(), false, null, owner);
    }

    public GameObject AcquireMagWithInventory()
    {
        int ammoToLoad = GetMaxAmmo();

        if (ammoInventory != null)
        {
            string inventoryTypeId = GetResolvedAmmoInventoryTypeId();
            ammoToLoad = Mathf.Min(GetMaxAmmo(), Mathf.Max(0, ammoInventory.GetAmmoCount(inventoryTypeId)));
        }

        return SpawnMagWithAmmo(ammoToLoad, true, ammoInventory, null);
    }

    public GameObject AcquireMagWithAmmo(int ammoCount)
    {
        return SpawnMagWithAmmo(ammoCount, false, null, null);
    }

    private GameObject SpawnMagWithAmmo(int ammoCount, bool consumeFromInventory, UCS_AmmoInventory inventorySource, VRCPlayerApi owner)
    {
        CachePoolMags();

        UCS_Mag magData = null;
        if (cachedMags != null)
        {
            for (int i = 0; i < cachedMags.Length; i++)
            {
                UCS_Mag candidate = cachedMags[i];
                if (candidate != null && candidate.GetMagId() < 0)
                {
                    magData = candidate;
                    break;
                }
            }
        }

        if (magData == null)
        {
            return null;
        }

        GameObject mag = magData.gameObject;
        if (!mag.activeSelf)
        {
            mag.SetActive(true);
        }

        if (owner != null)
        {
            Networking.SetOwner(owner, mag);
        }

        magData.ResetForPool();
        int assignedMagId = nextMagId++;
        magData.SetMagId(assignedMagId);
        magData.SetInUse(true);
        // If the pickup root is a separate child object it has its own VRCObjectSync and
        // needs ownership transferred independently of the pool root.
        if (owner != null)
        {
            Transform pickupRoot = magData.GetPickupRootTransform();
            if (pickupRoot != null && pickupRoot.gameObject != mag)
            {
                Networking.SetOwner(owner, pickupRoot.gameObject);
            }
        }

        magData.SetCurrentAmmo(Mathf.Clamp(ammoCount, 0, GetMaxAmmo()));

        if (consumeFromInventory && inventorySource != null)
        {
            string inventoryTypeId = GetResolvedAmmoInventoryTypeId();
            int ammoToConsume = Mathf.Min(GetMaxAmmo(), Mathf.Max(0, ammoCount));
            if (ammoToConsume > 0)
            {
                inventorySource.ConsumeAmmo(inventoryTypeId, ammoToConsume);
            }
        }

        return mag;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(UCS_MagPool))]
public class UCS_MagPoolEditor : Editor
{
    private SerializedProperty magTypeIdProp;
    private SerializedProperty maxAmmoProp;
    private SerializedProperty ammoInventoryProp;
    private SerializedProperty ammoInventoryTypeIdProp;
    private SerializedProperty poolExampleProp;
    private SerializedProperty poolSizeProp;

    private void OnEnable()
    {
        magTypeIdProp = serializedObject.FindProperty("magTypeId");
        maxAmmoProp = serializedObject.FindProperty("maxAmmo");
        ammoInventoryProp = serializedObject.FindProperty("ammoInventory");
        ammoInventoryTypeIdProp = serializedObject.FindProperty("ammoInventoryTypeId");
        poolExampleProp = serializedObject.FindProperty("poolExample");
        poolSizeProp = serializedObject.FindProperty("poolSize");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawPropertiesExcluding(serializedObject, "m_Script", "magTypeId", "maxAmmo", "ammoInventory", "ammoInventoryTypeId", "poolExample", "poolSize");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Magazine Definition", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(magTypeIdProp, new GUIContent("Mag Type ID"));
        EditorGUILayout.PropertyField(maxAmmoProp, new GUIContent("Max Ammo"));
        EditorGUILayout.PropertyField(ammoInventoryProp, new GUIContent("Ammo Inventory"));
        EditorGUILayout.PropertyField(ammoInventoryTypeIdProp, new GUIContent("Ammo Inventory Type ID"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pool Generation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(poolExampleProp, new GUIContent("Example GameObject"));
        EditorGUILayout.PropertyField(poolSizeProp, new GUIContent("Pool Size"));

        UCS_MagPool magPool = (UCS_MagPool)target;
        EditorGUI.BeginDisabledGroup(poolExampleProp.objectReferenceValue == null || poolSizeProp.intValue < 1);
        if (GUILayout.Button("Generate Pool"))
        {
            GeneratePool(magPool);
        }
        EditorGUI.EndDisabledGroup();

        serializedObject.ApplyModifiedProperties();
    }

    private void GeneratePool(UCS_MagPool magPool)
    {
        GameObject example = poolExampleProp.objectReferenceValue as GameObject;
        int desiredCount = Mathf.Max(1, poolSizeProp.intValue);

        if (example == null)
        {
            return;
        }

        Undo.SetCurrentGroupName("Generate Mag Pool");
        Undo.RegisterFullObjectHierarchyUndo(magPool.gameObject, "Generate Mag Pool");

        Transform root = magPool.transform;
        GameObject exampleInstance = example;

        if (example.transform.parent != root)
        {
            exampleInstance = Object.Instantiate(example, root);
            Undo.RegisterCreatedObjectUndo(exampleInstance, "Generate Mag Pool");
            exampleInstance.name = example.name;
        }

        Transform[] existingChildren = new Transform[root.childCount];
        for (int i = 0; i < root.childCount; i++)
        {
            existingChildren[i] = root.GetChild(i);
        }

        for (int i = 0; i < existingChildren.Length; i++)
        {
            if (existingChildren[i] != exampleInstance.transform)
            {
                Undo.DestroyObjectImmediate(existingChildren[i].gameObject);
            }
        }

        exampleInstance.transform.SetParent(root, false);
        exampleInstance.transform.SetAsFirstSibling();
        exampleInstance.name = example.name;
        exampleInstance.SetActive(true);
        SetPickupChildEnabled(exampleInstance, false);

        while (root.childCount < desiredCount)
        {
            GameObject clone = Object.Instantiate(exampleInstance, root);
            Undo.RegisterCreatedObjectUndo(clone, "Generate Mag Pool");
            clone.name = example.name + " (" + root.childCount + ")";
            clone.SetActive(true);
            SetPickupChildEnabled(clone, false);
        }

        EditorUtility.SetDirty(magPool);
        EditorSceneManager.MarkSceneDirty(magPool.gameObject.scene);
    }

    private void SetPickupChildEnabled(GameObject magObject, bool enabled)
    {
        if (magObject == null)
        {
            return;
        }

        UCS_MagPickup pickup = magObject.GetComponentInChildren<UCS_MagPickup>(true);
        if (pickup != null)
        {
            pickup.gameObject.SetActive(enabled);
        }
    }
}
#endif
