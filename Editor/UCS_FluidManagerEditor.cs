using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

#if UNITY_EDITOR
[CustomEditor(typeof(UCS_FluidManager), true)]
public class UCS_FluidManagerEditor : Editor
{
    private SerializedProperty fluidPropertiesProp;
    private SerializedProperty fluidPoolParentProp;
    private SerializedProperty poolsProp;
    private SerializedProperty poolExampleProp;
    private SerializedProperty poolSizeProp;
    private SerializedProperty depositMergeRadiusProp;
    private SerializedProperty networkTickRateProp;
    private SerializedProperty shadowPulseSecProp;

    private SerializedProperty poolFireNodeManagerProp;
    private SerializedProperty poolMaxVolumeProp;
    private SerializedProperty poolIgniteVolumeThresholdProp;
    private SerializedProperty poolBurnRateProp;
    private SerializedProperty poolAllowIgnitionProp;

    private void OnEnable()
    {
        fluidPropertiesProp = serializedObject.FindProperty("fluidProperties");
        fluidPoolParentProp = serializedObject.FindProperty("fluidPoolParent");
        poolsProp = serializedObject.FindProperty("_pools");
        poolExampleProp = serializedObject.FindProperty("poolExample");
        poolSizeProp = serializedObject.FindProperty("poolSize");
        depositMergeRadiusProp = serializedObject.FindProperty("depositMergeRadius");
        networkTickRateProp = serializedObject.FindProperty("networkTickRate");
        shadowPulseSecProp = serializedObject.FindProperty("shadowPulseSec");

        poolFireNodeManagerProp = serializedObject.FindProperty("poolFireNodeManager");
        poolMaxVolumeProp = serializedObject.FindProperty("poolMaxVolume");
        poolIgniteVolumeThresholdProp = serializedObject.FindProperty("poolIgniteVolumeThreshold");
        poolBurnRateProp = serializedObject.FindProperty("poolBurnRate");
        poolAllowIgnitionProp = serializedObject.FindProperty("poolAllowIgnition");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.LabelField("Fluid Manager", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fluidPropertiesProp);
        EditorGUILayout.PropertyField(fluidPoolParentProp, new GUIContent("Pool Parent", "Root transform used to discover existing UCS_FluidPool children."));
        EditorGUILayout.PropertyField(poolsProp, new GUIContent("Pools"), true);
        EditorGUILayout.PropertyField(poolExampleProp, new GUIContent("Example GameObject", "Prefab instance used as the template when generating the fluid pool."));
        EditorGUILayout.PropertyField(poolSizeProp, new GUIContent("Pool Size"));
        EditorGUILayout.PropertyField(depositMergeRadiusProp);
        EditorGUILayout.PropertyField(networkTickRateProp);
        EditorGUILayout.PropertyField(shadowPulseSecProp);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pool Defaults", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(poolFireNodeManagerProp, new GUIContent("Fire Node Manager", "Shared fire node manager reference applied to every child fluid pool."));
        EditorGUILayout.PropertyField(poolMaxVolumeProp, new GUIContent("Max Volume"));
        EditorGUILayout.PropertyField(poolIgniteVolumeThresholdProp, new GUIContent("Ignite Volume Threshold"));
        EditorGUILayout.PropertyField(poolBurnRateProp, new GUIContent("Burn Rate"));
        EditorGUILayout.PropertyField(poolAllowIgnitionProp, new GUIContent("Allow Ignition"));

        UCS_FluidManager fluidManager = (UCS_FluidManager)target;
        UCS_FluidPool[] pools = GetPools(fluidManager);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Pool Members", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Discovered Pools", pools.Length.ToString());

        EditorGUI.BeginDisabledGroup(poolExampleProp.objectReferenceValue == null || poolSizeProp.intValue < 1);
        if (GUILayout.Button("Generate Pool"))
        {
            GeneratePool(fluidManager);
            pools = GetPools(fluidManager);
            ApplyDefaultsToPools(fluidManager, pools);
            PopulateSerializedPools(fluidManager, pools);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUI.BeginDisabledGroup(pools.Length == 0);
        if (GUILayout.Button("Apply Defaults To All Pools"))
        {
            ApplyDefaultsToPools(fluidManager, pools);
        }
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.HelpBox("This inspector edits the shared settings on every discovered UCS_FluidPool under the manager's pool parent. Per-pool visual root references are left untouched.", MessageType.Info);

        serializedObject.ApplyModifiedProperties();
    }

    private void GeneratePool(UCS_FluidManager fluidManager)
    {
        GameObject example = poolExampleProp.objectReferenceValue as GameObject;
        int desiredCount = Mathf.Max(1, poolSizeProp.intValue);

        if (fluidManager == null || example == null)
        {
            return;
        }

        Undo.SetCurrentGroupName("Generate Fluid Pool");
        Undo.RegisterFullObjectHierarchyUndo(fluidManager.gameObject, "Generate Fluid Pool");

        Transform root = fluidManager.fluidPoolParent != null ? fluidManager.fluidPoolParent : fluidManager.transform;
        if (root == null)
        {
            return;
        }

        GameObject exampleInstance = example;

        if (example.transform.parent != root)
        {
            exampleInstance = Object.Instantiate(example, root);
            Undo.RegisterCreatedObjectUndo(exampleInstance, "Generate Fluid Pool");
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

        while (root.childCount < desiredCount)
        {
            GameObject clone = Object.Instantiate(exampleInstance, root);
            Undo.RegisterCreatedObjectUndo(clone, "Generate Fluid Pool");
            clone.name = example.name + " (" + root.childCount + ")";
            clone.SetActive(true);
        }

        EditorUtility.SetDirty(fluidManager);
        if (fluidManager.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(fluidManager.gameObject.scene);
        }
    }

    private UCS_FluidPool[] GetPools(UCS_FluidManager fluidManager)
    {
        Transform root = fluidManager.fluidPoolParent != null ? fluidManager.fluidPoolParent : fluidManager.transform;
        if (root == null)
        {
            return new UCS_FluidPool[0];
        }

        UCS_FluidPool[] pools = root.GetComponentsInChildren<UCS_FluidPool>(true);
        return pools != null ? pools : new UCS_FluidPool[0];
    }

    private void PopulateSerializedPools(UCS_FluidManager fluidManager, UCS_FluidPool[] pools)
    {
        if (fluidManager == null)
        {
            return;
        }

        SerializedObject managerObject = new SerializedObject(fluidManager);
        SerializedProperty serializedPools = managerObject.FindProperty("_pools");
        if (serializedPools == null)
        {
            return;
        }

        int poolCount = pools != null ? pools.Length : 0;
        serializedPools.arraySize = poolCount;
        for (int i = 0; i < poolCount; i++)
        {
            serializedPools.GetArrayElementAtIndex(i).objectReferenceValue = pools[i];
        }

        managerObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(fluidManager);
    }

    private void ApplyDefaultsToPools(UCS_FluidManager fluidManager, UCS_FluidPool[] pools)
    {
        if (pools == null || pools.Length == 0)
        {
            return;
        }

        float maxVolume = poolMaxVolumeProp.floatValue;
        float igniteThreshold = poolIgniteVolumeThresholdProp.floatValue;
        float burnRate = poolBurnRateProp.floatValue;
        bool allowIgnition = poolAllowIgnitionProp.boolValue;
        UCS_FireNodeManager fireNodeManager = poolFireNodeManagerProp.objectReferenceValue as UCS_FireNodeManager;

        Object[] undoTargets = new Object[pools.Length + 1];
        undoTargets[0] = fluidManager;
        for (int i = 0; i < pools.Length; i++)
        {
            undoTargets[i + 1] = pools[i];
        }

        Undo.RecordObjects(undoTargets, "Apply Fluid Pool Defaults");

        for (int i = 0; i < pools.Length; i++)
        {
            UCS_FluidPool pool = pools[i];
            if (pool == null)
            {
                continue;
            }

            pool.fluidManager = fluidManager;
            pool.fireNodeManager = fireNodeManager;
            pool.maxVolume = maxVolume;
            pool.igniteVolumeThreshold = igniteThreshold;
            pool.burnRate = burnRate;
            pool.allowIgnition = allowIgnition;

            EditorUtility.SetDirty(pool);
        }

        EditorUtility.SetDirty(fluidManager);
        if (fluidManager.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(fluidManager.gameObject.scene);
        }
    }
}
#endif