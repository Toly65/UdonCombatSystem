
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Dynamics.PhysBone.Components;

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_ComplexGun : UCS_MagFedGun
{
    
    [SerializeField] private AnimationClip reloadAnimation;
    [SerializeField] private VRC_Pickup gunPickup;
    [SerializeField] private bool desktopReloadCompatibility = true;
    [SerializeField] private KeyCode desktopReloadKey = KeyCode.Q;
    private bool desktopReloadBlocked = false;
    [SerializeField] private UCS_MagSocket magSocket;
    [SerializeField] private VRCPhysBone slidePhysBone;
    [SerializeField] private UCS_SliderHandler sliderHandler;
    [SerializeField] private float desktopSlideAnimDuration = 0.12f;
    [SerializeField] private UCS_MagBelt magBelt;
    [SerializeField] private UCS_MagPool requiredMagPool;
    [SerializeField] private float desktopMagEjectDuration = 0.25f;
    [SerializeField] private GameObject magazineVisualRoot;
    [SerializeField] private Transform magazinePickupAnchor;
    
    bool slideLockedBack = false;
    bool slideCycleNeedsChamber = false;

    int ParamIsFiring     = Animator.StringToHash("IsFiring");
    int ParamIsFiringLock = Animator.StringToHash("IsFiringLock");
    int ParamBulletVisible = Animator.StringToHash("BulletVisible");
    int StateFireCycle = Animator.StringToHash("FireCycle");
    int StateFireCycleLock = Animator.StringToHash("FireCycleLock");
    int StateFireIdle = Animator.StringToHash("FireIdle");
    int fireCycleLayerIndex = -2;
    
    bool MagazineInserted = false;
    [UdonSynced] private bool syncedMagazineVisualVisible;
    [UdonSynced] private int syncedMagazineId = -1;
    private int lastLoggedSyncedMagazineId = int.MinValue;
    private int lastLoggedGunOwnerId = int.MinValue;

    bool slideBeingHeld=false; //we prevent the gun from firing if the slide is being held back by the player

    bool playerPulledSlideBack = false; //we use this to determine whether to play the slide back sound when the slide is pulled back, as opposed to being pulled back by recoil

    protected override VRC_Pickup GetOwnershipPickup()
    {
        return gunPickup;
    }

    protected override void Start()
    {
        base.Start();
        if (gunPickup == null)
        {
            gunPickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));
        }

        // Spawn this gun empty so every client starts from the same unloaded state.
        CurrentAmmo = 0;
        bulletChambered = false;
        bulletInChamberWasFired = false;
        needsReload = true;
        UpdateBulletVisibility(false);
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }

        // Preserve the socket's preloaded magazine state when the socket initializes
        // before the gun; otherwise the first rack can fail to arm the chamber.
        if (magSocket == null || !magSocket.HasCurrentMag())
        {
            SetMagazineInserted(false);
            SetMagazineVisualVisible(false);
        }
    }

    private void Update()
    {
        if (!desktopReloadCompatibility)
        {
            return;
        }

        GetPickupOwner();
        if (Networking.LocalPlayer == null || Networking.LocalPlayer.IsUserInVR() || gunPickup == null || !gunPickup.IsHeld || cachedPickupOwner != Networking.LocalPlayer)
        {
            return;
        }

        if (Input.GetKeyDown(desktopReloadKey))
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(PerformDesktopReload));
        }
    }

    public void PerformDesktopReload()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        if (desktopReloadBlocked)
        {
            return;
        }

        desktopReloadBlocked = true;

        // begin reload state and play animation
        StartReload();
        RequestSerialization();

        // schedule magazine swap and slide finish across the reload duration
        float swapTime = ReloadTime / 2f;
        SendCustomEventDelayedSeconds("DesktopPerformMagSwap", swapTime);
        SendCustomEventDelayedSeconds("DesktopFinishSlideRack", ReloadTime);
    }

    public void DesktopPerformMagSwap()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        if (magSocket != null)
        {
            magSocket.ReloadMagDelayed(desktopMagEjectDuration);
        }
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkedDesktopSlideBack));
    }

    public void NetworkedDesktopSlideBack()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SimulatePullBack(desktopSlideAnimDuration);
        }
        if (firemechanicsAudioSource != null && slideBackSound != null)
        {
            firemechanicsAudioSource.PlayOneShot(slideBackSound);
        }
    }

    public void DesktopFinishSlideRack()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        if (bulletChambered)
        {
            OnEjectionThresholdCrossed();
        }

        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkedDesktopSlideForward));

        // maintain previous networked behavior as well
        OnSlidePulledBack();
        if (slideLockedBack)
        {
            OnSlideReleasePressed();
        }
        else
        {
            OnSlideForward();
        }
        OnSlideInsertionThesholdCrossed();

        CompleteReload();

        desktopReloadBlocked = false;
    }

    public void NetworkedDesktopSlideForward()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SimulateRelease(desktopSlideAnimDuration);
        }
        if (firemechanicsAudioSource != null && slideForwardSound != null)
        {
            firemechanicsAudioSource.PlayOneShot(slideForwardSound);
        }
    }

    public override void Pickup()
    {
        // Ensure the local picker becomes the network owner before any mag/belt logic runs.
        base.Pickup();

        if (sliderHandler != null)
        {
            if (Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR())
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SetOwnerVrMode));
            }
            else
            {
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(SetOwnerDesktopMode));
            }
        }

        bool useMagBelt = Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR() && magBelt != null;

        if (useMagBelt)
        {
            string typeId = "";
            if (requiredMagPool != null)
            {
                typeId = requiredMagPool.GetMagTypeId();
            }
            magBelt.SetRequestedMagSource(typeId, requiredMagPool);
        }
        else if (magBelt != null)
        {
            magBelt.ClearRequestedMag();
        }

        if (magSocket != null)
        {
            magSocket.TryReattachSocketedMag();
            magSocket.RefreshSocketedMagPickupState();
            magSocket.SetSocketedMagGunHeld(true);
        }

        // Ownership handoff can finalize one frame later; re-run once to avoid non-master pickup races.
        SendCustomEventDelayedFrames(nameof(ResolvePostPickupState), 1);
    }

    public void ResolvePostPickupState()
    {
        if (gunPickup == null || !gunPickup.IsHeld)
        {
            return;
        }

        base.Pickup();

        bool useMagBelt = Networking.LocalPlayer != null && Networking.LocalPlayer.IsUserInVR() && magBelt != null;
        if (useMagBelt)
        {
            string typeId = "";
            if (requiredMagPool != null)
            {
                typeId = requiredMagPool.GetMagTypeId();
            }
            magBelt.SetRequestedMagSource(typeId, requiredMagPool);
        }
        else if (magBelt != null)
        {
            magBelt.ClearRequestedMag();
        }

        if (magSocket != null)
        {
            magSocket.RefreshSocketedMagFromGunState();
            magSocket.TryReattachSocketedMag();
            magSocket.RefreshSocketedMagPickupState();
            magSocket.SetSocketedMagGunHeld(true);
        }
    }

    private void ApplyRequestedMagFromThisGun()
    {
        if (magBelt == null)
        {
            return;
        }

        string typeId = "";
        if (requiredMagPool != null)
        {
            typeId = requiredMagPool.GetMagTypeId();
        }

        magBelt.SetRequestedMagSource(typeId, requiredMagPool);
    }

    private UCS_ComplexGun ResolveGunFromPickup(VRC_Pickup pickup)
    {
        if (pickup == null)
        {
            return null;
        }

        UCS_PickupEventTransferer transferer = pickup.gameObject.GetComponent<UCS_PickupEventTransferer>();
        if (transferer != null && transferer.targetBehaviour != null)
        {
            UCS_ComplexGun transferedGun = transferer.targetBehaviour.gameObject.GetComponent<UCS_ComplexGun>();
            if (transferedGun != null)
            {
                return transferedGun;
            }
        }

        return pickup.GetComponentInChildren<UCS_ComplexGun>();
    }

    private bool TrySetRequestedMagFromHeldGun(VRC_Pickup.PickupHand hand)
    {
        if (Networking.LocalPlayer == null || magBelt == null || hand == VRC_Pickup.PickupHand.None)
        {
            return false;
        }

        VRC_Pickup heldPickup = Networking.LocalPlayer.GetPickupInHand(hand);
        UCS_ComplexGun heldGun = ResolveGunFromPickup(heldPickup);
        if (heldGun == null || heldGun == this || heldGun.magBelt != magBelt)
        {
            return false;
        }

        heldGun.ApplyRequestedMagFromThisGun();
        return true;
    }

    private bool TrySetRequestedMagFromOtherHeldGun()
    {
        if (gunPickup == null)
        {
            return false;
        }

        if (gunPickup.currentHand == VRC_Pickup.PickupHand.Left)
        {
            return TrySetRequestedMagFromHeldGun(VRC_Pickup.PickupHand.Right);
        }

        if (gunPickup.currentHand == VRC_Pickup.PickupHand.Right)
        {
            return TrySetRequestedMagFromHeldGun(VRC_Pickup.PickupHand.Left);
        }

        if (TrySetRequestedMagFromHeldGun(VRC_Pickup.PickupHand.Left))
        {
            return true;
        }

        return TrySetRequestedMagFromHeldGun(VRC_Pickup.PickupHand.Right);
    }



    public override void Drop()
    {
        GetPickupOwner();
        bool useMagBelt = cachedPickupOwner != null && cachedPickupOwner.IsUserInVR() && magBelt != null;

        if (useMagBelt)
        {
            if (!TrySetRequestedMagFromOtherHeldGun())
            {
                magBelt.ClearRequestedMag();
            }
        }

        if (magSocket != null)
        {
            magSocket.RefreshSocketedMagPickupState();
            magSocket.SetSocketedMagGunHeld(false);
        }

        if (sliderHandler != null)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NotifySliderGunDropped));
        }

        ClearCachedPickupOwner();
    }

    public void SetOwnerDesktopMode()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SetOwnerDesktopMode();
        }
    }

    public void SetOwnerVrMode()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SetOwnerVrMode();
        }
    }

    public void NotifySliderGunDropped()
    {
        if (sliderHandler != null)
        {
            sliderHandler.OnGunDropped();
        }
    }

    public string GetAcceptedMagTypeId()
    {
        if (requiredMagPool != null)
        {
            return requiredMagPool.GetMagTypeId();
        }
        return "";
    }

    public UCS_MagPool GetRequiredMagPool()
    {
        return requiredMagPool;
    }

    public Transform GetMagazinePickupAnchor()
    {
        return magazinePickupAnchor;
    }

    

    public void SetMagazineInserted(bool inserted)
    {
        MagazineInserted = inserted;
        if (!inserted)
        {
            SetMagazineVisualVisible(false);
        }
    }

    public int GetInsertedMagId()
    {
        return syncedMagazineId;
    }

    public void SetInsertedMagId(int magId)
    {
        syncedMagazineId = magId;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public void SetMagazineVisualVisible(bool visible)
    {
        syncedMagazineVisualVisible = visible;
        if (magazineVisualRoot != null)
        {
            magazineVisualRoot.SetActive(visible);
        }
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public override void OnDeserialization()
    {
        int ownerId = -1;
        var owner = Networking.GetOwner(gameObject);
        if (owner != null) ownerId = owner.playerId;
        if (syncedMagazineId != lastLoggedSyncedMagazineId || ownerId != lastLoggedGunOwnerId)
        {
            lastLoggedSyncedMagazineId = syncedMagazineId;
            lastLoggedGunOwnerId = ownerId;
            Debug.Log($"[UCS_ComplexGun] OnDeserialization gun={gameObject.name} syncedMagazineId={syncedMagazineId} owner={ownerId}");
        }
        if (magazineVisualRoot != null)
        {
            magazineVisualRoot.SetActive(syncedMagazineVisualVisible);
        }
        if (magSocket != null)
        {
            magSocket.RefreshSocketedMagFromGunState();
        }
    }

    // Called when a magazine is inserted into the gun socket so the gun can adopt its ammo count.
    public void OnMagazineInserted(UCS_Mag mag)
    {
        if (mag == null)
        {
            return;
        }

        CurrentAmmo = mag.GetCurrentAmmo();
        slideCycleNeedsChamber = CurrentAmmo > 0;

        // In the complex gun, inserting a magazine should not chamber a round yet.
        // The next rear-to-forward slide cycle is what loads the chamber.
        bulletChambered = false;
        bulletInChamberWasFired = false;
        UpdateBulletVisibility(false);

        // play mag-insert audio
        InsertMagazine();

        // Physical mag insertion completes any pending auto-reload so the gun can fire.
        isReloading = false;
        needsReload = CurrentAmmo <= 0;
        RequestSerialization();
    }

    // Called by external controllers (e.g. UCS_MagSocket) when the mag is being ejected
    public void NotifyMagazineEjected()
    {
        PullMagazine();
    }

    public override void CompleteReload()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        // Complex gun ammo is managed by the physical mag, not the base reload flow.
        // Just clear the reload state so the gun can fire; do not touch CurrentAmmo.
        if (GunAnimator != null)
        {
            GunAnimator.SetBool(ParamIsFiringLock, false);
        }
        isReloading = false;
        needsReload = false;
        RequestSerialization();
    }

    // Transfer current gun ammo into the ejected mag and zero the gun's internal magazine.
    public void TransferAmmoToMag(UCS_Mag mag)
    {
        if (mag == null)
        {
            return;
        }

        // Own both the mag data object and its pickup child (which has the VRCObjectSync
        // driving position) so physics simulation transfers to the local player on ejection.
        Networking.SetOwner(Networking.LocalPlayer, mag.gameObject);
        Transform ejectedPickupRoot = mag.GetPickupRootTransform();
        if (ejectedPickupRoot != null && ejectedPickupRoot.gameObject != mag.gameObject)
        {
            Networking.SetOwner(Networking.LocalPlayer, ejectedPickupRoot.gameObject);
        }
        mag.SetCurrentAmmo(CurrentAmmo);
        CurrentAmmo = 0;
        RequestSerialization();
    }

    public override void ReloadGun()
    {
        if (GunAnimator != null && reloadAnimation != null)
        {
            GunAnimator.Play(reloadAnimation.name);
        }
        // Manual reload completion is expected to be triggered by the interaction flow.
    }
    public override void TriggerPull()
    {
        if (slidePhysBone != null)
        {
            slideBeingHeld = slidePhysBone.IsGrabbed;
        }
        if (slideLockedBack || slideBeingHeld)
        {
            //if the slide is locked back, or held back by the player, we don't allow firing
            return;
        }
        base.TriggerPull();
    }
    public void OnEjectionThresholdCrossed()
    {
        if (!bulletChambered)
        {
            return;
        }

        if (bulletInChamberWasFired)
        {
            if (BulletEjectParticleSystem != null)
            {
                BulletEjectParticleSystem.Play();
            }
        }
        else if (UnfiredBulletEjectParticleSystem != null)
        {
            UnfiredBulletEjectParticleSystem.Play();
        }

        if (IsPickupOwner())
        {
            bulletChambered = false;
            bulletInChamberWasFired = false;
            needsReload = CurrentAmmo <= 0;
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
            RequestSerialization();
        }

        // Chambering is handled when the slide returns forward.
        slideCycleNeedsChamber = true;
    }
    
    public void OnSlidePulledBack()
    {
        //called when the slide is fully rearward, either by the player or by recoil
        //if they player grabbed the slide to get it to this state we play the slide back sound
        if (slidePhysBone != null)
        {
            slideBeingHeld = slidePhysBone.IsGrabbed;
        }
        if (slideBeingHeld)
        {
            firemechanicsAudioSource.PlayOneShot(slideBackSound);
        }
        playerPulledSlideBack = slideBeingHeld;

        //locking
        if(CurrentAmmo <= 0 && !slideBeingHeld)
        {
            //this is the automatic slide open when ammo runs out, so we lock the slide back until the player releases it
            slideLockedBack = true;
            if (sliderHandler != null) sliderHandler.LockSlideBack();
        }

        //unlocking
        if(slideBeingHeld && slideLockedBack)
        {
            //if the player pulled the slide back while it was locked, we unlock it immediately so that it can spring forward as soon as they let go
            slideLockedBack = false;
            if (GunAnimator != null)
            {
                GunAnimator.SetBool(ParamIsFiringLock, false);
            }
            if (sliderHandler != null) sliderHandler.ReleaseSlide();
        }

        slideCycleNeedsChamber = true;
    }
    public void OnSlideForward()
    {
        if(playerPulledSlideBack)
        {
            firemechanicsAudioSource.PlayOneShot(slideForwardSound);
        }
        playerPulledSlideBack = false;

    }

    public void OnSlideInsertionThesholdCrossed()
    {
        //called when the slide springs forward after being pulled back, or when the player pushes it forward
        if (slideCycleNeedsChamber && MagazineInserted && !bulletChambered)
        {
            //if there is a magazine and the chamber is empty, chamber a round
            ConsumeMagazineRound();
        }

        slideCycleNeedsChamber = false;
    }

    protected override void ConsumeFiredRound()
    {
        base.ConsumeFiredRound();
        bool isLastRound = CurrentAmmo <= 0;
        if (GunAnimator != null)
        {
            GunAnimator.SetBool(ParamIsFiringLock, isLastRound);
            GunAnimator.SetBool(ParamIsFiring, !isLastRound);

            // Fallback for controllers that are missing FireCycleLayer transitions.
            if (fireCycleLayerIndex == -2)
            {
                fireCycleLayerIndex = GunAnimator.GetLayerIndex("FireCycleLayer");
            }

            if (fireCycleLayerIndex >= 0)
            {
                int stateHash = isLastRound ? StateFireCycleLock : StateFireCycle;
                if (GunAnimator.HasState(fireCycleLayerIndex, stateHash))
                {
                    GunAnimator.Play(stateHash, fireCycleLayerIndex, 0f);
                }
            }
        }
        SendCustomEventDelayedSeconds(nameof(EndFireCycle), CycleTime);
        if (isLastRound)
            SendCustomEventDelayedSeconds(nameof(LockSlideAfterCycle), CycleTime);
    }

    public void EndFireCycle()
    {
        if (GunAnimator != null)
        {
            GunAnimator.SetBool(ParamIsFiring, false);

            if (fireCycleLayerIndex == -2)
            {
                fireCycleLayerIndex = GunAnimator.GetLayerIndex("FireCycleLayer");
            }

            if (fireCycleLayerIndex >= 0 && GunAnimator.HasState(fireCycleLayerIndex, StateFireIdle))
            {
                GunAnimator.Play(StateFireIdle, fireCycleLayerIndex, 0f);
            }
        }
    }

    public void LockSlideAfterCycle()
    {
        slideLockedBack = true;
        if (sliderHandler != null) sliderHandler.LockSlideBack();
    }

    public void OnSlideReleasePressed()
    {
        //called when the player presses the button to release the slide, allowing it to spring forward if it's locked back
        if (slideLockedBack)
        {
            slideLockedBack = false;
            if (GunAnimator != null)
            {
                GunAnimator.SetBool(ParamIsFiringLock, false);
            }
            if (sliderHandler != null) sliderHandler.ReleaseSlide();
            OnSlideForward();
        }
    }
   public void DumpMagazine()
   {
        //TODO: inform the magazine of remaining ammo when we have the script for it


        //internal state reset
       SetMagazineInserted(false);
        //eject all rounds from the internal ammo
        CurrentAmmo = 0;
       SetMagazineVisualVisible(false);
   }

    protected override void UpdateBulletVisibility(bool visible)
    {
        if (GunAnimator != null)
        {
            GunAnimator.SetBool(ParamBulletVisible, visible);
        }
    }
}
