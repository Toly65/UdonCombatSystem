
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class SpeedLimiter : UdonSharpBehaviour
{
    public float maxSpeed;
    private Rigidbody body;
    private void FixedUpdate()
    {
        if (body.velocity.magnitude > maxSpeed)
        {
            body.velocity = Vector3.ClampMagnitude(body.velocity, maxSpeed);
        }
    }

}
