using UnityEditor;
using UnityEngine;
using VRC.SDKBase;

// Gun checks for Check Setup: the references a gun, its mag socket, mag belt, mag pool and slide need to work together.
// Problems repeated across copies of a pooled gun are folded into one line with a count.
public partial class UCS_SetupValidator
{
    private static Object Ref(SerializedObject so, string field)
    {
        SerializedProperty p = so.FindProperty(field);
        return p == null ? null : p.objectReferenceValue;
    }

    private static T Ref<T>(SerializedObject so, string field) where T : Object
    {
        return Ref(so, field) as T;
    }

    // folds on the problem text without the gun's name, so every copy of one gun shares a line
    private void GunIssue(Level level, UCS_BaseGun gun, string problem, Object target = null)
    {
        Add(level, "Gun '" + gun.name + "': " + problem, target != null ? target : gun, null, null, "gun|" + gun.GetType().Name + "|" + problem);
    }

    private void CheckGuns()
    {
        foreach (UCS_BaseGun gun in FindObjectsOfType<UCS_BaseGun>(true))
        {
            var so = new SerializedObject(gun);

            if (so.FindProperty("multipleBarrels").boolValue)
            {
                SerializedProperty points = so.FindProperty("MuzzlePoints");
                bool ok = points.arraySize > 0;
                for (int i = 0; i < points.arraySize; i++) ok &= points.GetArrayElementAtIndex(i).objectReferenceValue != null;
                if (!ok) GunIssue(Level.Error, gun, "Multiple Barrels is on but Muzzle Points is empty or has blanks.");
            }
            else if (Ref(so, "MuzzlePoint") == null)
            {
                GunIssue(Level.Error, gun, "no Muzzle Point, so shots have nowhere to come from.");
            }

            if (gun.GetComponentInChildren<VRC_Pickup>(true) == null)
                GunIssue(Level.Error, gun, "no VRC Pickup on the gun or its children, so it can't be held.");
            else if (gun.GetComponentInChildren<UCS_PickupEventTransferer>(true) == null)
                GunIssue(Level.Warning, gun, "its pickup has no UCS_PickupEventTransferer, so grab and use events won't reach the gun.");

            if (so.FindProperty("UseProjectiles").boolValue && Ref(so, "ProjectileManager") == null)
                GunIssue(Level.Error, gun, "Use Projectiles is on but there is no Projectile Manager.");
            if (so.FindProperty("RoundsPerMinute").floatValue <= 0f)
                GunIssue(Level.Error, gun, "Rounds Per Minute is 0 or less, so it can never fire.");
            if (Ref(so, "FireSound") != null && Ref(so, "barrelAudioSource") == null)
                GunIssue(Level.Error, gun, "has a Fire Sound but no Barrel Audio Source to play it.");
            else if (Ref(so, "FireSound") == null)
                GunIssue(Level.Warning, gun, "no Fire Sound, so it fires silently.");

            if (gun is UCS_MagFedGun && MagazineSize(so) <= 0)
                GunIssue(Level.Error, gun, "Magazine Size is 0 or less.");

            var complex = gun as UCS_ComplexGun;
            if (complex != null) CheckComplexGun(complex, so);
        }

        CheckMagParts();
    }

    private static int MagazineSize(SerializedObject so)
    {
        SerializedProperty p = so.FindProperty("MagazineSize");
        return p == null ? 0 : p.intValue;
    }

    private void CheckComplexGun(UCS_ComplexGun gun, SerializedObject so)
    {
        bool infiniteMags = so.FindProperty("InfiniteMagazine").boolValue;

        if (Ref(so, "gunPickup") == null)
            GunIssue(Level.Error, gun, "Gun Pickup is not assigned.");

        // mag socket and its anchor point back at this gun
        var socket = Ref<UCS_MagSocket>(so, "magSocket");
        if (socket == null)
        {
            GunIssue(Level.Error, gun, "no Mag Socket, so magazines can't be inserted.");
        }
        else
        {
            var socketObject = new SerializedObject(socket);
            if (Ref(socketObject, "gun") != gun)
                GunIssue(Level.Error, gun, "its Mag Socket's Gun field points at " + (Ref(socketObject, "gun") == null ? "nothing" : "a different gun") + ".", socket);
            var anchor = Ref<UCS_MagSocketAnchor>(socketObject, "magSocketAnchor");
            if (anchor == null)
                GunIssue(Level.Warning, gun, "its Mag Socket has no Mag Socket Anchor.", socket);
            else if (Ref(new SerializedObject(anchor), "magSocket") != socket)
                GunIssue(Level.Error, gun, "its Mag Socket Anchor doesn't point back at the Mag Socket.", anchor);
            if (Ref(socketObject, "magBelt") != Ref(so, "magBelt"))
                GunIssue(Level.Warning, gun, "its Mag Socket and the gun use different Mag Belts.", socket);
        }

        // belt and pool
        var belt = Ref<UCS_MagBelt>(so, "magBelt");
        var pool = Ref<UCS_MagPool>(so, "requiredMagPool");
        if (!infiniteMags)
        {
            // the gun only uses its belt for VR players, so a missing one is a warning, not a broken gun
            if (belt == null) GunIssue(Level.Warning, gun, "no Mag Belt, so VR players have no belt to draw magazines from (or tick Infinite Magazine).");
            if (pool == null) GunIssue(Level.Error, gun, "no Required Mag Pool, so the belt doesn't know which magazines to hand out.");
        }

        if (belt != null)
        {
            var beltObject = new SerializedObject(belt);
            if (Ref(beltObject, "BeltHipBone") == null) GunIssue(Level.Error, gun, "its Mag Belt has no Belt Hip Bone.", belt);
            if (Ref(beltObject, "MagazineHolsterPoint") == null) GunIssue(Level.Error, gun, "its Mag Belt has no Magazine Holster Point.", belt);
            string requested = beltObject.FindProperty("requestedMagTypeId").stringValue;
            if (pool != null && !string.IsNullOrEmpty(requested) && requested != pool.GetMagTypeId())
                GunIssue(Level.Warning, gun, "its Mag Belt asks for mag type '" + requested + "' but the Required Mag Pool makes '" + pool.GetMagTypeId() + "'.", belt);
        }

        if (pool != null && MagazineSize(so) > 0)
        {
            int poolAmmo = new SerializedObject(pool).FindProperty("maxAmmo").intValue;
            if (poolAmmo != MagazineSize(so))
                GunIssue(Level.Warning, gun, "Magazine Size is " + MagazineSize(so) + " but the Required Mag Pool's mags hold " + poolAmmo + ".", pool);
        }

        // slide
        var handler = Ref<UCS_SliderHandler>(so, "sliderHandler");
        if (handler == null)
        {
            if (Ref(so, "slidePhysBone") != null) GunIssue(Level.Error, gun, "has a Slide Phys Bone but no Slider Handler.");
        }
        else
        {
            var handlerObject = new SerializedObject(handler);
            if (Ref(handlerObject, "complexGun") != gun) GunIssue(Level.Error, gun, "its Slider Handler's Complex Gun field doesn't point at this gun.", handler);
            if (Ref(handlerObject, "physBone") != Ref(so, "slidePhysBone")) GunIssue(Level.Warning, gun, "its Slider Handler and the gun use different Slide Phys Bones.", handler);
            var poller = Ref<UCS_PhysbonePoller>(handlerObject, "physbonePoller");
            if (poller == null) GunIssue(Level.Warning, gun, "its Slider Handler has no Physbone Poller.", handler);
            else if (Ref(new SerializedObject(poller), "sliderHandler") != handler) GunIssue(Level.Error, gun, "its Physbone Poller doesn't point back at the Slider Handler.", poller);
            if (Ref(handlerObject, "gunAnimator") == null) GunIssue(Level.Warning, gun, "its Slider Handler has no Gun Animator, so the slide won't animate.", handler);
        }

        // two-handed guns
        foreach (UCS_TwoHandedManager two in gun.GetComponentsInChildren<UCS_TwoHandedManager>(true))
        {
            var twoObject = new SerializedObject(two);
            if (Ref(twoObject, "primaryGripPickup") == null || Ref(twoObject, "secondaryGripPickup") == null)
                GunIssue(Level.Error, gun, "its Two Handed Manager is missing a grip pickup.", two);
            Object twoBelt = Ref(twoObject, "magBelt");
            if (twoBelt != null && twoBelt != belt) GunIssue(Level.Warning, gun, "its Two Handed Manager uses a different Mag Belt than the gun.", two);
        }
    }

    // mag pools, mags and belt triggers, wherever they sit
    private void CheckMagParts()
    {
        foreach (UCS_MagBeltTrigger trigger in FindObjectsOfType<UCS_MagBeltTrigger>(true))
        {
            if (Ref(new SerializedObject(trigger), "magBelt") == null)
                Add(Level.Error, "Mag Belt Trigger '" + trigger.name + "' has no Mag Belt.", trigger, null, null, "trigger");
        }

        foreach (UCS_MagPool pool in FindObjectsOfType<UCS_MagPool>(true))
        {
            var so = new SerializedObject(pool);
            if (string.IsNullOrEmpty(so.FindProperty("magTypeId").stringValue))
                Add(Level.Error, "Mag Pool '" + pool.name + "' has no Mag Type Id.", pool);
            if (Ref(so, "poolExample") == null)
                Add(Level.Error, "Mag Pool '" + pool.name + "' has no Pool Example to copy mags from.", pool);
        }

        foreach (UCS_Mag mag in FindObjectsOfType<UCS_Mag>(true))
        {
            var so = new SerializedObject(mag);
            Object magPool = Ref(so, "magPool");
            UCS_MagPool parentPool = mag.GetComponentInParent<UCS_MagPool>(true);
            if (magPool == null)
                Add(Level.Error, "A mag ('" + mag.name + "') has no Mag Pool, so it can't return to its pool.", mag, null, null, "mag|pool");
            else if (parentPool != null && parentPool != magPool)
                Add(Level.Error, "A mag ('" + mag.name + "') sits under one Mag Pool but points at another.", mag, null, null, "mag|wrongpool");
            if (Ref(so, "magPickupRoot") == null)
                Add(Level.Warning, "A mag ('" + mag.name + "') has no Mag Pickup Root.", mag, null, null, "mag|root");
        }

        foreach (UCS_MagPickup pickup in FindObjectsOfType<UCS_MagPickup>(true))
        {
            var so = new SerializedObject(pickup);
            if (Ref(so, "mag") == null || Ref(so, "magPickup") == null)
                Add(Level.Error, "A mag pickup ('" + pickup.name + "') is missing its Mag or VRC Pickup reference.", pickup, null, null, "magpickup");
        }
    }
}
