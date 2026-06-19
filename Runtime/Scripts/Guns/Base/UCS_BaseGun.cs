using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;
using VRC.Udon;


#if UNITY_EDITOR
using UnityEditor;
#endif

public enum FireSelection
{
    Safe,
    Semi,
    Auto,
    Burst
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
public class UCS_BaseGun : UdonSharpBehaviour
{
    [SerializeField] private bool multipleBarrels = false;
    //if multiple barrels is true, we will use all transforms in this array as muzzle points
    [SerializeField] private Transform[] MuzzlePoints;
    //if multiple barrels is false, we will use this single transform as muzzle point
    [SerializeField] private Transform MuzzlePoint;
    [SerializeField] private bool DesktopFaceFiring = true;
    [SerializeField] private bool DesktopFaceRaycastAiming = false;
    [SerializeField] private bool Raycast;
    //raycast settings, will hide if Raycast is false with editor scripts
    [SerializeField] private float Range = 100f;
    [SerializeField] private LayerMask HitLayers;
    //seperate layermask for bulletsparks and hit effects, we fire 2 raycasts.
    [SerializeField] private LayerMask EffectLayers;
    [SerializeField] private float Damage = 25f;
    [SerializeField] private float ImpactForce = 5f;
    [SerializeField] private UCS_HitEffectsPool HitEffectsPool;
    [SerializeField] private UCS_HitEffectsPool BulletSparkPool;
    [SerializeField] private float HitEffectOffset = 0.01f;
    [SerializeField] private float BulletSparkOffset = 0.0f;

    [Header("Pool / Rate Settings")]
    [Tooltip("Minimum pool size for each effect pool")]
    // pool / rate settings moved to UCS_HitEffectsPool


    [Header("Projectile Settings")]
    [SerializeField] private bool UseProjectiles = false;
    // when using projectiles, reference a shared projectile manager (handles pooling)
    [SerializeField] private UCS_ProjectileManager ProjectileManager;
    [SerializeField] private float ProjectileSpeed = 50f;
    [SerializeField] private float ProjectileSpreadAngle = 5f;

    // computed from RoundsPerMinute in Start()
    protected float CycleTime = 0.0f;

    [Header("Gun Settings")]
    [SerializeField] private float RoundsPerMinute = 300f;
    [SerializeField] protected float ReloadTime = 2f;
    [SerializeField] private bool InfiniteAmmo = false;
    // InfiniteMagazine is a feed concept but the firing path gates on it, so it lives here.
    [SerializeField] protected bool InfiniteMagazine = false;
    [SerializeField] private bool AutoReload = true;

    //ToDO replace with range of firemodes
    [SerializeField] private FireSelection FireMode = FireSelection.Semi;
    //hide burst settings if firemode is not burst
    [SerializeField] private int BurstCount = 3;
    [SerializeField] private float BurstDelay = 0.1f;
    [SerializeField] private bool shotgunMode = false;
    //hide shotgun settings if shotgunmode is false
    [SerializeField] private int PelletsPerShot = 8;

    [Header("audio settings")]
    [SerializeField] protected AudioSource barrelAudioSource;
    [SerializeField] protected AudioSource magazineAudioSource;
    [SerializeField] protected AudioSource firemechanicsAudioSource;
    [SerializeField] protected AudioClip FireSound;
    [SerializeField] protected AudioClip EmptyFireSound;
    [SerializeField] protected AudioClip magpullSound;
    [SerializeField] protected AudioClip maginsertSound;
    [SerializeField] protected AudioClip slideBackSound;
    [SerializeField] protected AudioClip slideForwardSound;

    [Header("haptic feedback")]
    public bool hapticFeedback;
    //hide haptic feedback settings if haptic feedback is false
    [SerializeField] private float hapticFeedbackDuration = 0.1f;
    [SerializeField] private float hapticFeedbackAmplitude = 1;
    [SerializeField] private float hapticFeedbackFrequency = 1;
    [SerializeField] private bool hapticFeedbackOnManualReload;

    [Header("animations")]
    public Animator GunAnimator;
    public AnimationClip CycleAnimation;
    
    [Header("particle systems")]
    public ParticleSystem MuzzleFlashParticleSystem;
    public ParticleSystem BulletEjectParticleSystem;
    public ParticleSystem UnfiredBulletEjectParticleSystem;


    //synced variables
    [UdonSynced] protected int CurrentAmmo;
    [UdonSynced] protected bool isReloading = false;


    //both realistic and arcade will use this
    //arcade will do so incase of a early reload
    //realistic will track this in various stages
    [UdonSynced] protected bool bulletChambered = false;
    protected bool bulletInChamberWasFired = false;
    protected bool needsReload = false;
    private float nextFireAllowedTime = 0f;
    private VRCCameraSettings screenCamera;
    protected VRCPlayerApi cachedPickupOwner;

    protected virtual VRC_Pickup GetOwnershipPickup()
    {
        return null;
    }

    protected VRCPlayerApi GetPickupOwner()
    {
        VRC_Pickup ownershipPickup = GetOwnershipPickup();
        if (ownershipPickup != null)
        {
            VRCPlayerApi pickupOwner = Networking.GetOwner(ownershipPickup.gameObject);
            if (pickupOwner != null)
            {
                cachedPickupOwner = pickupOwner;
                return pickupOwner;
            }
        }

        if (cachedPickupOwner != null)
        {
            return cachedPickupOwner;
        }

        return Networking.GetOwner(gameObject);
    }

    protected bool IsPickupOwner()
    {
        return GetPickupOwner() == Networking.LocalPlayer;
    }

    protected void ClearCachedPickupOwner()
    {
        cachedPickupOwner = null;
    }

    // Public accessor for other systems (mag sockets, belt, etc.) to query the gun's pickup owner.
    public VRCPlayerApi GetCachedPickupOwner()
    {
        return GetPickupOwner();
    }

    protected virtual void Start()
    {
        Debug.Log($"[UCS auto] Start name={gameObject.name} FireMode={FireMode} bulletChambered={bulletChambered} isReloading={isReloading} InfiniteAmmo={InfiniteAmmo} InfiniteMagazine={InfiniteMagazine} CurrentAmmo={CurrentAmmo}");
        screenCamera = VRCCameraSettings.ScreenCamera;

        // compute cycle time from RPM (seconds per shot)
        if (RoundsPerMinute <= 0f) RoundsPerMinute = 1f;
        CycleTime = 60f / RoundsPerMinute;

        burstShotsRemaining = BurstCount;

        // feed-model specific ammo initialization (overridden by feed subclasses)
        InitializeAmmoState();

        // hit effects pool is self-initializing; guns just request effects when needed
    }

    // Establish the initial chamber/ammo state. The feed-agnostic default simply derives
    // the chamber state from whatever CurrentAmmo the gun spawned with. Feed subclasses
    // (e.g. magazine-fed) override this to pre-fill CurrentAmmo from their capacity first.
    protected virtual void InitializeAmmoState()
    {
        bulletChambered = InfiniteAmmo || InfiniteMagazine || CurrentAmmo > 0;
        bulletInChamberWasFired = false;
        needsReload = !bulletChambered;
        UpdateBulletVisibility(bulletChambered);
    }

    // (Pooling handled by UCS_HitEffectsPool)

    public virtual void TriggerPull()
    {
        Debug.Log($"[UCS auto] base.TriggerPull name={gameObject.name} FireMode={FireMode} bulletChambered={bulletChambered} isReloading={isReloading} InfiniteAmmo={InfiniteAmmo} InfiniteMagazine={InfiniteMagazine} CurrentAmmo={CurrentAmmo}");
        // only fire when a chambered round is available
        if ((InfiniteAmmo || InfiniteMagazine || bulletChambered) && !isReloading)
        {
            //switch case for different fire modes
            //we send network events based on firemode
            switch(FireMode)
            {
                case FireSelection.Semi:
                    if (!IsFireCooldownReady())
                    {
                        return;
                    }
                    MarkFireCycleUsed();
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "FireGun");
                    break;
                case FireSelection.Auto:
                    //instead of sending network events every frame, we send when it starts and stops
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StartAuto");
                    break;
                case FireSelection.Burst:
                    if (!IsFireCooldownReady())
                    {
                        return;
                    }
                    MarkFireCycleUsed();
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "FireBurst"); //we will handle burst logic in FireGun
                    break;
                case FireSelection.Safe:
                    //do nothing
                    break;
            }
        }
        else
        {
            needsReload = true;

            //automatic reload if out of ammo and auto reload is enabled (skip for InfiniteAmmo / InfiniteMagazine)
            if (!InfiniteAmmo && !InfiniteMagazine && AutoReload && !isReloading)
            {
                StartReload();
            }
            else if(!InfiniteAmmo && !InfiniteMagazine)
            {
                //play empty fire sound
                if(EmptyFireSound != null && barrelAudioSource != null)
                {
                    barrelAudioSource.PlayOneShot(EmptyFireSound);
                }
            }
        }
    }
    public void TriggerRelease()
    {
        //stop auto fire
        if(FireMode == FireSelection.Auto)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "StopAuto");
        }
    }
    
    private bool isAutoFiring = false;//no need to sync this since they are called via network events
    public void StartAuto()
    {
        Debug.Log($"[UCS auto] StartAuto name={gameObject.name} isAutoFiring={isAutoFiring} isReloading={isReloading} bulletChambered={bulletChambered} CurrentAmmo={CurrentAmmo} owner={IsPickupOwner()}");
        if (isAutoFiring)
        {
            return;
        }

        isAutoFiring = true;
        float cooldownRemaining = GetFireCooldownRemaining();
        if (cooldownRemaining > 0f)
        {
            SendCustomEventDelayedSeconds("FireAuto", cooldownRemaining);
            return;
        }

        FireAuto();
    }

    public void StopAuto()
    {
        isAutoFiring = false;
    }
    public void FireBurst()
    {
        //set the burst count and call FireGun
        burstShotsRemaining = BurstCount;
        FireGun();
    }
    public void FireAuto()
    {
        Debug.Log($"[UCS auto] FireAuto name={gameObject.name} isAutoFiring={isAutoFiring} isReloading={isReloading} bulletChambered={bulletChambered} CurrentAmmo={CurrentAmmo} cooldownReady={IsFireCooldownReady()}");
        if (!isAutoFiring || isReloading)
        {
            return;
        }

        // Keep the loop running as long as there's ammo. FireGun() itself checks
        // bulletChambered internally and only fires when a round is actually ready.
        // This handles the complex gun where bulletChambered is temporarily cleared
        // during slide ejection while the next round is being chambered.
        bool hasAmmo = InfiniteAmmo || InfiniteMagazine || bulletChambered || CurrentAmmo > 0;
        if (!hasAmmo)
        {
            return;
        }

        if (!IsFireCooldownReady())
        {
            SendCustomEventDelayedSeconds("FireAuto", GetFireCooldownRemaining());
            return;
        }

        MarkFireCycleUsed();
        FireGun();
        //invoke next shot based on cycle time
        SendCustomEventDelayedSeconds("FireAuto", CycleTime);
    }

    private bool IsFireCooldownReady()
    {
        return Time.time >= nextFireAllowedTime;
    }

    private float GetFireCooldownRemaining()
    {
        return Mathf.Max(0f, nextFireAllowedTime - Time.time);
    }

    private void MarkFireCycleUsed()
    {
        nextFireAllowedTime = Time.time + CycleTime;
    }

    // Network-invoked end of the fire-cycle animation
    public void NetworkedEndFireCycle()
    {
        if (GunAnimator != null)
        {
            GunAnimator.SetBool("IsFiring", false);
        }
    }

    private Vector3 GetDesktopAimOrigin(VRCPlayerApi aimPlayer)
    {
        if (aimPlayer != null && aimPlayer == Networking.LocalPlayer && Utilities.IsValid(screenCamera) && screenCamera.Active)
        {
            return screenCamera.Position;
        }

        if (aimPlayer != null)
        {
            return aimPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
        }

        return Vector3.zero;
    }

    private Vector3 GetAimDirection(Transform muzzle, VRCPlayerApi aimPlayer, bool useDesktopRaycastTarget)
    {
        if (muzzle == null)
        {
            return Vector3.forward;
        }

        Vector3 direction = muzzle.forward;
        if (aimPlayer == null || !DesktopFaceFiring || aimPlayer.IsUserInVR())
        {
            return direction;
        }

        bool useScreenCamera = aimPlayer == Networking.LocalPlayer && Utilities.IsValid(screenCamera) && screenCamera.Active;
        Vector3 desktopForward = useScreenCamera
            ? screenCamera.Forward
            : aimPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation * Vector3.forward;

        if (useDesktopRaycastTarget)
        {
            Vector3 desktopOrigin = GetDesktopAimOrigin(aimPlayer);
            RaycastHit desktopHit;
            if (Physics.Raycast(desktopOrigin, desktopForward, out desktopHit, Range, HitLayers))
            {
                return (desktopHit.point - muzzle.position).normalized;
            }
        }

        return desktopForward;
    }

    private void BulletRaycast(Transform muzzle)
    {
        if (muzzle == null) return;

        RaycastHit hit;
        var aimPlayer = GetPickupOwner();
        Vector3 direction = GetAimDirection(muzzle, aimPlayer, DesktopFaceRaycastAiming);

        // primary hit (for damage/force)
        bool primaryHit = false;
        RaycastHit primaryHitInfo = new RaycastHit();
        if (Physics.Raycast(muzzle.position, direction, out hit, Range, HitLayers))
        {
            primaryHit = true;
            primaryHitInfo = hit;

            if (hit.rigidbody != null)
            {
                if (aimPlayer != null && aimPlayer == Networking.LocalPlayer && !Networking.IsOwner(hit.rigidbody.gameObject))
                {
                    Networking.SetOwner(aimPlayer, hit.rigidbody.gameObject);
                }
                hit.rigidbody.AddForceAtPosition(direction * ImpactForce, hit.point, ForceMode.Impulse);
            }
            if (hit.collider != null)
            {
                UCS_Hitbox hitbox = hit.collider.gameObject.GetComponent<UCS_Hitbox>();
                if (hitbox != null)
                {
                    hitbox.HitEvent((int)Damage,cachedPickupOwner != null ? cachedPickupOwner.playerId : -1, -1, (int)DamageType.Bullet);
                    //teamID is currently unused, we can implement team damage logic later and pass the teamID here
                }
            }
        }

        // effect hit (separate ray) - prefer this for visual placement
        RaycastHit effectHitInfo = new RaycastHit();
        bool effectHit = Physics.Raycast(muzzle.position, direction, out effectHitInfo, Range, EffectLayers);

        // choose hit info for effects: prefer effectHit, otherwise use primaryHit
        bool haveEffectSource = false;
        RaycastHit usedHit = new RaycastHit();
        if (effectHit)
        {
            usedHit = effectHitInfo;
            haveEffectSource = true;
        }
        else if (primaryHit)
        {
            usedHit = primaryHitInfo;
            haveEffectSource = true;
        }

        if (haveEffectSource)
        {
            Vector3 surfaceNormal = usedHit.normal;
            Vector3 effectPos = usedHit.point + surfaceNormal * HitEffectOffset;
            Quaternion effectRot = Quaternion.LookRotation(-direction, surfaceNormal);

            GameObject hitEffect = HitEffectsPool != null ? HitEffectsPool.AcquireInstance() : null;
            if (hitEffect != null)
            {
                hitEffect.transform.position = effectPos;
                hitEffect.transform.rotation = effectRot;
            }

            GameObject bulletSpark = BulletSparkPool != null ? BulletSparkPool.AcquireInstance() : null;
            if (bulletSpark != null)
            {
                Vector3 sparkPos = usedHit.point + surfaceNormal * BulletSparkOffset;
                bulletSpark.transform.position = sparkPos;
                bulletSpark.transform.rotation = effectRot;
            }
        }
        else
        {
            // no hit - do not spawn an impact effect
        }
    }
    int burstShotsRemaining;
    public void FireGun()
    {
        Debug.Log($"[UCS auto] FireGun name={gameObject.name} willFire={((InfiniteAmmo || InfiniteMagazine || bulletChambered) && !isReloading)} bulletChambered={bulletChambered} isReloading={isReloading} CurrentAmmo={CurrentAmmo}");
        //to do: implement burst logic
        if((InfiniteAmmo || InfiniteMagazine || bulletChambered) && !isReloading)
        {
            if(multipleBarrels)
            {
                if(MuzzlePoints != null)
                {
                    foreach(Transform muzzle in MuzzlePoints)
                    {
                        if (UseProjectiles)
                        {
                            if (ProjectileManager != null)
                            {
                                Vector3 baseDir = GetAimDirection(muzzle, GetPickupOwner(), DesktopFaceRaycastAiming);
                                if (shotgunMode)
                                {
                                    for (int p = 0; p < PelletsPerShot; p++)
                                    {
                                        float yaw = Random.Range(-ProjectileSpreadAngle, ProjectileSpreadAngle);
                                        float pitch = Random.Range(-ProjectileSpreadAngle, ProjectileSpreadAngle);
                                        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.LookRotation(baseDir);
                                        ProjectileManager.SpawnProjectile(muzzle.position, rot * Vector3.forward, ProjectileSpeed);
                                    }
                                }
                                else
                                {
                                    ProjectileManager.SpawnProjectile(muzzle.position, baseDir, ProjectileSpeed);
                                }
                            }
                            else if (Raycast)
                            {
                                BulletRaycast(muzzle);
                            }
                        }
                        else
                        {
                            if(Raycast)
                            {
                                BulletRaycast(muzzle);
                            }
                        }
                    }
                }
            }
            else
            {
                if (MuzzlePoint != null)
                {
                    if (UseProjectiles)
                    {
                        if (ProjectileManager != null)
                        {
                            Vector3 baseDir = GetAimDirection(MuzzlePoint, GetPickupOwner(), DesktopFaceRaycastAiming);
                            if (shotgunMode)
                            {
                                for (int p = 0; p < PelletsPerShot; p++)
                                {
                                    float yaw = Random.Range(-ProjectileSpreadAngle, ProjectileSpreadAngle);
                                    float pitch = Random.Range(-ProjectileSpreadAngle, ProjectileSpreadAngle);
                                    Quaternion rot = Quaternion.Euler(pitch, yaw, 0f) * Quaternion.LookRotation(baseDir);
                                    ProjectileManager.SpawnProjectile(MuzzlePoint.position, rot * Vector3.forward, ProjectileSpeed);
                                }
                            }
                            else
                            {
                                ProjectileManager.SpawnProjectile(MuzzlePoint.position, baseDir, ProjectileSpeed);
                            }
                        }
                        else if (Raycast)
                        {
                            BulletRaycast(MuzzlePoint);
                        }
                    }
                    else if (Raycast)
                    {
                        BulletRaycast(MuzzlePoint);
                    }
                }
            }

            // start fire-cycle animation on this instance (FireGun is invoked on all clients)
            if (GunAnimator != null)
            {
                bool shouldUseFireLock = !InfiniteAmmo && !InfiniteMagazine && CurrentAmmo <= 1;
                GunAnimator.SetBool("IsFiringLock", shouldUseFireLock);
                GunAnimator.SetBool("IsFiring", !shouldUseFireLock);
                SendCustomEventDelayedSeconds("NetworkedEndFireCycle", CycleTime);
            }

            //play fire sound
            if(FireSound != null && barrelAudioSource != null)
            {
                barrelAudioSource.PlayOneShot(FireSound);
            }

            //play muzzle flash
            if(MuzzleFlashParticleSystem != null)
            {
                MuzzleFlashParticleSystem.Play();
            }

            //eject bullet casing
            if(BulletEjectParticleSystem != null)
            {
                BulletEjectParticleSystem.Play();
            }

            //haptic feedback (guard)
            if(hapticFeedback && Networking.LocalPlayer != null)
            {
                // choose hand or expose setting instead of hardcoding
                Networking.LocalPlayer.PlayHapticEventInHand(VRC.SDKBase.VRC_Pickup.PickupHand.Right, hapticFeedbackAmplitude, hapticFeedbackFrequency, hapticFeedbackDuration);
            }

            // decrease ammo with proper ownership and broadcast the new chamber state
            if (IsPickupOwner() && !InfiniteAmmo && !InfiniteMagazine)
            {
                ConsumeFiredRound();

                if (CurrentAmmo <= 0 && AutoReload && !isReloading)
                {
                    StartReload();
                }
            }
            else if (!InfiniteAmmo && !InfiniteMagazine && !bulletChambered)
            {
                needsReload = true;
            }

            //recurse for burst fire
            if(FireMode == FireSelection.Burst)
            {
                burstShotsRemaining--;
                if(burstShotsRemaining > 0)
                {
                    SendCustomEventDelayedSeconds("FireGun", BurstDelay);
                }
            }
            else
            {
                //reset burst count
                burstShotsRemaining = BurstCount;
            }
        }
    }

    public void StartReload()
    {
        if (isReloading)
        {
            return;
        }

        needsReload = false;
        isReloading = true;
        ReloadGun();
    }

    public void SetChamberLoaded()
    {
        bulletChambered = true;
        bulletInChamberWasFired = false;
        UpdateBulletVisibility(true);
    }

    public void SetChamberEmpty()
    {
        bulletChambered = false;
        UpdateBulletVisibility(false);
    }

    protected virtual void UpdateBulletVisibility(bool visible)
    {
    }

    protected virtual void ConsumeFiredRound()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        if (CurrentAmmo > 0)
        {
            CurrentAmmo--;
            CurrentAmmo = Mathf.Max(CurrentAmmo, 0);
        }

        bulletInChamberWasFired = true;

        if (CurrentAmmo <= 0)
        {
            needsReload = true;
            bulletChambered = false;
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
            RequestSerialization();
        }
    }

    protected void EjectChamberedRound()
    {
        if (!IsPickupOwner() || !bulletChambered)
        {
            return;
        }

        if (CurrentAmmo > 0)
        {
            CurrentAmmo--;
            CurrentAmmo = Mathf.Max(CurrentAmmo, 0);
        }

        bulletChambered = false;
        bulletInChamberWasFired = false;
        needsReload = CurrentAmmo <= 0;
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
        if (CurrentAmmo <= 0)
        {
            RequestSerialization();
        }
    }
    // Feed-agnostic reload completion: clear the reload state and resync. Feed subclasses
    // (magazine, tube, revolver…) override this to actually replenish their ammo store.
    public virtual void CompleteReload()
    {
        if (GunAnimator != null)
        {
            GunAnimator.SetBool("IsFiringLock", false);
        }

        isReloading = false;
        needsReload = false;
        if (!IsPickupOwner())
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
        RequestSerialization();
    }

    // Feed-agnostic default does nothing; feed subclasses drive the actual reload sequence.
    public virtual void ReloadGun()
    {
    }

    public virtual void Pickup()
    {
        if (Networking.LocalPlayer != null && !IsPickupOwner())
        {
            Networking.SetOwner(Networking.LocalPlayer, gameObject);
        }
    }
    public virtual void Drop()
    {
        ClearCachedPickupOwner();
    }
    protected void ConsumeChamberedRound()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        // If there's more than 1 bullet left, eject the unfired round
        if (bulletChambered)
        {
            // Play unfired bullet eject particle
            if (UnfiredBulletEjectParticleSystem != null)
            {
                UnfiredBulletEjectParticleSystem.Play();
            }

            // Load/chamber the next bullet from magazine
            if (!IsPickupOwner())
            {
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            }
            CurrentAmmo--;
            CurrentAmmo = Mathf.Max(CurrentAmmo, 0);
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
            bulletInChamberWasFired = false;
        }
    }

    protected void ConsumeMagazineRound()
    {
        if (!IsPickupOwner())
        {
            return;
        }

        //feed a round from the magazine into the chamber
        if (CurrentAmmo > 0)
        {
            // CurrentAmmo tracks total rounds (magazine + chamber). Moving a round from
            // the magazine into the chamber should not change the total count -- it
            // only moves its location. Only update chamber state here.
            if (!bulletChambered)
            {
                bulletChambered = true;
                bulletInChamberWasFired = false;
                SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberLoaded");
            }
        }
        else if (bulletChambered)
        {
            bulletChambered = false;
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetChamberEmpty");
        }
    }

    public void GunDropped()
    {
        //sync ammo and reloading state when the gun is dropped (ownership changes)
        RequestSerialization();
    }
}

#if UNITY_EDITOR


[CustomEditor(typeof(UCS_BaseGun), true)]
public class UCS_BaseGunEditor : Editor
{
    private SerializedProperty multipleBarrels;
    private SerializedProperty muzzlePoints;
    private SerializedProperty muzzlePoint;
    private SerializedProperty desktopFaceFiring;
    private SerializedProperty desktopFaceRaycastAiming;
    private SerializedProperty raycast;
    private SerializedProperty range;
    private SerializedProperty hitLayers;
    private SerializedProperty effectLayers;
    private SerializedProperty damage;
    private SerializedProperty impactForce;
    private SerializedProperty hitEffectsPoolProp;
    private SerializedProperty bulletSparkPoolProp;
    private SerializedProperty useProjectiles;
    private SerializedProperty projectileManager;
    private SerializedProperty projectileSpeed;
    private SerializedProperty projectileSpreadAngle;
    private SerializedProperty roundsPerMinute;
    private SerializedProperty magazineSize;
    private SerializedProperty reloadTime;
    private SerializedProperty refillAmmoOnDisable;
    private SerializedProperty infiniteAmmo;
    private SerializedProperty infiniteMagazine;
    private SerializedProperty autoReload;
    private SerializedProperty bulletInChamberAddsToMag;
    private SerializedProperty fireMode;
    private SerializedProperty burstCount;
    private SerializedProperty burstDelay;
    private SerializedProperty shotgunMode;
    private SerializedProperty pelletsPerShot;
    private SerializedProperty barrelAudioSource;
    private SerializedProperty magazineAudioSource;
    private SerializedProperty firemechanicsAudioSource;
    private SerializedProperty fireSound;
    private SerializedProperty emptyFireSound;
    private SerializedProperty magpullSound;
    private SerializedProperty maginsertSound;
    private SerializedProperty slideBackSound;
    private SerializedProperty slideForwardSound;
    private SerializedProperty hapticFeedback;
    private SerializedProperty hapticFeedbackDuration;
    private SerializedProperty hapticFeedbackAmplitude;
    private SerializedProperty hapticFeedbackFrequency;
    private SerializedProperty hapticFeedbackOnManualReload;
    private SerializedProperty gunAnimator;
    private SerializedProperty cycleAnimation;
    private SerializedProperty reloadAnimation;
    private SerializedProperty muzzleFlashParticleSystem;
    private SerializedProperty bulletEjectParticleSystem;
    private SerializedProperty unfiredBulletEjectParticleSystem;

    private bool muzzleSettingsFoldout = true;
    private bool raycastSettingsFoldout = true;
    private bool projectileSettingsFoldout = false;
    private bool gunSettingsFoldout = true;
    private bool fireModeFoldout = true;
    private bool audioSettingsFoldout = true;
    private bool hapticSettingsFoldout = true;
    private bool animationSettingsFoldout = true;
    private bool particleSettingsFoldout = true;

    private void OnEnable()
    {
        multipleBarrels = serializedObject.FindProperty("multipleBarrels");
        muzzlePoints = serializedObject.FindProperty("MuzzlePoints");
        muzzlePoint = serializedObject.FindProperty("MuzzlePoint");
        desktopFaceFiring = serializedObject.FindProperty("DesktopFaceFiring");
        desktopFaceRaycastAiming = serializedObject.FindProperty("DesktopFaceRaycastAiming");
        raycast = serializedObject.FindProperty("Raycast");
        range = serializedObject.FindProperty("Range");
        hitLayers = serializedObject.FindProperty("HitLayers");
        effectLayers = serializedObject.FindProperty("EffectLayers");
        damage = serializedObject.FindProperty("Damage");
        impactForce = serializedObject.FindProperty("ImpactForce");
        hitEffectsPoolProp = serializedObject.FindProperty("HitEffectsPool");
        bulletSparkPoolProp = serializedObject.FindProperty("BulletSparkPool");
        useProjectiles = serializedObject.FindProperty("UseProjectiles");
        projectileManager = serializedObject.FindProperty("ProjectileManager");
        projectileSpeed = serializedObject.FindProperty("ProjectileSpeed");
        projectileSpreadAngle = serializedObject.FindProperty("ProjectileSpreadAngle");
        roundsPerMinute = serializedObject.FindProperty("RoundsPerMinute");
        magazineSize = serializedObject.FindProperty("MagazineSize");
        reloadTime = serializedObject.FindProperty("ReloadTime");
        refillAmmoOnDisable = serializedObject.FindProperty("refillAmmoOnDisable");
        infiniteAmmo = serializedObject.FindProperty("InfiniteAmmo");
        infiniteMagazine = serializedObject.FindProperty("InfiniteMagazine");
        autoReload = serializedObject.FindProperty("AutoReload");
        bulletInChamberAddsToMag = serializedObject.FindProperty("bulletInChamberAddsToMag");
        fireMode = serializedObject.FindProperty("FireMode");
        burstCount = serializedObject.FindProperty("BurstCount");
        burstDelay = serializedObject.FindProperty("BurstDelay");
        shotgunMode = serializedObject.FindProperty("shotgunMode");
        pelletsPerShot = serializedObject.FindProperty("PelletsPerShot");
        barrelAudioSource = serializedObject.FindProperty("barrelAudioSource");
        magazineAudioSource = serializedObject.FindProperty("magazineAudioSource");
        firemechanicsAudioSource = serializedObject.FindProperty("firemechanicsAudioSource");
        fireSound = serializedObject.FindProperty("FireSound");
        emptyFireSound = serializedObject.FindProperty("EmptyFireSound");
        magpullSound = serializedObject.FindProperty("magpullSound");
        maginsertSound = serializedObject.FindProperty("maginsertSound");
        slideBackSound = serializedObject.FindProperty("slideBackSound");
        slideForwardSound = serializedObject.FindProperty("slideForwardSound");
        hapticFeedback = serializedObject.FindProperty("hapticFeedback");
        hapticFeedbackDuration = serializedObject.FindProperty("hapticFeedbackDuration");
        hapticFeedbackAmplitude = serializedObject.FindProperty("hapticFeedbackAmplitude");
        hapticFeedbackFrequency = serializedObject.FindProperty("hapticFeedbackFrequency");
        hapticFeedbackOnManualReload = serializedObject.FindProperty("hapticFeedbackOnManualReload");
        gunAnimator = serializedObject.FindProperty("GunAnimator");
        cycleAnimation = serializedObject.FindProperty("CycleAnimation");
        reloadAnimation = serializedObject.FindProperty("reloadAnimation");
        muzzleFlashParticleSystem = serializedObject.FindProperty("MuzzleFlashParticleSystem");
        bulletEjectParticleSystem = serializedObject.FindProperty("BulletEjectParticleSystem");
        unfiredBulletEjectParticleSystem = serializedObject.FindProperty("UnfiredBulletEjectParticleSystem");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Muzzle Settings
        muzzleSettingsFoldout = EditorGUILayout.Foldout(muzzleSettingsFoldout, "Muzzle Settings", true);
        if (muzzleSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(multipleBarrels);
            if (multipleBarrels.boolValue)
            {
                EditorGUILayout.PropertyField(muzzlePoints, new GUIContent("Muzzle Points"));
            }
            else
            {
                EditorGUILayout.PropertyField(muzzlePoint, new GUIContent("Muzzle Point"));
            }
            EditorGUILayout.PropertyField(desktopFaceFiring);
                EditorGUILayout.PropertyField(desktopFaceRaycastAiming, new GUIContent("Desktop Camera Raycast Aiming"));
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Firing Mode Settings
        fireModeFoldout = EditorGUILayout.Foldout(fireModeFoldout, "Fire Mode Settings", true);
        if (fireModeFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(fireMode);
            if (fireMode.enumValueIndex == (int)FireSelection.Burst)
            {
                EditorGUILayout.PropertyField(burstCount);
                EditorGUILayout.PropertyField(burstDelay);
            }
            EditorGUILayout.PropertyField(shotgunMode);
            if (shotgunMode.boolValue)
            {
                EditorGUILayout.PropertyField(pelletsPerShot);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Raycast Settings
        raycastSettingsFoldout = EditorGUILayout.Foldout(raycastSettingsFoldout, "Raycast Settings", true);
        if (raycastSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(raycast);
            if (raycast.boolValue)
            {
                EditorGUILayout.PropertyField(range);
                EditorGUILayout.PropertyField(hitLayers);
                EditorGUILayout.PropertyField(effectLayers, new GUIContent("Effect Layers (for bullet sparks and hit effects)"));
                EditorGUILayout.PropertyField(damage);
                EditorGUILayout.PropertyField(impactForce);
                EditorGUILayout.PropertyField(hitEffectsPoolProp, new GUIContent("Hit Effect Pool"));
                EditorGUILayout.PropertyField(bulletSparkPoolProp, new GUIContent("Bullet Spark Pool"));
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        EditorGUILayout.Space();

        // Projectile Settings
        projectileSettingsFoldout = EditorGUILayout.Foldout(projectileSettingsFoldout, "Projectile Settings", true);
        if (projectileSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(useProjectiles);
            if (useProjectiles.boolValue)
            {
                EditorGUILayout.PropertyField(projectileManager, new GUIContent("Projectile Manager"));
                EditorGUILayout.PropertyField(projectileSpeed);
                EditorGUILayout.PropertyField(projectileSpreadAngle, new GUIContent("Projectile Spread Angle"));
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Gun Settings
        gunSettingsFoldout = EditorGUILayout.Foldout(gunSettingsFoldout, "Gun Settings", true);
        if (gunSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(roundsPerMinute, new GUIContent("Rounds Per Minute (RPM)"));
            // magazine-specific fields live on UCS_MagFedGun; guard so feed-agnostic
            // guns (tube, revolver, break-action) don't NRE here.
            if (magazineSize != null) EditorGUILayout.PropertyField(magazineSize);
            EditorGUILayout.PropertyField(reloadTime);
            if (refillAmmoOnDisable != null) EditorGUILayout.PropertyField(refillAmmoOnDisable);
            EditorGUILayout.PropertyField(infiniteAmmo);
            EditorGUILayout.PropertyField(infiniteMagazine);
            EditorGUILayout.PropertyField(autoReload);
            if (bulletInChamberAddsToMag != null) EditorGUILayout.PropertyField(bulletInChamberAddsToMag);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Audio Settings
        audioSettingsFoldout = EditorGUILayout.Foldout(audioSettingsFoldout, "Audio Settings", true);
        if (audioSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(barrelAudioSource);
            EditorGUILayout.PropertyField(magazineAudioSource);
            EditorGUILayout.PropertyField(firemechanicsAudioSource);
            EditorGUILayout.PropertyField(fireSound);
            EditorGUILayout.PropertyField(emptyFireSound);
            EditorGUILayout.PropertyField(magpullSound);
            EditorGUILayout.PropertyField(maginsertSound);
            EditorGUILayout.PropertyField(slideBackSound);
            EditorGUILayout.PropertyField(slideForwardSound);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Haptic Feedback Settings
        hapticSettingsFoldout = EditorGUILayout.Foldout(hapticSettingsFoldout, "Haptic Feedback", true);
        if (hapticSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(hapticFeedback);
            if (hapticFeedback.boolValue)
            {
                EditorGUILayout.PropertyField(hapticFeedbackDuration);
                EditorGUILayout.PropertyField(hapticFeedbackAmplitude);
                EditorGUILayout.PropertyField(hapticFeedbackFrequency);
                EditorGUILayout.PropertyField(hapticFeedbackOnManualReload);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Animation Settings
        animationSettingsFoldout = EditorGUILayout.Foldout(animationSettingsFoldout, "Animation Settings", true);
        if (animationSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(gunAnimator);
            if (!(target is UCS_ComplexGun))
            {
                EditorGUILayout.PropertyField(cycleAnimation);
            }
            if (reloadAnimation != null && !(target is UCS_ComplexGun))
            {
                EditorGUILayout.PropertyField(reloadAnimation);
            }
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        // Particle Systems
        particleSettingsFoldout = EditorGUILayout.Foldout(particleSettingsFoldout, "Particle Systems", true);
        if (particleSettingsFoldout)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(muzzleFlashParticleSystem);
            EditorGUILayout.PropertyField(bulletEjectParticleSystem);
            EditorGUILayout.PropertyField(unfiredBulletEjectParticleSystem);
            EditorGUI.indentLevel--;
        }

        serializedObject.ApplyModifiedProperties();
    }
}
#endif