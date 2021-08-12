
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class GripScript : UdonSharpBehaviour
{
    private Rigidbody body;
    private void Start()
    {
        body = gameObject.GetComponent<Rigidbody>();
    }

    private void OnDrop()
    {
        body.isKinematic = false;
    }

    private void OnPickup()
    {
        body.isKinematic = true;
    }
}
