using UnityEditor;
using UnityEngine;

// The Item Icon field plus a Bake Icon button that renders the pool's first object via UCS_PoolIconBaker. A property
// drawer instead of a custom editor, so UdonSharp's own inspector header is left alone.
[CustomPropertyDrawer(typeof(UCS_BakeIconButtonAttribute))]
public class UCS_BakeIconButtonDrawer : PropertyDrawer
{
    private const float ButtonHeight = 20f;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing + ButtonHeight;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var field = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.PropertyField(field, property, label);

        var pool = property.serializedObject.targetObject as BulkPreOrderPool;
        var button = new Rect(position.x + EditorGUIUtility.labelWidth, field.yMax + EditorGUIUtility.standardVerticalSpacing,
            position.width - EditorGUIUtility.labelWidth, ButtonHeight);

        // greyed out with nothing to render, or when several pools are selected
        bool canBake = pool != null && !property.serializedObject.isEditingMultipleObjects
            && pool.GetObjectCount() > 0 && pool.GetObjectAt(0) != null;
        using (new EditorGUI.DisabledScope(!canBake))
        {
            var content = new GUIContent(property.objectReferenceValue == null ? "Bake Icon" : "Re-bake Icon",
                canBake ? "Renders the pool's first object to a sprite in Assets/ItemPreviews" : "Add objects to the pool's Managed Objects first");
            if (GUI.Button(button, content))
            {
                UCS_PoolIconBaker.Bake(pool, 512);
            }
        }
    }
}
