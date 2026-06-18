
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

// Detachable box-magazine feed model. Reloading refills/swaps the whole magazine at once.
// Sits between the feed-agnostic firing core (UCS_BaseGun) and the concrete mag-fed guns
// (UCS_ArcadeGun, UCS_ComplexGun). Tube-fed / revolver / break-action guns extend
// UCS_BaseGun directly with their own feed model instead.
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_MagFedGun : UCS_BaseGun
{
    [SerializeField] private int MagazineSize = 30;
    [SerializeField] private bool refillAmmoOnDisable = true;
    [SerializeField] private bool bulletInChamberAddsToMag = true;

    // Total rounds a full magazine holds, including the chambered round when configured.
    protected int GetMaxAmmo()
    {
        return MagazineSize + (bulletInChamberAddsToMag ? 1 : 0);
    }

    protected override void InitializeAmmoState()
    {
        // pre-fill from magazine capacity, then let the base derive chamber state
        int maxAmmo = GetMaxAmmo();
        if (CurrentAmmo <= 0)
        {
            CurrentAmmo = Mathf.Clamp(maxAmmo, 0, maxAmmo);
        }

        base.InitializeAmmoState();
    }

    public void OnDisable()
    {
        if (refillAmmoOnDisable && !InfiniteMagazine)
        {
            // refill ammo when gun is disabled (respect chamber setting)
            CurrentAmmo = GetMaxAmmo();
            // no need to sync since everyone does this on disable
        }
    }

    protected void InsertMagazine()
    {
        // play mag insert sound
        if (magazineAudioSource != null && maginsertSound != null)
        {
            magazineAudioSource.PlayOneShot(maginsertSound);
        }
    }

    protected void PullMagazine()
    {
        // play mag pull sound
        if (magazineAudioSource != null && magpullSound != null)
        {
            magazineAudioSource.PlayOneShot(magpullSound);
        }
    }

    public override void CompleteReload()
    {
        if (GunAnimator != null)
        {
            GunAnimator.SetBool("IsFiringLock", false);
        }

        // if infinite magazine, nothing to refill; still clear reloading flag and sync
        if (InfiniteMagazine)
        {
            isReloading = false;
            needsReload = false;
            if (!IsPickupOwner())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            RequestSerialization();
            return;
        }

        // handle bullet-in-chamber option properly
        int maxAmmo = GetMaxAmmo();
        CurrentAmmo = Mathf.Clamp(maxAmmo, 0, maxAmmo);
        needsReload = false;

        if (CurrentAmmo > 0)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberLoaded");
        }
        else
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
        }

        bulletInChamberWasFired = false;

        // clear reloading flag and serialize (ensure owner)
        isReloading = false;
        if (!IsPickupOwner())
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        RequestSerialization();
    }

    public override void ReloadGun()
    {
        // play reload sound
        if (magazineAudioSource != null && magpullSound != null)
        {
            PullMagazine();
        }

        // insert magazine and complete reload after the configured delay
        SendCustomEventDelayedSeconds("InsertMagazine", ReloadTime / 2f);
        SendCustomEventDelayedSeconds("CompleteReload", ReloadTime);
    }
}
