using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

[AddComponentMenu("")]
public class Gun : UdonSharpBehaviour
{
    //projectiles will come in future, maybe...
    public bool RaycastDamage = true;
    public Transform LocalBulletPool;

    public float maxDistance;
    public float RaycastDamageAmount;
    //public LayerMask hitboxLayer;
    public GameObject RaycastSpark;
    public bool shotgun = false;
    public int pelletCount;
    public bool Automatic = false;
    public GameObject bullet;
    public Transform firePosition;
    public float fireVelocity = 5f;
    public int AmmoCount = 5;
    public float reloadTime = 15f;
    public AudioClip[] clips = new AudioClip[4];
    public Text Display;
    public float BulletSpread = 10f;
    public LayerMask sparkable;
    public LayerMask players;
    public VRC_Pickup pickup;

    public ScopeManager scope;

    private int MaxAmmo;
    private bool startTimer = false;
    private float currentTime;
    private float wantedTime;
    private bool AmmoCheck = true;
    private new AudioSource audio;
    private bool isreloadingaudio = true;
    private VRCPlayerApi localPlayer;

    public Animator GunAnimator;
    public AnimationClip CycleAnimation;
    public bool UseShellParticle;
    public ParticleSystem ShellParticle;

    private float firedTime;
    public bool BaseCycleOffAnimation;
    public float CycleTime;

    private string AnimName;
    private bool Firing = false;
    private bool Scoped;
    public float recoilForce;
    
    public void OnPickupUseDown()
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

    public void OnPickupUseUp()
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
        
        
    }

    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        audio = (AudioSource)gameObject.GetComponent(typeof(AudioSource));
        MaxAmmo = AmmoCount;
        Debug.Log("Ammo Left: " + AmmoCount);

        AnimName = CycleAnimation.name;

        if(BaseCycleOffAnimation)
        {
            CycleTime = CycleAnimation.length;
        }
        if (shotgun && !RaycastDamage)
        {
            shotgun = false;
            //bad user, no projectile shotguns
        }

        if (scope != null)
        {
            Scoped = true;
        }
    }
    public void OnPickup()
    {
        if(Scoped)
        {
            scope.ManageScope();
        }
    }

    private void FixedUpdate()
    {
        if (AmmoCount > 0)
        {
            Display.text = (AmmoCount.ToString() + " / " + MaxAmmo.ToString());
        }
        else
        {
            Display.text = "Reloading";
        }

        if (AmmoCount <= 0 && AmmoCheck)
        {
            audio.PlayOneShot(clips[1]);
            Reload();
            AmmoCheck = false;
        }

        if (!startTimer) return;
        if (currentTime > wantedTime - 0.7 && isreloadingaudio)
        {
            audio.PlayOneShot(clips[2]);
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
            audio.PlayOneShot(clips[3]);
            isreloadingaudio = true;
            Debug.Log("Reloaded. Ammo now: " + AmmoCount);
            Display.text = (AmmoCount.ToString() + " / " + MaxAmmo.ToString());
        }
    }

    public void Fire()
    {
        
        if (AmmoCount > 0)
        {
            if(RaycastDamage)
            {

                //raycast stuff
                if(shotgun)
                {
                    for (int i = 0; i <= pelletCount; i++)
                    {
                        Quaternion temp = firePosition.rotation;
                        firePosition.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));
                        RaycastHit hit;

                        if (Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, players)&& localPlayer.IsOwner(gameObject))
                        {
                            if (hit.transform.gameObject.name.Contains("hitbox"))
                            {
                                GameObject target = hit.collider.gameObject;
                                UdonBehaviour TargetBehaviour = (UdonBehaviour)target.GetComponent(typeof(UdonBehaviour));
                                TargetBehaviour.SetProgramVariable("Modifier", (RaycastDamageAmount * -1));
                                
                                TargetBehaviour.SendCustomEvent("ModifyHealth");
                              
                                firePosition.rotation = temp;
                            }
                        }
                        if (Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, sparkable))
                        {
                            Debug.Log("raycast hit");
                            GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                            if (hit.collider != null)
                            {
                                if (!hit.collider.isTrigger)
                                {
                                    if(RaycastSpark != null)
                                    {
                                        var spark = VRCInstantiate(RaycastSpark);
                                        spark.transform.position = hit.point;
                                        spark.SetActive(true);
                                        Debug.Log("wall hit");
                                    }
                                    Debug.Log("wall hit");

                                }
                            }
                        }

                        
                    }
                    GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                    AmmoCount--;
                    audio.PlayOneShot(clips[0]);
                    if (UseShellParticle)
                    {
                        ShellParticle.Play();
                    }
                }
                else
                { 
                        RaycastHit hit;

                    if (Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, players) && localPlayer.IsOwner(gameObject))
                    {
                        Debug.Log("player layer hit");
                        if (hit.transform.gameObject.name.Contains("hitbox"))
                        {
                            GameObject target = hit.collider.gameObject;
                            UdonBehaviour TargetBehaviour = (UdonBehaviour)target.GetComponent(typeof(UdonBehaviour));
                            Debug.Log("before Modification");
                            TargetBehaviour.SetProgramVariable("Modifier", (RaycastDamageAmount * -1));
                            Debug.Log("aftermodification");
                            TargetBehaviour.SendCustomEvent("ModifyHealth");
                            
                        }
                    }
                    if (Physics.Raycast(firePosition.position, firePosition.forward, out hit, maxDistance, sparkable))
                    {
                        Debug.Log("raycast hit");
                        GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                        if (hit.collider != null)
                        {
                            if (!hit.collider.isTrigger)
                            {
                                var spark = VRCInstantiate(RaycastSpark);
                                spark.transform.position = hit.point;
                                spark.SetActive(true);
                                Debug.Log("wall hit");

                            }
                        }
                    }
                   
                    GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                    AmmoCount--;
                    audio.PlayOneShot(clips[0]);
                    if (UseShellParticle)
                    {
                        ShellParticle.Play();
                    }
                }
               


            }
            else
            {

                //projectile stuff
                if (shotgun == false)
                {
                    GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);

                    //bullet shooting
                    //TODO replace instantiation with localised object pools

                    var bul = VRCInstantiate(bullet);

                    
                    if(bul != gameObject)
                    {
                        bul.transform.SetParent(null);
                        bul.SetActive(true);
                        bul.transform.SetPositionAndRotation(firePosition.position, firePosition.rotation);

                        bul.transform.Rotate(Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread), Random.Range(-BulletSpread, BulletSpread));

                        var bulletrb = (Rigidbody)bul.GetComponent(typeof(Rigidbody));
                        bulletrb.velocity = (bul.transform.forward) * fireVelocity;
                        AmmoCount--;
                        audio.PlayOneShot(clips[0]);

                        if (UseShellParticle)
                        {
                            ShellParticle.Play();
                        }
                    }else
                    {
                        Debug.Log("bullet not available");
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
                    GunAnimator.Play("Base Layer." + AnimName, 0, 0.25f);
                    AmmoCount--;
                    audio.PlayOneShot(clips[0]);
                    if (UseShellParticle)
                    {
                        ShellParticle.Play();
                    }
                }
            }
            if (!localPlayer.IsPlayerGrounded())//only does anything if in the air.
            {
                if(Networking.IsOwner(gameObject))
                {
                    Vector3 PlayerVel = localPlayer.GetVelocity();
                    localPlayer.SetVelocity(PlayerVel - firePosition.forward * recoilForce);
                }
                
            }
        }
        //Debug.Log("Ammo Left: " + AmmoCount);
    }

    

    private void Reload()
    {
       // Debug.Log("Reloading");
        currentTime = Time.time;
        wantedTime = currentTime + reloadTime;
        startTimer = true;
    }

   
}
