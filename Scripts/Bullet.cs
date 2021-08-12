
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class Bullet : UdonSharpBehaviour
{
    //public configuarble variables
    public bool sendTriggerMessage = false;
    private ContactPoint contact;
    public GameObject BulletSpark;
    //public Explosive explodeScript;
    private UdonBehaviour Target;
    public float StartDamage = 25f;
    public float MinDamage = 10f;
    private float totalDamage;
    public float lifetime = 15.0f;
    public int BleedTimeToAdd = 1;
    public float BleedDamageToAdd = 5f;
    public bool Rocket;
    public float rocketPropulsion;
    public float PropulsionTime;
    public float RotationSpeed;
    public GameObject SpawningGun;

    //private calculation variables
    private float currentVel;
    private float vpercent;
    [HideInInspector] public float MaxVelocity;
    private Rigidbody rb;
    private float currentTime;
    private float wantedTime;
    private bool isFirst = true;

    //pivate collison raycasting detection variables
    private LayerMask layerMask = 10; //make sure we aren't in this layer
    private float skinWidth = 0.1f; //probably doesn't need to be changed
    private float minimumExtent;
    private float partialExtent;
    private float sqrMinimumExtent;
    private Vector3 previousPosition;
    private Rigidbody myRigidbody;
    private Collider myCollider;

    private void Start()
    {
        Destroy(gameObject, lifetime);
        rb = (Rigidbody)gameObject.GetComponent(typeof(Rigidbody));
        currentTime = Time.time;
        wantedTime = currentTime + 0.01f;


        myRigidbody = GetComponent<Rigidbody>();
        myCollider = GetComponent<Collider>();
        previousPosition = myRigidbody.position;
        minimumExtent = Mathf.Min(Mathf.Min(myCollider.bounds.extents.x, myCollider.bounds.extents.y), myCollider.bounds.extents.z);
        partialExtent = minimumExtent * (1.0f - skinWidth);
        sqrMinimumExtent = minimumExtent * minimumExtent;
    }

    private void Update()
    {
        currentTime += Time.deltaTime;

        if (currentTime >= wantedTime && isFirst)
        {
            MaxVelocity = rb.velocity.magnitude;
            isFirst = false;
        }
        if (!(currentTime >= wantedTime) || isFirst) return;
        currentVel = rb.velocity.magnitude;
        vpercent = (currentVel / MaxVelocity);
        totalDamage = StartDamage * vpercent;

        if (totalDamage < MinDamage)
        {
            totalDamage = MinDamage;
        }

        
    }

    void FixedUpdate()
    {
        //have we moved more than our minimum extent?
        Vector3 movementThisStep = myRigidbody.position - previousPosition;
        float movementSqrMagnitude = movementThisStep.sqrMagnitude;

        if (movementSqrMagnitude > sqrMinimumExtent)
        {
            float movementMagnitude = Mathf.Sqrt(movementSqrMagnitude);
            RaycastHit hitInfo;

            //check for obstructions we might have missed
            if (Physics.Raycast(previousPosition, movementThisStep, out hitInfo, movementMagnitude, layerMask.value))
            {
                if (!hitInfo.collider)
                    return;

                if (hitInfo.collider.isTrigger)
                    BulletStuff(hitInfo.collider.gameObject);
                // BulletStuff(PlayerRoot);

                if (!hitInfo.collider.isTrigger)
                    myRigidbody.position = hitInfo.point - (movementThisStep / movementMagnitude) * partialExtent;
            }
        }

        previousPosition = myRigidbody.position;
    }

    private void OnCollisionEnter(Collision other)
    {
        var otherObject = other.gameObject;
        BulletStuff(otherObject);
        contact = other.GetContact(0);
        Debug.Log("collisionEntered");
        
        var hit = VRCInstantiate(BulletSpark);
        hit.transform.position = transform.position;
        hit.transform.position = contact.point;
        hit.SetActive(true);
        
        
        Destroy(gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        Debug.Log("triggerEntered");
        var otherObject = other.gameObject;
        BulletStuff(otherObject);
    } 
    private void BulletStuff(GameObject otherObject)
    {
        //var point = other.ClosestPoint();
        Debug.Log("oh look, a hit");
        //var hit = VRCInstantiate(BulletSpark);
        //hit.transform.position = contact.point;


        
        if ((UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour)) == null)
        {
            //TrueParent.gameObject.SetActive(false);
            Debug.Log("Trigger");
            Debug.Log("collided with: " + otherObject.name);
        }
        else
        {
            if(Networking.GetOwner(gameObject)==Networking.GetOwner(SpawningGun))
            {
                Target = (UdonBehaviour)otherObject.GetComponent(typeof(UdonBehaviour));
                Debug.Log("udon collision with " + Target.name);
                Target.SetProgramVariable("Modifier", (totalDamage * -1));
                Target.SendCustomEvent("ModifyHealth");
            }
            

            /*
            BuffManagerTarget.SetProgramVariable("isBleeding", true);
            int BleedTime = (int)BuffManagerTarget.GetProgramVariable("BleedTime");
            BleedTime += BleedTimeToAdd;
            BuffManagerTarget.SetProgramVariable("BleedTime", BleedTime);

            float BleedDamage = (float)BuffManagerTarget.GetProgramVariable("BleedDamage");
            BleedDamage += BleedDamageToAdd;
            BuffManagerTarget.SetProgramVariable("BleedDamage", BleedDamage);

            Debug.Log("Bullet did: " + totalDamage);*/

            
        }

        Destroy(gameObject);
        
    }

}
