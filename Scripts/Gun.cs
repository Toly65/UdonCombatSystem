using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;
[UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
[AddComponentMenu("")]
public class Gun : UdonSharpBehaviour
{
    [Header("general settings")]
    public bool infiniteAmmo;
    public float maxDistance;
    public float RaycastDamageAmount;
    public VRC_Pickup pickup;
    public bool shotgun = false;
    public int pelletCount;
    public bool Automatic = false;
    public Transform firePosition;
    public float fireVelocity = 5f;
    [UdonSynced] public int AmmoCount = 5;
    public float reloadTime = 15f;
    public float BulletSpread = 10f;
    public bool automaticReload = true;
    [Tooltip("gunshot, magpull, maginsert, gun cock")]
    public AudioClip[] clips = new AudioClip[4];
    [Header("raycast settings")]
    public bool RaycastDamage = true;
    public bool RaycastBulletDrop;
    public bool desktopFaceFiring;
    public GameObject playerRaycastSpark;
    public GameObject terrainRaycastSpark;
    public LayerMask sparkable;
    public LayerMask players;
    [Header("Rigibody projectile, requires raycast to be off")]
    public GameObject bullet;
    [Header("physics stuff, only add if you're putting this on a vehicle")]
    public Rigidbody targetRigidbody;
    public float forceFromBarrel;
    [Header("addons")]
    
    public Text Display;
    public GameObject secondGrip;
    public ScopeManager scope;
    public HUDAmmoCount ammoCountHud;

    [HideInInspector]public int MaxAmmo;
    private bool startTimer = false;
    private float currentTime;
    private float wantedTime;
    [HideInInspector] public bool AmmoCheck = true;
    public AudioSource audio;
    private bool isreloadingaudio = true;
    private VRCPlayerApi localPlayer;
    private bool DesktopUser;
   // [UdonSynced]private VRCPlayerApi owner;

    public Animator GunAnimator;
    public AnimationClip CycleAnimation;
    public AnimationClip ReloadAnimation;
    public ParticleSystem ShellParticle;
    [Header("particles are visual only")]
    public ParticleSystem gunShotParticle;

    private float firedTime;
    public bool BaseCycleOffAnimation;
    public float CycleTime;

    private string AnimName;
    private bool Firing = false;
    private bool Scoped;

    public void TriggerPull()
    {
        if (Automatic == true)
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetFireTrue");
        }
        else
        {
            SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "Fire");
        }
    }
    public void OnDisable()
    {
        AmmoCount = MaxAmmo;
    }
    public void TriggerRelease()
    {
        SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All, "SetFireFalse");
    }

    public void SetFireTrue()
    {
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
                firedTime = Time.time;
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

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;

        //audio = (AudioSource)gameObject.GetComponent(typeof(AudioSource));
        if (!localPlayer.IsUserInVR())
        {
            DesktopUser = true;
        }
        else
        {
            DesktopUser = false;
        }

        MaxAmmo = AmmoCount;
        Debug.Log("Ammo Left: " + AmmoCount);

        


        if (BaseCycleOffAnimation&&CycleAnimation)
        {
            CycleTime = CycleAnimation.length;
        }

        if (scope != null)
        {
            Scoped = true;
        }
    }
    public void Pickup()
    {
        //owner = localPlayer;
        if (Scoped)
        {
            scope.ManageScope();
        }
        if(secondGrip != null)
        {
            secondGrip.SetActive(true);
        }
        if(ammoCountHud)
        {

        }
    }

    public void Drop()
    {
        if(secondGrip!= null)
        {
            secondGrip.SetActive(false);
        }
        RequestSerialization();
    }
    private void FixedUpdate()
    {
        if (AmmoCount > 0 && Display != null)
        {
            Display.text = (AmmoCount + " / " + MaxAmmo);
        }
        

        if (AmmoCount <= 0 && AmmoCheck && localPlayer == Networking.GetOwner(gameObject)&&automaticReload)
        {
            Reload();
            AmmoCheck = false;
        }

        if (!startTimer) return;
        if (currentTime > wantedTime - 0.7 && isreloadingaudio)
        {
            if (clips[2] != null&& audio)
            {
                audio.PlayOneShot(clips[2]);
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
            if (clips[3] != null&& audio)
            {
                audio.PlayOneShot(clips[3]);
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
        
        
        if (Time.time - firedTime < CycleTime && !Automatic)
        {
            Debug.Log("fired too fast");
            return;
        }
        firedTime = Time.time;
        if (infiniteAmmo)
        {
            AmmoCount++;
        }
        if (AmmoCount > 0)
        {

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
                    if (clips[0] != null&&audio)
                    {
                        audio.PlayOneShot(clips[0]);
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

                    BulletRaycast();
                    

                    if (GunAnimator && CycleAnimation)
                    {
                        GunAnimator.Play("Base Layer." + CycleAnimation.name, 0, 0.25f);
                    }

                    AmmoCount--;
                    if (clips[0] != null&&audio)
                    {
                        audio.PlayOneShot(clips[0]);
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
                    if (clips[0] != null && audio)
                    {
                        audio.PlayOneShot(clips[0]);
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
                    if (clips[0] != null && audio)
                    {
                        audio.PlayOneShot(clips[0]);
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



    public void Reload()
    { 
        if (Display != null)
        {
            Display.text = "Reloading";
        }

        if (clips[1] != null && audio)
        {
            audio.PlayOneShot(clips[1]);
        }

        if(ReloadAnimation&&GunAnimator)
        {
                GunAnimator.Play("Base Layer." + ReloadAnimation.name, 0, 0.25f);   
        }
        // Debug.Log("Reloading");
        currentTime = Time.time;
        wantedTime = currentTime + reloadTime;
        startTimer = true;
    }
}
