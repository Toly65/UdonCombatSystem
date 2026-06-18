
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;
using VRC.SDK3.Components;

public class UCS_MagBeltTrigger : UdonSharpBehaviour
{
    [SerializeField] private float holsterReturnDistance = 0.05f;
    [SerializeField] private UCS_MagBelt magBelt;

    private float GetScaledHolsterReturnDistance()
    {
        VRCPlayerApi localPlayer = Networking.LocalPlayer;
        if (localPlayer == null)
        {
            return holsterReturnDistance;
        }

        float eyeHeight = localPlayer.GetAvatarEyeHeightAsMeters();
        if (eyeHeight <= 0.01f || float.IsNaN(eyeHeight) || float.IsInfinity(eyeHeight))
        {
            return holsterReturnDistance;
        }

        return holsterReturnDistance * eyeHeight;
    }

    public void OnTriggerEnter(Collider other)
    {
        if (magBelt == null || other == null)
        {
            return;
        }

        // The collider will be a child of the mag prefab; always look in parents.
        UCS_Mag mag = other.GetComponentInParent<UCS_Mag>();

        if (mag == null)
        {
            return;
        }

        VRC_Pickup pickup = mag.GetComponent<VRC_Pickup>();
        if (pickup == null)
        {
            pickup = mag.GetComponentInChildren<VRC_Pickup>();
        }

        if (pickup != null && pickup.IsHeld)
        {
            float scaledHolsterReturnDistance = GetScaledHolsterReturnDistance();

            if (magBelt.IsMagCloseToHolster(mag, scaledHolsterReturnDistance))
            {
                // Don't force a drop. Instead mark this mag so when the player drops it
                // it will be returned to the belt. This only applies when the mag is
                // actually close to the holster point.
                mag.SetReturnToPool(mag.GetMagPool(), magBelt.GetMagazineHolsterPoint(), scaledHolsterReturnDistance);
            }
            else
            {
                mag.ClearReturnToPool();
            }

            return;
        }

        if (magBelt.IsMagCloseToHolster(mag, GetScaledHolsterReturnDistance()))
        {
            magBelt.ReturnMagToBelt(mag);
        }
    }

    public void OnTriggerExit(Collider other)
    {
        if (magBelt == null || other == null)
        {
            return;
        }

        // The collider will be a child of the mag prefab; always look in parents.
        UCS_Mag mag = other.GetComponentInParent<UCS_Mag>();

        if (mag == null)
        {
            return;
        }

        // Clear any return-to-belt marking so the mag won't be auto-returned when dropped.
        mag.ClearReturnToPool();
    }
}
