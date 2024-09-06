using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;


#if !COMPILER_UDONSHARP && UNITY_EDITOR
using UdonSharpEditor;
#endif
public enum GunType
{
    Projectile,
    Raycast,
    RaycastWithDrop
}

[CustomEditor(typeof(Gun))]
public class GunEditor : Editor
{

    //bools for foldouts for each section
    bool generalSettingsFoldout = true;
    bool reloadSettings = true;
    bool audioSettingsFoldout = true;
    bool hapticSettings = true;
    bool animationSettings;
    bool particleSettings;
    //gun type enum
    GunType gunType;

    //serialized properties
    SerializedProperty gunTypeProperty;
    SerializedProperty bulletProperty;
    SerializedProperty bulletSpreadProperty;
    SerializedProperty bulletVelocityProperty;
    SerializedProperty bulletDropProperty;
    SerializedProperty bulletDropMultiplierProperty;


    private void OnEnable()
    {

    }
    public override void OnInspectorGUI()
    {
        if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

        //default inspector
        DrawDefaultInspector();
    }
}