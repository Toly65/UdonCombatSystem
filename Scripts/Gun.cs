using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
using UnityEngine.XR;
public enum fireSelection
{
    Safe,
    Semi,
    Auto,
    Burst
}

[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[AddComponentMenu("")]

public class Gun : UdonSharpBehaviour
{
    [Header("general settings")]
    public bool infiniteAmmo;
    [SerializeField] private float maxDistance;
    public float RaycastDamageAmount;
    [SerializeField] private bool shotgun = false;
    [SerializeField] private int pelletCount;
    [SerializeField] private Transform firePosition;
    [UdonSynced] public int AmmoCount = 5;
    [SerializeField] private float BulletSpread = 10f;
    [SerializeField] bool automaticReload = true;

    [Header("FireSelectionSettings")]
    public fireSelection fireSelection = fireSelection.Semi;
    public bool BaseCycleOffAnimation;
    public float CycleTime;
    public float BurstCycleTime;
    public int BurstCount;
    [Header("audioSettings")]
    public AudioSource barrelAudioSource;
    public AudioSource magazineAudioSource;
    public AudioSource fireMechanismAudioSource;
    [SerializeField] private AudioClip gunShot;
    [SerializeField] private AudioClip MagPull;
    [SerializeField] private AudioClip MagInsert;
    [SerializeField] private AudioClip GunCock;
    [SerializeField] private AudioClip GunEmpty;

    [Header("raycast settings")]
    public bool RaycastDamage = true;
    [SerializeField] private bool RaycastBulletDrop;
    [SerializeField] private bool desktopFaceFiring;
    [SerializeField] private GameObject playerRaycastSpark;
    [SerializeField] private GameObject terrainRaycastSpark;
    [SerializeField] private LayerMask sparkable;
    [SerializeField] private LayerMask players;

    [Header("Rigibody projectile, requires raycast to be off")]
    public GameObject bullet;
    public float fireVelocity = 5f;

    [Header("multi-barrel settings, if the script detects multiple barrels it will use them")]
    [SerializeField] private Transform[] barrels;
    [SerializeField] private bool fireAllBarrelsAtOnce;
    public bool ammoCountDependsOnBarrels;
    private int currentBarrel = 0;


    [Header("physics stuff, only add if you're putting this on a vehicle")]
    [SerializeField] Rigidbody targetRigidbody;
    [SerializeField] private float forceFromBarrel;

    [Header("haptic feedback")]
    public bool hapticFeedback;
    [SerializeField] private float hapticFeedbackDuration = 0.1f;
    [SerializeField] private float hapticFeedbackAmplitude = 1;
    [SerializeField] private float hapticFeedbackFrequency = 1;
    [SerializeField] private bool hapticFeedbackOnManualReload;
    [SerializeField] private VRC_Pickup secondaryGripPickup;
    [SerializeField] private VRC_Pickup pickup;
    [Header("Manipulation Settings")]
    public bool useVirtualStock;
    [SerializeField] private Transform stockTransform;
    [SerializeField] private Transform rotationPointTransform;
    [SerializeField] private float relativeVirtualStockActivationDistance;
    [SerializeField] private Vector2 rotationRange = new Vector2(180, 180);

    [Header("Manual Reload")]

    [Header("addons")]

    public Text Display;
    public HUDAmmoCount ammoCountHud;


    [Header("animation settings")]
    public Animator GunAnimator;
    public AnimationClip CycleAnimation;

    public AnimationClip ReloadAnimation;
    public float reloadTime = 15f;
    public bool BaseReloadTimeOffAnimation;
    public Animator magazineAnimator;
    public string MagazineAnimatorVariable = "AmmoPercentage";
    [Header("particles are visual only")]
    public ParticleSystem ShellParticle;
    public ParticleSystem gunShotParticle;

    [HideInInspector] public int MaxAmmo;
    private bool startTimer = false;
    private float currentTime;
    private float wantedTime;
    [HideInInspector] public bool AmmoCheck = true;

    private bool isreloadingaudio = true;
    private VRCPlayerApi localPlayer;
    private bool DesktopUser;

    private float firedTime;
    private int MagazineAnimatorVariableHash;

    private string AnimName;
    private bool Firing = false;
    private bool Scoped;
    private VRC_Pickup.PickupHand hand;
    [UdonSynced] private float playerHeight;
    public void TriggerPull()
    {
        //switch case for different fire modes

        if (AmmoCount > 0)
        {
            switch (fireSelection)
            {
                case fireSelection.Safe:
                    break;
                case fireSelection.Semi:
                    Fire();
                    break;
                case fireSelection.Auto:
                    SetFireTrue();
                    break;
                case fireSelection.Burst:
                    if (!Firing)
                    {
                        Firing = true;
                        Fire();
                    }
                    break;
            }
        }
        else
        {
            if (isreloadingaudio)
            {
                isreloadingaudio = false;
                magazineAudioSource.PlayOneShot(GunEmpty);
            }
        }
    }
    public void TriggerRelease()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetFireFalse");
    }

    public void OnDisable()
    {
        AmmoCount = MaxAmmo;
    }
    public void FireBurst()
    {
        fireSelection = fireSelection.Burst;
    }
    public void SetFireTrue()
    {
        fireSelection = fireSelection.Auto;
        Firing = true;
    }

    public void SetFireFalse()
    {
        Firing = false;
    }
    private void Update()
    {

        if (Firing == true)
        {
            if (Time.time - firedTime > CycleTime)
            {
                Fire();
            }
        }
        if(pickup)
        {
            if (Input.GetKey(KeyCode.Q) && !startTimer && pickup.IsHeld && localPlayer == Networking.GetOwner(gameObject))
            {
                Reload();
            }
        }
        

    }
    public float GetAvatarHeight(VRCPlayerApi player)
    {
        float height = 0;
        Vector3 postition1 = player.GetBonePosition(HumanBodyBones.Head);
        Vector3 postition2 = player.GetBonePosition(HumanBodyBones.Neck);
        height += (postition1 - postition2).magnitude;
        postition1 = postition2;
        postition2 = player.GetBonePosition(HumanBodyBones.Hips);
        height += (postition1 - postition2).magnitude;
        postition1 = postition2;
        postition2 = player.GetBonePosition(HumanBodyBones.RightLowerLeg);
        height += (postition1 - postition2).magnitude;
        postition1 = postition2;
        postition2 = player.GetBonePosition(HumanBodyBones.RightFoot);
        height += (postition1 - postition2).magnitude;
        return height;
    }
    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        if (magazineAnimator)
        {
            MagazineAnimatorVariableHash = Animator.StringToHash(MagazineAnimatorVariable);
        }
        //audio = (AudioSource)gameObject.GetComponent(typeof(AudioSource));
        if (!localPlayer.IsUserInVR())
        {
            DesktopUser = true;
        }
        else
        {
            DesktopUser = false;
        }

        
        Debug.Log("Ammo Left: " + AmmoCount);

        if(ammoCountDependsOnBarrels)
        {
            AmmoCount = barrels.Length;
        }
        MaxAmmo = AmmoCount;

        if (BaseCycleOffAnimation&&CycleAnimation)
        {
            CycleTime = CycleAnimation.length;
        }
        if (BaseReloadTimeOffAnimation && ReloadAnimation)
        {
            reloadTime = ReloadAnimation.length;
        }
        if(secondaryGripPickup)
        {
            secondaryGripPickup.pickupable = false;
        }
    }
    public void Pickup()
    {
        //owner = localPlayer;
        
        if(secondaryGripPickup != null)
        {
            secondaryGripPickup.pickupable = true;
        }
        //find which hand the current pickup is in
        if (!pickup)
            return;
        if (localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Left) == pickup)
        {
            hand = VRC_Pickup.PickupHand.Left;
        }
        else if (localPlayer.GetPickupInHand(VRC_Pickup.PickupHand.Right) == pickup)
        {
            hand = VRC_Pickup.PickupHand.Right;
        }
        else
        {
            hand = VRC_Pickup.PickupHand.None;
        }
        playerHeight = GetAvatarHeight(localPlayer);
        RequestSerialization();
    }

    public void Drop()
    {
        if(secondaryGripPickup != null)
        {
            secondaryGripPickup.pickupable = false;
        }
        RequestSerialization();
    }
    private void FixedUpdate()
    {
        if (AmmoCount > 0 && Display != null)
        {
            Display.text = (AmmoCount + " / " + MaxAmmo);
        }
        

        if (AmmoCount <= 0 && AmmoCheck && automaticReload)
        {
            Reload();
            AmmoCheck = false;
        }

        if (!startTimer) return;

        //TODO check if automatic reload
        
        if (currentTime > wantedTime - 0.7 && isreloadingaudio)
        {
            if (MagPull != null && magazineAudioSource)
            {
                magazineAudioSource.PlayOneShot(MagPull);
            }
            
            isreloadingaudio = false;
        }
        if (currentTime < wantedTime)
        {
            currentTime += Time.fixedDeltaTime;
        }
        else
        {
            startTimer = false;
            AmmoCount = MaxAmmo;
            AmmoCheck = true;
            if (MagInsert != null&& magazineAudioSource)
            {
                magazineAudioSource.PlayOneShot(MagInsert);
            }

            isreloadingaudio = true;
            Debug.Log("Reloaded. Ammo now: " + AmmoCount);
            if (Display != null)
            {
                Display.text = (AmmoCount.ToString() + " / " + MaxAmmo.ToString());
            }
        }
    }
    public void BulletRaycast()
    {
        Vector3 startPoint = firePosition.position;
        Vector3 velocity = firePosition.forward * fireVelocity;
        RaycastHit hit;
        int iterations = 100;
        float timeStep = 0.01f;
        if(RaycastBulletDrop)
        {
            for (int ii = 1; ii < iterations; ii++)
            {
                Debug.DrawLine(startPoint, startPoint + velocity * timeStep);
                startPoint += velocity * timeStep;
                // Detect collision

                if (Physics.Raycast(startPoint, velocity, out hit, velocity.magnitude * timeStep, players))
                {
                   
                        Debug.Log("player layer hit");
                        if (hit.transform.gameObject.name.Contains("hitbox"))
                        {
                            if (localPlayer.IsOwner(gameObject))
                            {
                                GameObject target = hit.collider.gameObject;
                                UdonBehaviour TargetBehaviour = (UdonBehaviour)target.GetComponent(typeof(UdonBehaviour));
                                Debug.Log("before Modification");
                                TargetBehaviour.SetProgramVariable("Modifier", (RaycastDamageAmount * -1));
                                Debug.Log("aftermodification");
                                TargetBehaviour.SendCustomEvent("ModifyHealth");
                            }
                            if (playerRaycastSpark != null)
                            {
                                var spark = VRCInstantiate(playerRaycastSpark);
                                spark.transform.position = hit.point;
                                spark.SetActive(true);
                            }
                        }
                }
                if (Physics.Raycast(startPoint, velocity, out hit, velocity.magnitude * timeStep, sparkable))
                {
                    Debug.Log("raycast hit");
                    if (hit.collider != null)
                    {
                        if (!hit.collider.isTrigger)
                        {
                            if (terrainRaycastSpark != null)
                            {
                                var spark = VRCInstantiate(terrainRaycastSpark);
                                spark.transform.position = hit.point;
                                spark.SetActive(true);
                                Debug.Log("wall hit");
                            }
                            Debug.Log("wall hit");

                        }
                    }
                }
                    velocity.y -= 9.81f * timeStep; // simulate gravitational acceleration
            }
        }
        else
        {
            if ((Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, players)))
            {

                Debug.Log("player layer hit");
                if (hit.transform.gameObject.name.Contains("hitbox"))
                {
                    if (localPlayer.IsOwner(gameObject))
                    {
                        GameObject target = hit.collider.gameObject;
                        UdonBehaviour TargetBehaviour = (UdonBehaviour)target.GetComponent(typeof(UdonBehaviour));
                        Debug.Log("before Modification");
                        TargetBehaviour.SetProgramVariable("Modifier", (RaycastDamageAmount * -1));
                        Debug.Log("aftermodification");
                        TargetBehaviour.SendCustomEvent("ModifyHealth");
                    }
                    if (playerRaycastSpark != null)
                    {
                        var spark = VRCInstantiate(playerRaycastSpark);
                        spark.transform.position = hit.point;
                        spark.SetActive(true);
                    }
                }
                
            }
            if ((Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, sparkable)))
            {
                Debug.Log("raycast hit");
                    if (hit.collider != null)
                    {
                        if (!hit.collider.isTrigger)
                        {
                            if (terrainRaycastSpark != null)
                            {
                                var spark = VRCInstantiate(terrainRaycastSpark);
                                spark.transform.position = hit.point;
                                spark.SetActive(true);
                                Debug.Log("wall hit");
                            }
                            Debug.Log("wall hit");
                        }
                    }
                
            }
        } 
    }
    public void Fire()
    {
       
        if(barrels.Length!=0)
        {
            firePosition.position = barrels[currentBarrel].position;
            Debug.Log("fire position changed to new barrel " + currentBarrel);
            //select the next available current barrel
            if (currentBarrel < barrels.Length - 1)
            {
                currentBarrel++;
            }
            else
            {
                currentBarrel = 0;
            }
        }
        
        if (Time.time - firedTime < CycleTime)
        {
            Debug.Log("fired too fast");
            return;
        }
        firedTime = Time.time;
        if (infiniteAmmo)
        {
            AmmoCount++;
        }
        if(magazineAnimator)
        {
            magazineAnimator.SetFloat(MagazineAnimatorVariableHash, AmmoCount / MaxAmmo);
        }
        if (AmmoCount > 0)
        {
            if (hapticFeedback && pickup && Networking.LocalPlayer == Networking.GetOwner(gameObject))
            {
                Networking.LocalPlayer.PlayHapticEventInHand(hand, hapticFeedbackDuration, hapticFeedbackAmplitude, hapticFeedbackFrequency);
                if(secondaryGripPickup)
                {
                    if(secondaryGripPickup.IsHeld)
                    {
                        //get the opposite hand to play a haptic event
                        if(hand == VRC_Pickup.PickupHand.Left)
                        {
                            Networking.LocalPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Right, hapticFeedbackDuration, hapticFeedbackAmplitude, hapticFeedbackFrequency);
                        }
                        else
                        {
                            Networking.LocalPlayer.PlayHapticEventInHand(VRC_Pickup.PickupHand.Left, hapticFeedbackDuration, hapticFeedbackAmplitude, hapticFeedbackFrequency);
                        }
                    }
                }
            }
            if (targetRigidbody && localPlayer == Networking.GetOwner(targetRigidbody.gameObject))
            {
                targetRigidbody.AddForceAtPosition((-firePosition.forward) * forceFromBarrel, firePosition.position);
                Debug.Log("force applied, " + ((-firePosition.forward) * forceFromBarrel).magnitude);
            }
            if (RaycastDamage)
            {

                //raycast stuff
                if (shotgun)
                {
                    for (int i = 0; i <= pelletCount; i++)
                    {
                        if (desktopFaceFiring && DesktopUser && Networking.IsOwner(gameObject))
                        {
                            firePosition.position = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                            firePosition.rotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
                        }
                        Quaternion temp = firePosition.rotation;
                        firePosition.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));
                        if(gunShotParticle)
                        {
                            gunShotParticle.Play();
                        }
                        BulletRaycast();
                        
                        firePosition.rotation = temp;
                    }
                    if (GunAnimator&&CycleAnimation)
                    {
                        GunAnimator.Play("Base Layer." + CycleAnimation.name, 0, 0.25f);
                    }

                    AmmoCount--;
                    if (gunShot != null && barrelAudioSource)
                    {
                        barrelAudioSource.PlayOneShot(gunShot);
                    }
                    

                    if (ShellParticle != null)
                    {
                        ShellParticle.Play();
                    }
                    
                }
                else
                {

                    if (desktopFaceFiring && DesktopUser && Networking.IsOwner(gameObject))
                    {
                        firePosition.position = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                        firePosition.rotation = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation;
                    }
                    Quaternion temp = firePosition.localRotation;
                    firePosition.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));
                    BulletRaycast();
                    firePosition.localRotation = temp;

                    if (GunAnimator && CycleAnimation)
                    {
                        GunAnimator.Play("Base Layer." + CycleAnimation.name, 0, 0.25f);
                    }

                    AmmoCount--;
                    if (gunShot != null && barrelAudioSource)
                    {
                        barrelAudioSource.PlayOneShot(gunShot);
                    }

                    if (ShellParticle != null)
                    {
                        ShellParticle.Play();
                    }
                    if (gunShotParticle)
                    {
                        gunShotParticle.Play();
                    }
                }
            }
            else
            {

                //projectile stuff
                if (shotgun == false)
                {

                    //bullet shooting
                    //TODO replace instantiation with localised object pools

                    var bul = VRCInstantiate(bullet);



                    bul.transform.SetParent(null);
                    bul.SetActive(true);
                    if (desktopFaceFiring && DesktopUser && Networking.IsOwner(gameObject))
                    {
                        bul.transform.SetPositionAndRotation(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
                    }
                    else
                    {
                        bul.transform.SetPositionAndRotation(firePosition.position, firePosition.rotation);
                    }


                    bul.transform.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));

                    var bulletrb = (Rigidbody)bul.GetComponent(typeof(Rigidbody));
                    bulletrb.velocity = (bul.transform.forward) * fireVelocity;
                    if (GunAnimator != null)
                    {
                        GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                    }

                    AmmoCount--;
                    if (gunShot != null && barrelAudioSource)
                    {
                        barrelAudioSource.PlayOneShot(gunShot);
                    }   

                    if (ShellParticle!=null)
                    {
                        ShellParticle.Play();
                    }

                }
                else
                {
                    //shotgun

                    for (int i = 0; i <= pelletCount; i++)
                    {
                        var bul = VRCInstantiate(bullet);
                        bul.transform.parent = null;

                        bul.transform.SetPositionAndRotation(firePosition.position, firePosition.rotation);
                        bul.transform.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));
                        bul.SetActive(true);
                        var bulletrb = (Rigidbody)bul.GetComponent(typeof(Rigidbody));
                        bulletrb.velocity = (bul.transform.forward) * fireVelocity;
                    }
                    if (GunAnimator != null)
                    {
                        GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                    }

                    AmmoCount--;
                    if (gunShot != null && barrelAudioSource)
                    {
                        barrelAudioSource.PlayOneShot(gunShot);
                    }                

                    if (ShellParticle!=null)
                    {
                        ShellParticle.Play();
                    }
                }
            }
        }
        //Debug.Log("Ammo Left: " + AmmoCount);
    }

    private void HandleGunRotation()
    {
        float actualShoulderActivationDistance = relativeVirtualStockActivationDistance * playerHeight;
        //check if the stocktransform is near one of the shoulders, using the tracking data for the shoulders
        
        
        
    }

    public void Reload()
    { 
        if (Display != null)
        {
            Display.text = "Reloading";
        }

        if (MagPull != null && magazineAudioSource)
        {
            magazineAudioSource.PlayOneShot(MagPull);
        }
        

        if(ReloadAnimation&&GunAnimator)
        {
                GunAnimator.Play("Base Layer." + ReloadAnimation.name, 0, 0.25f);   
        }
        // Debug.Log("Reloading");
        currentTime = Time.time;
        wantedTime = currentTime + reloadTime;
        startTimer = true;
        //send changes to network
        if(localPlayer.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }
}
