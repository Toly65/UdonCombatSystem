
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

//this is a parent class for hitbox management
public class UCS_Hitbox : UdonSharpBehaviour
{
    //colider array this gameobject uses as hitboxes
    public Collider[] hitboxes;

    public virtual void EnableHitboxes()
    {
        foreach (Collider col in hitboxes)
        {
            col.enabled = true;
        }
    }

    public virtual void DisableHitboxes()
    {
        foreach (Collider col in hitboxes)
        {
            col.enabled = false;
        }
    }

    //hit event
    public virtual void HitEvent(int damage)
    {
        //child classes will implement this
    }
}