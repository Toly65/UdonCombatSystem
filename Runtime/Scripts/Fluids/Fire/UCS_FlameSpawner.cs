
// =============================================================================
// UCS_FlameSpawner — DRAFT, owner-gated ignition source.
//
// Acts like a match / lighter / muzzle flash. Only the owner of authorityObject
// (the lighter pickup, the thrown molotov, etc.) drives the ignite event. All
// remotes see the result via the manager's network sync.
// =============================================================================

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

public class UCS_FlameSpawner : UdonSharpBehaviour
{
    [Tooltip("The object whose owner authoritatively decides ignition. " +
             "Wire to the pickup / rigidbody holder. If null, falls back to this GameObject.")]
    public GameObject authorityObject;

    private void OnTriggerEnter(Collider other)
    {
        AttemptIgniteContact(other);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null) AttemptIgniteContact(collision.collider);
    }

    private void AttemptIgniteContact(Component contact)
    {
        if (contact == null) return;

        GameObject authority = authorityObject != null ? authorityObject : gameObject;
        if (!Networking.IsOwner(authority)) return;

        UCS_FluidPool pool = contact.GetComponentInParent<UCS_FluidPool>();
        if (pool != null)
        {
            pool.Ignite();
            return;
        }

        UCS_FluidTrail trail = contact.GetComponentInParent<UCS_FluidTrail>();
        if (trail != null)
        {
            trail.IgniteTrackedPools();
            return;
        }

        UCS_FluidSpout spout = contact.GetComponentInParent<UCS_FluidSpout>();
        if (spout != null)
        {
            spout.AttemptIgniteFlammableOutput();
        }
    }
}
