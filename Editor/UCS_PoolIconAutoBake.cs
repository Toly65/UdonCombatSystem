using UnityEditor;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

// Bakes an icon for every pool in the open scenes that has none, just before entering play mode or a VRChat build, so a
// forgotten icon never ships as a blank hotbar tile. Pools that already have an icon are left alone, so a hand-made
// icon is never overwritten. Prefab assets that aren't in an open scene still need the Bake Icon button by hand.
[InitializeOnLoad]
public class UCS_PoolIconAutoBake : IVRCSDKBuildRequestedCallback
{
    public int callbackOrder => -100;

    static UCS_PoolIconAutoBake()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) BakeMissing();
        };
    }

    public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
    {
        BakeMissing();
        return true;
    }

    public static int BakeMissing()
    {
        int baked = 0;
        foreach (BulkPreOrderPool pool in Object.FindObjectsOfType<BulkPreOrderPool>(true))
        {
            if (pool.GetItemIcon() != null || pool.GetObjectCount() == 0 || pool.GetObjectAt(0) == null) continue;
            if (UCS_PoolIconBaker.Bake(pool, 512) != null) baked++;
        }

        if (baked > 0) Debug.Log("[UCS_PoolIconAutoBake] Baked " + baked + " missing pool icon(s).");
        return baked;
    }
}
