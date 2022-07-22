
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ImprovedGun : UdonSharpBehaviour
{
    [Header("Raycast settings")]
    public float damage = 30.0f;
    public float maxDistance = 50.0f;
    public GameObject impactSpark;
    public GameObject hitSpark;
    public LayerMask playerLayer;
    public LayerMask EnvironmentLayer;
    [Header("Projectile settings")]
    public GameObject projectile;
    public float fireVelocity;
    [Header("Weapon settings")]
    [SerializeField] private bool DesktopFaceFiring;
    [SerializeField] private bool useAmmoForEveryShot;
    [SerializeField] private bool useAmmo;
    public int rateOfFire = 60;
    public int maxAmmo;
    public int shotsPerTrigger;
    public Transform firePosition;
    public GameObject frontGrip;

    [Tooltip("recoil")]
    public float recoilMaxAngle = 7.5f;
    public float recoilGrowth = 0.5f;
    public float recoilAttackStep = 1f;
    public float recoilReleaseStep = 0.05f;
    public float recoilStayTimeMultiplier = 2f;
    public float firingConeAngle = 1f;
    public float firingConeAngleDesktopZoomMultiplier = 0.5f;
    public float stabilizeAimInterpSpeed = 0.001f;
    [Tooltip("this value is mainly for shotguns")]
    public float bulletSpread;

    [Space(20f)]
    

    [Space(20f)]

    [Header("Objects")]
    public Animator animator;
    public string AnimName;
    public AudioClip audioClipReload;
    public AudioClip audioClipGunShot;
    public AudioClip audioClipSelectorSwitch;
    
    public ParticleSystem muzzleFlash;
    [Tooltip("Optional")]
    public ParticleSystem bulletDischarge;
    [Tooltip("Optional")]
    public Transform fireSelectorSwitch;
    [Tooltip("Optional")]
    public ScopeManager scope;

    [Header("Debugging")]

    // desktop weapon hold handling
    private const uint HoldStateDesktopLowReady = 0;
    private const uint HoldStateDesktopHighReady = 1;
    private const uint HoldStateDesktopScopedIn = 2;

    [SerializeField]
    private uint desktopHoldState = HoldStateDesktopHighReady;
    private Vector3 safeAngles = new Vector3(45, -90, 0);

    // fire handling
    private const uint FireSelectSafe = 0;
    private const uint FireSelectSingle = 1;
    private const uint FireSelectAuto = 2;

    private uint fireSelect = FireSelectSingle;
    private int lastFireTime_ms;
    private int rateOfFire_ms;

    private bool autoFire = false;
    private bool stabilizeAim = false;
    private Quaternion stabilizeAimDestination = Quaternion.identity;
    private Quaternion stabilizeAimInterpolation = Quaternion.identity;

    private int vrfireSelectLatch = 0;

    // ammo handling
    [SerializeField]
    private int ammoCount;
    [SerializeField]
    private bool reloading = false;

    // recoil handling
    [SerializeField]
    private float recoilDuration = 0f;
    private Quaternion recoilOffsetRotation = Quaternion.identity;
    // magnification handling
    //private magnif

    // extracted components
    private VRC_Pickup pickup;
    private VRC_Pickup frontPickup;
    private AudioSource audioSource;
    private Camera cameraBeh;

    // original positions
    private Vector3 originPosition;
    private Quaternion originRotation;
    private Vector3 originRotationPointPosition;
    private Vector3 frontGripPosition;
    private Quaternion frontGripRotation;


    private VRCPlayerApi localPlayer;
    private bool desktopUser;

    public void HitScan()
    {
        for (int i = 0; i < shotsPerTrigger; i++)
        {
            Transform tempFirePos = firePosition;
            if(DesktopFaceFiring && desktopUser && Networking.IsOwner(gameObject))
            {
                tempFirePos.SetPositionAndRotation(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position, localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            }
            Quaternion firePosOriginalRotation = tempFirePos.rotation;
            tempFirePos.Rotate(Random.Range(-bulletSpread, bulletSpread), Random.Range(-bulletSpread, bulletSpread), Random.Range(-bulletSpread, bulletSpread));
            RaycastHit hit;
            if (Physics.Raycast(tempFirePos.position, tempFirePos.forward, out hit, maxDistance, playerLayer) && localPlayer.IsOwner(gameObject))
            {
                if (hit.transform.gameObject.name.Contains("hitbox"))
                {
                    GameObject target = hit.collider.gameObject;
                    UdonBehaviour TargetBehaviour = (UdonBehaviour)target.GetComponent(typeof(UdonBehaviour));
                    TargetBehaviour.SetProgramVariable("Modifier", (damage * -1));

                    TargetBehaviour.SendCustomEvent("ModifyHealth");

                    tempFirePos.rotation = firePosOriginalRotation;
                    if (hitSpark != null)
                    {
                        var spark = VRCInstantiate(hitSpark);
                        spark.transform.position = hit.point;
                        spark.SetActive(true);
                        //Debug.Log("wall hit");
                    }
                }
            }
            if (Physics.Raycast(tempFirePos.position, tempFirePos.forward, out hit, maxDistance, EnvironmentLayer))
            {
                Debug.Log("raycast hit");
                animator.Play("Base Layer." + AnimName, 0, 0.25f);
                if (hit.collider != null)
                {
                    if (!hit.collider.isTrigger)
                    {
                        if (impactSpark != null)
                        {
                            var spark = VRCInstantiate(impactSpark);
                            spark.transform.position = hit.point;
                            spark.SetActive(true);
                            //Debug.Log("wall hit");
                        }
                        //Debug.Log("wall hit");

                    }
                }
            }
            if(useAmmoForEveryShot&&useAmmo)
            {
                DeductAmmo();
            }
            
        }
        if(!useAmmoForEveryShot&&useAmmo)
        {
            DeductAmmo();
        }
    }
    public void DeductAmmo()
    {
        ammoCount--;
        if (audioClipGunShot != false)
        {
            audioSource.PlayOneShot(audioClipGunShot);
        }
        if (bulletDischarge != false)
        {
            bulletDischarge.Play();
        }
    }

    public void SpawnProjectile()
    {
        for (int i = 0; i < shotsPerTrigger; i++)
        {

        }
    }
}
