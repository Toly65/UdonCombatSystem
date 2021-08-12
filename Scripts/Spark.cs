
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

[AddComponentMenu("")]
public class Spark : UdonSharpBehaviour
{
    public float Lifetime = 1f;
    private void Start()
    {
        Destroy(gameObject, Lifetime);
    }
}
