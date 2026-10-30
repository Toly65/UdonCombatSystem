using System.Collections.Generic;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

// Tools > Combat System > Check Setup: lists what is missing or miswired in the open scene for the stow system, the stow
// HUD, pools and stowable items, with a Select button on each and a one-click fix where there is one.
public partial class UCS_SetupValidator : EditorWindow
{
    private enum Level { Error, Warning }

    private class Issue
    {
        public Level level;
        public string message;
        public Object target;
        public string fixLabel;
        public System.Action fix;
        public int extra; // further objects with the same problem, folded into this line
    }

    private readonly List<Issue> issues = new List<Issue>();
    private Vector2 scroll;

    [MenuItem("Tools/Combat System/Check Setup")]
    private static void Open()
    {
        var window = GetWindow<UCS_SetupValidator>("Check Setup");
        window.minSize = new Vector2(420, 240);
        window.Refresh();
    }

    private void OnFocus()
    {
        Refresh();
    }

    private void OnGUI()
    {
        if (GUILayout.Button("Check again", GUILayout.Height(24))) Refresh();

        if (issues.Count == 0)
        {
            EditorGUILayout.HelpBox("No problems found in the open scene.", MessageType.Info);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (Issue issue in issues.ToArray())
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(EditorGUIUtility.IconContent(issue.level == Level.Error ? "console.erroricon.sml" : "console.warnicon.sml"), GUILayout.Width(20));
            EditorGUILayout.LabelField(issue.extra > 0 ? issue.message + " (and " + issue.extra + " more like it)" : issue.message, EditorStyles.wordWrappedLabel);
            using (new EditorGUI.DisabledScope(issue.target == null))
            {
                if (GUILayout.Button("Select", GUILayout.Width(52)))
                {
                    Selection.activeObject = issue.target;
                    EditorGUIUtility.PingObject(issue.target);
                }
            }
            if (issue.fix != null && GUILayout.Button(issue.fixLabel, GUILayout.Width(110)))
            {
                issue.fix();
                Refresh();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }

    private readonly Dictionary<string, Issue> byKey = new Dictionary<string, Issue>();

    // key folds repeats: the same problem on every copy of a pooled gun shows once, with a count
    private void Add(Level level, string message, Object target = null, string fixLabel = null, System.Action fix = null, string key = null)
    {
        if (key != null && byKey.TryGetValue(key, out Issue existing))
        {
            existing.extra++;
            return;
        }

        var issue = new Issue { level = level, message = message, target = target, fixLabel = fixLabel, fix = fix };
        issues.Add(issue);
        if (key != null) byKey[key] = issue;
    }

    private void Refresh()
    {
        issues.Clear();
        byKey.Clear();
        CheckGuns();
        CheckStow();
        CheckHud();
        CheckPools();
        CheckStowables();
        CheckPrograms();
        Repaint();
    }

    private void CheckStow()
    {
        PlayerStowPointManager[] managers = FindObjectsOfType<PlayerStowPointManager>(true);
        if (managers.Length > 1) Add(Level.Warning, managers.Length + " stow managers in the scene; players should have one.", managers[1]);

        foreach (PlayerStowPointManager m in managers)
        {
            if (m.stowPoints == null || m.stowPoints.Length == 0)
            {
                Add(Level.Error, "'" + m.name + "' has no stow points, so nothing can be stowed and the stow HUD will be empty. Use Add Stow Point on it.", m);
                continue;
            }

            int keyCount = m.stowKeys == null ? 0 : m.stowKeys.Length;
            if (keyCount != m.stowPoints.Length)
                Add(Level.Error, "'" + m.name + "' has " + m.stowPoints.Length + " stow points but " + keyCount + " keys. Each slot needs a key.", m);

            for (int i = 0; i < m.stowPoints.Length; i++)
            {
                if (m.stowPoints[i] == null) Add(Level.Error, "'" + m.name + "' slot " + i + " has no stow point assigned.", m);
                else if (i < keyCount && (m.stowKeys[i] < (int)KeyCode.Alpha0 || m.stowKeys[i] > (int)KeyCode.Alpha9))
                    Add(Level.Warning, "'" + m.name + "' slot " + i + " uses a key other than 0-9, which the stow HUD can't label.", m);
            }

            if (FindObjectsOfType<DesktopKeyPoller>(true).Length == 0)
                Add(Level.Warning, "No DesktopKeyPoller in the scene, so desktop players can't use the stow keys.", m);
        }
    }

    private void CheckHud()
    {
        UCS_HealthBarsManager health = FindObjectOfType<UCS_HealthBarsManager>(true);
        PlayerStowPointManager manager = FindObjectOfType<PlayerStowPointManager>(true);
        UCS_StowHud[] huds = FindObjectsOfType<UCS_StowHud>(true);

        if (health != null && manager != null && huds.Length == 0)
        {
            Add(Level.Warning, "There is a stow system but no stow HUD hotbar.", health, "Create HUD", () =>
                EditorApplication.ExecuteMenuItem("Tools/Combat System/Create Stow HUD"));
        }
        else if (huds.Length > 1)
        {
            Add(Level.Error, huds.Length + " stow HUDs in the scene, so hotbars draw on top of each other. Delete the extra ones.", huds[1]);
        }

        foreach (UCS_StowHud hud in huds)
        {
            var so = new SerializedObject(hud);
            if (so.FindProperty("stowManager").objectReferenceValue == null)
                Add(Level.Error, "The stow HUD has no stow manager assigned.", hud);
            else if (manager != null && so.FindProperty("stowManager").objectReferenceValue != manager)
                Add(Level.Warning, "The stow HUD points at a different stow manager than the one in the scene.", hud);
            if (so.FindProperty("columnTemplate").objectReferenceValue == null || so.FindProperty("tileTemplate").objectReferenceValue == null)
                Add(Level.Error, "The stow HUD is missing its column or tile template. Re-run Create Stow HUD.", hud);
        }

        if (health != null && new SerializedObject(health).FindProperty("ammoPanel").objectReferenceValue == null)
            Add(Level.Warning, "The health manager has no Ammo Panel assigned (Create Stow HUD needs it).", health);
    }

    private void CheckPools()
    {
        foreach (BulkPreOrderPool pool in FindObjectsOfType<BulkPreOrderPool>(true))
        {
            var so = new SerializedObject(pool);
            bool hasObjects = pool.GetObjectCount() > 0 && pool.GetObjectAt(0) != null;
            if (so.FindProperty("itemIcon").objectReferenceValue == null)
            {
                BulkPreOrderPool captured = pool;
                Add(Level.Warning, "Pool '" + pool.name + "' has no Item Icon, so its items show a letter on the hotbar and spawn menu.", pool,
                    hasObjects ? "Bake Icon" : null, hasObjects ? (System.Action)(() => UCS_PoolIconBaker.Bake(captured, 512)) : null);
            }
            else if (string.IsNullOrEmpty(so.FindProperty("itemName").stringValue))
            {
                Add(Level.Warning, "Pool '" + pool.name + "' has an icon but no Item Name.", pool);
            }
        }
    }

    private void CheckStowables()
    {
        foreach (StowableManager m in FindObjectsOfType<StowableManager>(true))
        {
            var so = new SerializedObject(m);
            if (so.FindProperty("pickupVisualRoot").objectReferenceValue == null)
                Add(Level.Error, "'" + m.name + "' has no Pickup Visual Root, so it can't hide or show the item.", m);
            if (so.FindProperty("stowablePickup").objectReferenceValue == null)
                Add(Level.Error, "'" + m.name + "' has no Stowable Pickup assigned.", m);
            // a gun's second grip is a pickup too, so count stowable pickups: more than one means several items share a root
            int items = m.GetComponentsInChildren<StowablePickup>(true).Length;
            if (items > 1)
                Add(Level.Warning, "'" + m.name + "' holds " + items + " stowable pickups. A stowable root must belong to one item, or stowing one hides the rest.", m);
        }
    }

    // a UdonSharp script with no program asset (or no Udon behaviour behind it) silently does nothing at runtime
    private void CheckPrograms()
    {
        var reported = new HashSet<System.Type>();
        foreach (UdonSharpBehaviour behaviour in FindObjectsOfType<UdonSharpBehaviour>(true))
        {
            System.Type type = behaviour.GetType();
            if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(behaviour) == null)
            {
                if (reported.Add(type)) Add(Level.Error, type.Name + " has no UdonSharp program asset, so it won't run. Create one (Assets > Create > U# Program Asset) and Compile All.", behaviour);
            }
            else if (UdonSharpEditorUtility.GetBackingUdonBehaviour(behaviour) == null)
            {
                Add(Level.Warning, "'" + behaviour.name + "' (" + type.Name + ") has no Udon behaviour behind it. Select it to repair it.", behaviour);
            }
        }
    }
}
