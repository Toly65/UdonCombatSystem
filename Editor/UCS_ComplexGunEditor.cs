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
    private SerializedProperty sliderHandlerProp;
    private SerializedProperty magSocketProp;
    private SerializedProperty requiredMagPoolProp;
    private SerializedProperty magBeltProp;
    private SerializedProperty desktopSlideAnimDurationProp;
    private SerializedProperty desktopMagEjectDurationProp;
    private SerializedProperty magazineVisualRootProp;
    private SerializedProperty magazinePickupAnchorProp;

    void OnEnable()
    {
        baseEditor = CreateEditor(target, typeof(UCS_BaseGunEditor));
        slidePhysBoneProp = serializedObject.FindProperty("slidePhysBone");
        gunPickupProp = serializedObject.FindProperty("gunPickup");
        desktopReloadCompatibilityProp = serializedObject.FindProperty("desktopReloadCompatibility");
        desktopReloadKeyProp = serializedObject.FindProperty("desktopReloadKey");
        sliderHandlerProp = serializedObject.FindProperty("sliderHandler");
        magSocketProp = serializedObject.FindProperty("magSocket");
        requiredMagPoolProp = serializedObject.FindProperty("requiredMagPool");
        magBeltProp = serializedObject.FindProperty("magBelt");
        desktopSlideAnimDurationProp = serializedObject.FindProperty("desktopSlideAnimDuration");
        desktopMagEjectDurationProp = serializedObject.FindProperty("desktopMagEjectDuration");
        magazineVisualRootProp = serializedObject.FindProperty("magazineVisualRoot");
        magazinePickupAnchorProp = serializedObject.FindProperty("magazinePickupAnchor");
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
        EditorGUILayout.PropertyField(sliderHandlerProp, new GUIContent("Slider Handler", "Controls the local slide animation when the slide is simulated during reload handling."));
        EditorGUILayout.PropertyField(requiredMagPoolProp, new GUIContent("Required Mag Pool"));
        EditorGUILayout.PropertyField(desktopSlideAnimDurationProp, new GUIContent("Desktop Slide Anim Duration", "How long the local slide animation takes during the desktop reload path."));
        EditorGUILayout.PropertyField(desktopMagEjectDurationProp, new GUIContent("Desktop Mag Eject Duration", "Delay used before the desktop reload path ejects the magazine."));
        EditorGUILayout.PropertyField(magazineVisualRootProp, new GUIContent("Magazine Visual Root", "Optional visual root for the inserted magazine that can be toggled independently of the socket state."));
        EditorGUILayout.PropertyField(magazinePickupAnchorProp, new GUIContent("Magazine Pickup Anchor", "Transform used to position the magazine pickup while socketed (where players will grab it)."));
        serializedObject.ApplyModifiedProperties();
    }
}
#endif
