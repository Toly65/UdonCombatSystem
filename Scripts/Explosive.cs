
using UdonSharp;
using UnityEngine;
using UnityEngine.SocialPlatforms;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class Explosive : UdonSharpBehaviour
{
    public float ExplosivePower = 750f;
    public float ExplosiveRange = 5f;
    public float UpwardsModifier = 3f;
    public float playerLaunchModifier = 0.01f;
    public float Damage = 50.0f;
    public float MaxSpeedFromExplosionForPlayer = 80.0f;
    public GameObject ExplosiveViz;
    public AudioSource Audio;
    public AudioClip[] clips = new AudioClip[4];
    private AudioClip ExplosionAudio;
  
    private float distanceFromPlayer;

    public GameObject GunRoot;

    private VRCPlayerApi localPlayer;
    private VRCPlayerApi TrueOwner;
    private void Start()
    {
        localPlayer = Networking.LocalPlayer;
        TrueOwner = Networking.GetOwner(GunRoot);
        
        
        Explode();
    }

    private void Update()
    {
        
    }


    public void Explode()
    {
        
        distanceFromPlayer = Vector3.Distance(transform.position, localPlayer.GetPosition());
       

        
        if (distanceFromPlayer <= 50)
        {
            ExplosionAudio = clips[1];
        }
        else if (distanceFromPlayer <= 250)
        {
            ExplosionAudio = clips[2];
        }
        else
        {
            ExplosionAudio = clips[3];
        }

        if (ExplosiveRange <= 2)
        {
            ExplosionAudio = clips[0];
        }
        localPlayer = Networking.LocalPlayer;
        
        Audio.PlayOneShot(ExplosionAudio);

        var Explosion = VRCInstantiate(ExplosiveViz);
        Explosion.SetActive(true);
        Explosion.transform.position = transform.position;
        Explosion.transform.localScale = new Vector3(ExplosiveRange, ExplosiveRange, ExplosiveRange);


        var colliders = Physics.OverlapSphere(transform.position, ExplosiveRange);

        foreach (var hitCol in colliders)
        {
            if(hitCol != null)
            {
                Debug.Log(hitCol.gameObject.name);
                if (hitCol.gameObject.name.Contains("hitbox") || hitCol.gameObject.name.Contains("[Object]") )
                {

                    bool isVisible = false;

                    RaycastHit hit;
                    if (Physics.Raycast(transform.position, (hitCol.transform.position - transform.position), out hit))
                    {
                        if (hit.collider.gameObject.name.Contains("hitbox") || hit.collider.gameObject.name.Contains("[Object]"))
                        {
                            isVisible = true;
                        }
                    }

                    //case of random rigidbodies
                    if(hit.collider.gameObject.name.Contains("[Object]") && localPlayer == TrueOwner)
                    {
                        //This IF statement breaks when there are no rigidbodies, because unity said fuck you
                        if (isVisible && (Rigidbody)hitCol.GetComponent(typeof(Rigidbody)) != null)
                        {

                            //set ownership of everything that gets moved
                            Networking.SetOwner(TrueOwner, hitCol.gameObject);
                            var rb = (Rigidbody)hitCol.GetComponent(typeof(Rigidbody));
                            rb.AddExplosionForce(ExplosivePower, Explosion.transform.position, ExplosiveRange, UpwardsModifier);

                            //keeping this in incase you want random objects to take damage
                            if ((UdonBehaviour)hitCol.GetComponent(typeof(UdonBehaviour)) != null)
                            {
                                var TargetHealthManager = (UdonBehaviour)hitCol.GetComponent(typeof(UdonBehaviour));

                                var Distance = Vector3.Distance(hitCol.transform.position, transform.position);
                                var totalDamage = Damage / Distance;
                                if (totalDamage > Damage)
                                {
                                    totalDamage = Damage;
                                }

                                if (TargetHealthManager.GetProgramVariable("Modifier") != null)
                                {
                                    TargetHealthManager.SetProgramVariable("Modifier", (totalDamage * -1));
                                    TargetHealthManager.SendCustomEvent("ModifyHealth");
                                }
                            }
                        }
                    }
                    

                    //todo, case of torso and case of legs

                    //the torso handles damage
                    if(isVisible && hitCol.gameObject.name.Contains("torso") && localPlayer == TrueOwner)
                    {
                        if ((UdonBehaviour)hitCol.GetComponent(typeof(UdonBehaviour)) != null&&localPlayer == Networking.GetOwner(GunRoot))
                        {
                            var TargetHealthManager = (UdonBehaviour)hitCol.GetComponent(typeof(UdonBehaviour));

                            var Distance = Vector3.Distance(hitCol.transform.position, transform.position);
                            var totalDamage = Damage / Distance;
                            if (totalDamage > Damage)
                            {
                                totalDamage = Damage;
                            }

                            if (TargetHealthManager.GetProgramVariable("Modifier") != null)
                            {
                                TargetHealthManager.SetProgramVariable("Modifier", (totalDamage * -1));
                                TargetHealthManager.SendCustomEvent("ModifyHealth");
                            }
                        }
                    }
                    //legs handle blast this means that teoretically a player can rocket jump without taking damage

                    //this is done locally by every player in the explosion
                    if(isVisible && hitCol.gameObject.name.Contains("legs"))
                    {
                       // localPlayer.SetVelocity(localPlayer.GetVelocity() + new Vector3(0, UpwardsModifier, 0));
                        localPlayer.SetVelocity((localPlayer.GetPosition() - transform.position)/(localPlayer.GetPosition() - transform.position).magnitude * ExplosivePower * playerLaunchModifier + localPlayer.GetVelocity());
                        if(localPlayer.GetVelocity().magnitude > MaxSpeedFromExplosionForPlayer)
                        {
                            localPlayer.SetVelocity(localPlayer.GetVelocity().normalized * MaxSpeedFromExplosionForPlayer);
                        }
                    }
                }
            }
        }
        Destroy(gameObject, 4.0f);
        //transform.parent.gameObject.SetActive(false);
    }
}
