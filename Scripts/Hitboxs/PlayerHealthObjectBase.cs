
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class PlayerHealthObjectBase : UdonSharpBehaviour
{
    [UdonSynced] public float health = 100f;
    [SerializeField] public float maxHealth = 100f;
}
