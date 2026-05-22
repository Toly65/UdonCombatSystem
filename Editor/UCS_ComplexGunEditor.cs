using UnityEngine;
using UnityEditor;

#if UNITY_EDITOR
[CustomEditor(typeof(UCS_ComplexGun), true)]
public class UCS_ComplexGunEditor : Editor
{
    private Editor baseEditor;
    private SerializedProperty slidePhysBoneProp;
    private SerializedProperty gunPickupProp;
    private SerializedProperty desktopReloadCompatibilityProp;
    private SerializedProperty desktopReloadKeyProp;
    private SerializedProperty magSocketProp;
    private SerializedProperty requiredMagPoolProp;
    private SerializedProperty magBeltProp;
    private SerializedProperty magazinePickupAnchorProp;
    private SerializedProperty prefillFromInventoryProp;

    void OnEnable()
    {
        baseEditor = CreateEditor(target, typeof(UCS_BaseGunEditor));
        slidePhysBoneProp = serializedObject.FindProperty("slidePhysBone");
        gunPickupProp = serializedObject.FindProperty("gunPickup");
        magSocketProp = serializedObject.FindProperty("magSocket");
        requiredMagPoolProp = serializedObject.FindProperty("requiredMagPool");
        magBeltProp = serializedObject.FindProperty("magBelt");
        magazinePickupAnchorProp = serializedObject.FindProperty("magazinePickupAnchor");
        prefillFromInventoryProp = serializedObject.FindProperty("prefillFromInventory");
    }

    void OnDisable()
    {
        if (baseEditor != null)
        {
            DestroyImmediate(baseEditor);
        }
    }

    public override void OnInspectorGUI()
    {
        if (baseEditor != null)
        {
            baseEditor.OnInspectorGUI();
        }

        serializedObject.Update();
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Complex Gun", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(gunPickupProp, new GUIContent("Gun Pickup", "Explicit pickup reference used while the magazine is socketed, so the mag can stay pickupable when the gun is held."));
        EditorGUILayout.PropertyField(desktopReloadCompatibilityProp, new GUIContent("Desktop Reload Compatibility", "Lets desktop players press a key to run the complex reload sequence."));
        EditorGUI.BeginDisabledGroup(desktopReloadCompatibilityProp != null && desktopReloadCompatibilityProp.boolValue == false);
        EditorGUILayout.PropertyField(desktopReloadKeyProp, new GUIContent("Desktop Reload Key", "Key used for the desktop reload compatibility path."));
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.PropertyField(magSocketProp, new GUIContent("Mag Socket", "Socket controller used to refresh the socketed magazine pickup state when the gun is picked up or dropped."));
        EditorGUILayout.PropertyField(magBeltProp, new GUIContent("Mag Belt", "Reference to the player's mag belt used to request magazines at runtime."));
        EditorGUILayout.PropertyField(slidePhysBoneProp, new GUIContent("Slide PhysBone"));
        EditorGUILayout.PropertyField(requiredMagPoolProp, new GUIContent("Required Mag Pool"));
        EditorGUILayout.PropertyField(magazinePickupAnchorProp, new GUIContent("Magazine Pickup Anchor", "Transform used to position the magazine pickup while socketed (where players will grab it)."));
        EditorGUILayout.PropertyField(prefillFromInventoryProp, new GUIContent("Prefill From Inventory", "If enabled the gun will try to fill the socketed magazine from the assigned MagPool's Ammo Inventory when the gun is enabled. Otherwise it spawns a full mag."));
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
