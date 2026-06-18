
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_ArcadeGun : UCS_MagFedGun
{
    [SerializeField] private AnimationClip reloadAnimation;

    public override void ReloadGun()
    {
        if (GunAnimator != null && reloadAnimation != null)
        {
            GunAnimator.Play(reloadAnimation.name);
        }

        base.ReloadGun();
    }

}
