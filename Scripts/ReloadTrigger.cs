
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class ReloadTrigger : UdonSharpBehaviour
{

    public Gun gun;
    public void Interact()
    {

        if(gun.clips[1] != null)
        {
            gun.GetComponent<AudioSource>().PlayOneShot(gun.clips[1]);
        }
        gun.SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget.All,"Reload");
        gun.AmmoCheck = false;
    }
}
