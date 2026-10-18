
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

    [Tooltip("Automatically lock the slide open when the gun runs out of ammo (bolt-catch / last-round hold-open). Disable for guns like the G3 whose slide closes on empty.")]
    [SerializeField] private bool autoLockSlideOnEmpty = true;
    
    [Tooltip("G3-style: the slide never auto-locks, only the player can latch it, and it unlocks the next time they grab it.")]
    [SerializeField] private bool manualSlideLockOnly = false;

    bool slideLockedBack = false;
    bool slideLockSettled = false; // manual-lock guns: player has let go since latching
    bool slideLockedFromPose = false; // locked back by a physbone pose (holds the slide open even with ammo)
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

    // Desktop reload input used to be polled every frame in Update(); that loop ticked even
    // when no relevant player was holding the gun. It is now a self-rescheduling delayed
    // event that only starts (and keeps running) while the local player is a desktop user
    // holding the gun. desktopReloadPolling guards against duplicate poll loops.
    private bool desktopReloadPolling = false;

    private void StartDesktopReloadPolling()
    {
        if (!desktopReloadCompatibility || desktopReloadPolling)
        {
            return;
        }

        GetPickupOwner();
        if (Networking.LocalPlayer == null || Networking.LocalPlayer.IsUserInVR() || gunPickup == null || !gunPickup.IsHeld || cachedPickupOwner != Networking.LocalPlayer)
        {
            return;
        }

        desktopReloadPolling = true;
        PollDesktopReload();
    }

    public void PollDesktopReload()
    {
        if (!desktopReloadPolling)
        {
            // Stale tick from a loop that was already stopped - drop it.
            return;
        }

        if (!desktopReloadCompatibility)
        {
            desktopReloadPolling = false;
            return;
        }

        GetPickupOwner();
        if (Networking.LocalPlayer == null || Networking.LocalPlayer.IsUserInVR() || gunPickup == null || !gunPickup.IsHeld || cachedPickupOwner != Networking.LocalPlayer)
        {
            // No longer a desktop player holding the gun - stop polling.
            desktopReloadPolling = false;
            return;
        }

        if (Input.GetKeyDown(desktopReloadKey))
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(PerformDesktopReload));
        }

        SendCustomEventDelayedFrames(nameof(PollDesktopReload), 1);
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

        // The desktop reload runs as a staged sequence so it reads like the VR flow:
        // grab the charging handle -> pull the slide back -> lock it open -> swap the
        // mag -> release the lock so the slide springs forward and chambers a round.
        //
        // Phase 1 - grab the charging handle and pull the slide back (on all clients).
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkedDesktopGrabAndPullBack));

        // Phase 2 - lock the slide open once it is fully rear, before the mag is swapped.
        float swapTime = ReloadTime / 2f;
        float lockTime = Mathf.Min(desktopSlideAnimDuration, swapTime);
        SendCustomEventDelayedSeconds(nameof(DesktopLockSlideOpen), lockTime);

        // Phase 3 - swap the mag while the slide is locked open.
        SendCustomEventDelayedSeconds(nameof(DesktopPerformMagSwap), swapTime);

        // Phase 4 - release the lock, spring the slide forward, chamber, complete.
        SendCustomEventDelayedSeconds(nameof(DesktopFinishSlideRack), ReloadTime);
    }

    // Local delayed stub for Phase 2 - re-broadcasts the lock so every client shows the
    // slide latching into the locked (rear) position before the mag is swapped.
    public void DesktopLockSlideOpen()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NetworkedDesktopLockSlideOpen));
    }

    public void NetworkedDesktopLockSlideOpen()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SimulateLockSlideOpen();
        }
    }

    // Phase 1 (all clients): simulate grabbing the charging handle and pulling the slide
    // back, mirroring what a VR player does physically with the physbone.
    public void NetworkedDesktopGrabAndPullBack()
    {
        if (sliderHandler != null)
        {
            sliderHandler.SimulateGrabChargeHandle();
            sliderHandler.SimulatePullBack(desktopSlideAnimDuration);
        }
        if (firemechanicsAudioSource != null && slideBackSound != null)
        {
            firemechanicsAudioSource.PlayOneShot(slideBackSound);
        }
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
            // Release the simulated charging-handle grab, then let the slide spring forward.
            sliderHandler.SimulateReleaseChargeHandle();
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
            magSocket.SetSocketedMagGunHeld(true);
        }

        if (magBelt != null)
        {
            magBelt.RequestSideRefresh();
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
            magSocket.SetSocketedMagGunHeld(true);
        }

        StartDesktopReloadPolling();
    }

    public override void OnOwnershipTransferred(VRCPlayerApi player)
    {
        // Networking.SetOwner is async: Pickup()/ResolvePostPickupState() can run before
        // this client actually owns the gun, so the socketed mag never gets handed off
        // (the mag + socket stay owned by the previous holder and remain ungrabbable).
        // This is the reliable moment ownership lands, so finalize the mag-socket handoff here.
        if (player == null || player != Networking.LocalPlayer)
        {
            return;
        }

        if (magSocket != null)
        {
            // Re-resolves the socketed mag from the gun's syncedMagazineId (if needed),
            // transfers the mag + pickup-root ownership to this player, and re-enables the
            // anchor. SetSocketedMagGunHeld(true) additionally hands the socket GameObject
            // over so its events / child VRCObjectSync operate for the new owner.
            magSocket.RefreshSocketedMagFromGunState();
            bool isHeld = gunPickup != null && gunPickup.IsHeld;
            magSocket.SetSocketedMagGunHeld(isHeld);
        }

        // Ownership is now settled on the local player, so (re)start the desktop reload
        // polling loop if this is a desktop player holding the gun.
        StartDesktopReloadPolling();
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
        // Use Utilities.IsValid instead of a raw null check: if the player holding the gun
        // has disconnected, cachedPickupOwner can be a stale, non-null but invalid
        // VRCPlayerApi reference, and calling IsUserInVR() on it would crash the script.
        if(Utilities.IsValid(cachedPickupOwner))
        {
            bool useMagBelt = cachedPickupOwner.IsUserInVR() && magBelt != null;
            if (useMagBelt)
            {
                if (!TrySetRequestedMagFromOtherHeldGun())
                {
                    magBelt.ClearRequestedMag();
                }
            }
        }
       

        if (magSocket != null)
        {
            magSocket.SetSocketedMagGunHeld(false);
        }

        if (magBelt != null)
        {
            magBelt.RequestSideRefresh();
        }

        if (sliderHandler != null)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, nameof(NotifySliderGunDropped));
        }

        // Stop the desktop reload polling loop now that the gun is no longer held.
        desktopReloadPolling = false;
        // If the player dropped the gun mid-reload, DesktopFinishSlideRack would have
        // returned early on !IsPickupOwner() and never cleared this - reset it so the
        // next pickup isn't stuck with a blocked reload.
        desktopReloadBlocked = false;
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
        if(magId == syncedMagazineId)
        {
            return;
        }
        syncedMagazineId = magId;
        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    public void SetMagazineVisualVisible(bool visible)
    {
        if (visible == syncedMagazineVisualVisible)
        {
            return;
        }
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
            //Debug.Log($"[UCS_ComplexGun] OnDeserialization gun={gameObject.name} syncedMagazineId={syncedMagazineId} owner={ownerId}");
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
        //Debug.Log($"[UCS auto] ComplexGun.TriggerPull name={gameObject.name} slideLockedBack={slideLockedBack} slideBeingHeld={slideBeingHeld}");
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
            if (!bulletInChamberWasFired)
            {
                // live round hits the dirt - it leaves the gun's total count with it
                CurrentAmmo = Mathf.Max(CurrentAmmo - 1, 0);
            }
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
        if(autoLockSlideOnEmpty && CurrentAmmo <= 0 && !slideBeingHeld && !manualSlideLockOnly)
        {
            //this is the automatic slide open when ammo runs out, so we lock the slide back until the player releases it
            //(disabled for guns like the G3 that don't hold open on empty)
            slideLockedBack = true;
            slideLockSettled = false;
            slideLockedFromPose = false;
            if (sliderHandler != null) sliderHandler.LockSlideBack();
        }

        //unlocking
        if(slideBeingHeld && slideLockedBack && !slideLockedFromPose && !manualSlideLockOnly)
        {
            //if the player pulled the slide back while it was locked, we unlock it immediately so that it can spring forward as soon as they let go
            //(pose-locks are only cleared via the slide release button, not by racking)
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
        // Guns without a slide hold-open (e.g. G3) don't lock on the final shot.
        bool locksOnEmpty = isLastRound && autoLockSlideOnEmpty;
        if (GunAnimator != null)
        {
            // IsFiringLock is a held state (the slide stays locked); IsFiring is a one-shot.
            if (locksOnEmpty) GunAnimator.SetBool(ParamIsFiringLock, true);
            else GunAnimator.SetTrigger(ParamIsFiring);

            // Fallback for controllers that are missing FireCycleLayer transitions.
            if (fireCycleLayerIndex == -2)
            {
                fireCycleLayerIndex = GunAnimator.GetLayerIndex("FireCycleLayer");
            }

            if (fireCycleLayerIndex >= 0)
            {
                int stateHash = locksOnEmpty ? StateFireCycleLock : StateFireCycle;
                if (GunAnimator.HasState(fireCycleLayerIndex, stateHash))
                {
                    GunAnimator.Play(stateHash, fireCycleLayerIndex, 0f);
                }
            }
        }
        SendCustomEventDelayedSeconds(nameof(EndFireCycle), CycleTime);
        if (locksOnEmpty)
            SendCustomEventDelayedSeconds(nameof(LockSlideAfterCycle), CycleTime);
    }

    public void EndFireCycle()
    {
        if (GunAnimator != null)
        {
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

    // Called by whatever the player uses to latch the slide/handle into its notch.
    public void LockSlideManually()
    {
        if (!slideLockedBack)
        {
            LockSlideAfterCycle();
        }
    }

    // Manual-lock guns unlock on the next grab, but only once the player has let go
    // of the latched slide - otherwise the grab that latched it would undo it.
    public void OnSlideGrabbed()
    {
        if (manualSlideLockOnly && slideLockedBack && slideLockSettled)
        {
            OnSlideReleasePressed();
        }
    }

    public void OnSlideReleased()
    {
        if (slideLockedBack)
        {
            slideLockSettled = true;
        }
    }

    public void LockSlideAfterCycle()
    {
        slideLockedBack = true;
        // if nobody is holding the slide, the latch is settled already - otherwise the
        // grab that latched it would immediately undo it, so wait for the release.
        slideLockSettled = slidePhysBone == null || !slidePhysBone.IsGrabbed;
        if (sliderHandler != null) sliderHandler.LockSlideBack();
    }

    // Called by UCS_SliderHandler when a physbone pose locks/unlocks the slide.
    // This lets the gun detect (and block firing for) a slide that is held open
    // by a pose even when ammo is still present. Releasing only clears the lock
    // if it actually came from a pose (auto-open / last-round locks are handled
    // by their own release paths).
    public void SetSlideLockedFromPose(bool locked)
    {
        if (locked)
        {
            slideLockedBack = true;
            slideLockedFromPose = true;
        }
        else if (slideLockedFromPose)
        {
            slideLockedBack = false;
            slideLockedFromPose = false;
        }
    }

    public void OnSlideReleasePressed()
    {
        //called when the player presses the button to release the slide, allowing it to spring forward if it's locked back
        if (slideLockedBack)
        {
            slideLockedBack = false;
            slideLockSettled = false;
            slideLockedFromPose = false;
            if (GunAnimator != null)
            {
                GunAnimator.SetBool(ParamIsFiringLock, false);
            }
            if (sliderHandler != null) sliderHandler.ReleaseSlide();
            OnSlideForward();
            // The handle may already be home (separate charging handle), so there is no
            // falling stretch left to chamber off. Idempotent - slideCycleNeedsChamber
            // guards the double call on guns whose slide really does travel here.
            OnSlideInsertionThesholdCrossed();
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

    // Called by spawner/stow systems to reset the gun to an empty, unloaded state.
    // This clears any socketed mag (returns it to the pool) and zeros all ammo state
    // so the gun appears freshly spawned with no magazine.
    public void ClearMagazineForSpawner()
    {
        if (magSocket != null)
        {
            magSocket.ClearSocketForSpawner();
        }

        CurrentAmmo = 0;
        bulletChambered = false;
        bulletInChamberWasFired = false;
        needsReload = true;
        slideLockedBack = false;
        slideLockSettled = false;
        slideLockedFromPose = false;
        slideCycleNeedsChamber = false;
        UpdateBulletVisibility(false);
        SetMagazineInserted(false);
        SetMagazineVisualVisible(false);

        if (Networking.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }
}
