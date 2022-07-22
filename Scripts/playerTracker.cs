
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class playerTracker : UdonSharpBehaviour
{
    private VRCPlayerApi localplayer;

    private void Update()
    {
        transform.position = localplayer.GetPosition();
        transform.rotation = localplayer.GetRotation();
    }
}
