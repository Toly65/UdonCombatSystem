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
    
    [SerializeField] private float maxDistance;
    public float RaycastDamageAmount;
    [SerializeField] private bool shotgun = false;
    [SerializeField] private int pelletCount;
    [SerializeField] private Transform firePosition;
    public bool infiniteAmmo;
    [UdonSynced] public int AmmoCount = 5;
    [Tooltip("The amount of spread in degrees")]
    [SerializeField] private float BulletSpread = 10f;

    [Header("FireSelectionSettings")]
    public fireSelection fireSelection = fireSelection.Semi;
    public bool BaseCycleOffAnimation;
    public float CycleTime;
    public float BurstCycleTime;
    public int BurstCount;
    private int currentBurstCount;
    [Header("audioSettings")]
    public AudioSource barrelAudioSource;
    public AudioSource magazineAudioSource;
    public AudioSource fireMechanismAudioSource;
    //foldout
    [SerializeField] private AudioClip gunShot;
    [SerializeField] private AudioClip MagPull;
    [SerializeField] private AudioClip MagInsert;
    [SerializeField] private AudioClip GunCock;
    [SerializeField] private AudioClip GunEmpty;


    [Header("reload settings")]
    public float reloadTime = 15f;
    public bool BaseReloadTimeOffAnimation;
    [SerializeField] private bool reloadOnMagEmpty;
    [SerializeField] private bool bulletInChamberAddsToAmmoCount;


    public Animator magazineAnimator;
    public string MagazineAnimatorVariable = "AmmoPercentage";

    private float firedTime;
    private int MagazineAnimatorVariableHash;
    private bool reloading;
    private float reloadTimer;

    //individual timers for each reloading audioclip
    private float magPullTimer;
    private float magInsertTimer;
    private float gunCockTimer;

    //this timer is for when the mag should be inserted
    private float magInsertTimeStamp;

    //bools tracking which stage of reloading is done
    private bool magPulled;
    private bool magInserted;
    private bool gunCocked;

    [UdonSynced] private bool bulletInChamber = true;

    [Header("raycast settings")]
    public bool RaycastDamage = true;
    [SerializeField] private bool RaycastBulletDrop;
    [SerializeField] private bool desktopFaceFiring;
    [SerializeField] private GameObject playerRaycastSpark;
    [SerializeField] private GameObject terrainRaycastSpark;
    [Tooltip("the layer which spawns a spark on terrain")]
    [SerializeField] private LayerMask sparkable;
    [Tooltip("the layer which the raycast will hit players and potentially spawn sparks on players, like blood")]
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
    private GripScript secondaryGripScript;
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
    
    
    
    [Header("particles are visual only")]
    public ParticleSystem ShellParticle;
    public ParticleSystem gunShotParticle;

    [HideInInspector] public int MaxAmmo;
    [HideInInspector] public bool AmmoCheck = true;

    private VRCPlayerApi localPlayer;
    private bool DesktopUser;

    
    

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
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "Fire");
                    break;
                case fireSelection.Auto:
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetFireTrue");
                    break;
                case fireSelection.Burst:
                    SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "FireBurst");
                    break;
            }
        }
        else
        {
            //automatic reload
            if (reloadOnMagEmpty&&!reloading)
            {
                Reload();
                return;
            }
            if (fireMechanismAudioSource&&GunEmpty&&!reloading)
            {
                fireMechanismAudioSource.PlayOneShot(GunEmpty);
            }
            bulletInChamber = false;
           
        }
    }
    public void TriggerRelease()
    {
        if (fireSelection == fireSelection.Auto)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetFireFalse");
        }
    }

    public void OnDisable()
    {
        AmmoCount = MaxAmmo;
    }
    public void FireBurst()
    {
        fireSelection = fireSelection.Burst;
        currentBurstCount = BurstCount;
        Firing = true;
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

    private GameObject[] raycastSparks;
    private GameObject[] playerRaycastSparks;
    private void SummonRaycastSpark(bool PlayerHit)
    {
        //this function manages object pooling for the sparks
        //if there are no sparks in the array, then instantiate a new one
        //the bool input determines if the spark is a player spark or a terrain spark

        if (PlayerHit)
        {
            if (playerRaycastSparks == null)
            {
                playerRaycastSparks = new GameObject[1];
                playerRaycastSparks[0] = VRCInstantiate(playerRaycastSpark);
                playerRaycastSparks[0].SetActive(true);
            }
            else
            {
                //check if any sparks are available
                for (int i = 0; i < playerRaycastSparks.Length; i++)
                {
                    if (!playerRaycastSparks[i].activeSelf)
                    {
                        playerRaycastSparks[i].SetActive(true);
                        return;
                    }
                }
                //if no sparks are available, instantiate a new one
                GameObject[] temp = new GameObject[playerRaycastSparks.Length + 1];
                for (int i = 0; i < playerRaycastSparks.Length; i++)
                {
                    temp[i] = playerRaycastSparks[i];
                }
                temp[playerRaycastSparks.Length] = VRCInstantiate(playerRaycastSpark);
                temp[playerRaycastSparks.Length].SetActive(true);
                playerRaycastSparks = temp;
            }
        }
        else
        {
            if (raycastSparks == null)
            {
                raycastSparks = new GameObject[1];
                raycastSparks[0] = VRCInstantiate(terrainRaycastSpark);
                raycastSparks[0].SetActive(true);
            }
            else
            {
                //check if any sparks are available
                for (int i = 0; i < raycastSparks.Length; i++)
                {
                    if (!raycastSparks[i].activeSelf)
                    {
                        raycastSparks[i].SetActive(true);
                        return;
                    }
                }
                //if no sparks are available, instantiate a new one
                GameObject[] temp = new GameObject[raycastSparks.Length + 1];
                for (int i = 0; i < raycastSparks.Length; i++)
                {
                    temp[i] = raycastSparks[i];
                }
                temp[raycastSparks.Length] = VRCInstantiate(terrainRaycastSpark);
                temp[raycastSparks.Length].SetActive(true);
                raycastSparks = temp;
            }
        }
    }

    private void Update()
    {

        if (AmmoCount > 0 && Display != null)
        {
            Display.text = (AmmoCount + " / " + MaxAmmo);
        }


        if (AmmoCount <= 0 && AmmoCheck && reloadOnMagEmpty)
        {
            Reload();
            Firing = false;
            AmmoCheck = false;
        }


        if (Firing == true)
        {
            if (Time.time - firedTime > CycleTime)
            {
                Fire();
                if(fireSelection == fireSelection.Burst)
                {
                    currentBurstCount--;
                    if(currentBurstCount == 1)
                    {
                        Firing = false;
                    }
                }
            }
        }
        if(pickup)
        {
            if (Input.GetKey(KeyCode.Q) && !reloading  && pickup.IsHeld && localPlayer == Networking.GetOwner(gameObject))
            {
                reloading = true;
                Reload();
            }
        }

        
        
        //TODO all the seperateTimers for reload
        if (reloading)
        {

            /*
            if (Time.time - reloadTimer > reloadTime)
            {
                AmmoCount = MaxAmmo;
                AmmoCheck = true;
            }*/

            //timer to wait for the mag pull audio to finish
            if (!magPulled)
            {
                if (Time.time - magPullTimer > MagPull.length)
                {
                    magPulled = true;
                    magInserted = false;
                    
                }
            }

            //magInsert timer

            //check if the maginserttimestamp has passed
            if(Time.time > magInsertTimeStamp)
            {
                if (!magInserted)
                {
                    magInserted = true;
                    magInsertTimer = MagInsert.length;
                    if (MagInsert && magazineAudioSource)
                    {
                        magazineAudioSource.PlayOneShot(MagInsert);
                        Debug.Log("mag insert audio played");
                    }
                }
                if (magPulled &&magInserted)
                {
                    if (magInsertTimer > 0)
                    {
                        magInsertTimer -= Time.deltaTime;
                    }
                    else
                    {
                        magPulled = false;
                        if (!bulletInChamber)
                        {
                            //timer over, play the mag gun cock;
                            if(GunCock && fireMechanismAudioSource&&!gunCocked)
                            {
                                fireMechanismAudioSource.PlayOneShot(GunCock);
                                Debug.Log("gun cock audio played");
                                gunCocked = true;
                                bulletInChamber = true;
                            }

                        }
                        else
                        {
                            reloading = false;
                            if (bulletInChamberAddsToAmmoCount)
                            {
                                AmmoCount = MaxAmmo + 1;
                            }
                            else
                            {
                                AmmoCount = MaxAmmo;
                            }

                        }

                    }

                }
            }
            
           
            //gun cock timer
            if(magInserted && !magPulled)
            {
                if(gunCockTimer>0)
                {
                    gunCockTimer -= Time.deltaTime;
                }
                else
                {
                    if(!bulletInChamber)
                    {
                        bulletInChamber = true;
                        //set ammo to max
                        AmmoCount = MaxAmmo;
                    }
                    reloading = false;
                }
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
        if(secondaryGripPickup)
        {
            secondaryGripScript = secondaryGripPickup.GetComponent<GripScript>();
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
            Networking.SetOwner(localPlayer, secondaryGripPickup.gameObject);
            if(secondaryGripPickup.IsHeld)
            {
                secondaryGripScript.DetachGunFromSelf();
            }
            else
            {
                secondaryGripScript.AttachSelfToGun();
            }
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
            
            if (secondaryGripPickup.IsHeld)
            {
                secondaryGripScript.AttachGunToSelf();
            }
            else
            {
                secondaryGripScript.AttachSelfToGun();
            }
        }
        RequestSerialization();
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
        else
        {
            //ammocount is empty
            bulletInChamber = false;
            if(reloadOnMagEmpty)
            {
                Reload();
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
        if(AmmoCount > 0)
        {
            bulletInChamber = true;
        }
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
        Debug.Log("Reloading");
        reloading = true;
        AdjustAudioSourceTiming();
        
        //set the reload audio timers
        if (MagInsert != null)
        {
            magInsertTimer = MagInsert.length;
            if(GunCock != null)
            {
                magInsertTimeStamp = Time.time + reloadTime - MagInsert.length - GunCock.length;
            }
        }
        if (GunCock != null)
        {
            gunCockTimer = GunCock.length;
        }
        if (MagPull != null)
        {
            magPullTimer = MagPull.length;
        }
        //set the reload bools to false
        magPulled = false;
        magInserted = false;
        gunCocked = false;
        //play the mag pull sound first
        if (MagPull != null && magazineAudioSource)
        {
            magazineAudioSource.PlayOneShot(MagPull);
            Debug.Log("MagPull audio");
        }
        //send changes to network
        if (localPlayer.IsOwner(gameObject))
        {
            RequestSerialization();
        }
    }

    private void AdjustAudioSourceTiming()
    {
        //add up the durations of the reloading audio clips, and compare it to the reload time
        float reloadAudioDuration = 0;
        if (MagPull != null)
        {
            reloadAudioDuration += MagPull.length;
        }
        if (MagInsert != null)
        {
            reloadAudioDuration += MagInsert.length;
        }
        if (GunCock != null)
        {
            reloadAudioDuration += GunCock.length;
        }

        if (reloadAudioDuration > reloadTime)
        {
            //if the audio is longer than the reload time, then we need to speed up the audio
            float speedUpFactor = reloadAudioDuration / reloadTime;
            if (MagPull != null)
            {
                MagPull = AudioClip.Create(MagPull.name, (int)(MagPull.samples / speedUpFactor), MagPull.channels, MagPull.frequency, false);
            }
            if (MagInsert != null)
            {
                MagInsert = AudioClip.Create(MagInsert.name, (int)(MagInsert.samples / speedUpFactor), MagInsert.channels, MagInsert.frequency, false);
            }
            if (GunCock != null)
            {
                GunCock = AudioClip.Create(GunCock.name, (int)(GunCock.samples / speedUpFactor), GunCock.channels, GunCock.frequency, false);
            }
        }
        else
        {
            //ensure the audio clips are their default length
            
        }
    }
}
