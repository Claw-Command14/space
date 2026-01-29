// Inspiration from Goob, coded by DeKwadrat22 with copious amount of help (FE4R helped mentally)

using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Log;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Utility;

namespace Content.Shared.Multishot;

/// <summary>
/// System for managing multishot behavior where two non-wielded weapons can fire simultaneously.
/// </summary>
public sealed class MultishotSystem : EntitySystem
{
    private readonly HashSet<EntityUid> _processing = new();
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly INetManager _netManager = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        _sawmill = Logger.GetSawmill("multishot");
        SubscribeLocalEvent<MultishotComponent, GunShotEvent>(OnGunShot);
    }

    /// <summary>
    /// When a multishot weapon fires, apply accuracy bonus and fire its paired weapon.
    /// </summary>
    private void OnGunShot(Entity<MultishotComponent> ent, ref GunShotEvent args)
    {
        if (ent.Comp.PairedWeapon is null)
            return;

        var thisGun = ent.Owner;
        var paired = ent.Comp.PairedWeapon.Value;

        // Reentrancy guard: if we're already processing this gun, skip to avoid recursion.
        if (_processing.Contains(thisGun))
            return;

        try
        {
            _processing.Add(thisGun);

            // Ensure paired still exists and has a gun component
            if (!TryComp<GunComponent>(paired, out var pairedGun))
                return;

            // Prevent the paired gun handler from re-triggering us
            if (_processing.Contains(paired))
                return;

            _processing.Add(paired);

            // Attempt to shoot the paired weapon as if the same user fired it.
            // Use the original gun's ShootCoordinates if available to maintain aim direction.
            EntityCoordinates toCoordinates;
            if (TryComp(thisGun, out GunComponent? thisGunComp) && thisGunComp.ShootCoordinates is not null)
            {
                toCoordinates = thisGunComp.ShootCoordinates.Value;
            }
            else
            {
                toCoordinates = new EntityCoordinates(thisGun, pairedGun.DefaultDirection);
            }

            _gunSystem.AttemptShoot(args.User, paired, pairedGun, toCoordinates);

            // Refetch the component to get updated state
            if (TryComp<GunComponent>(paired, out var updatedPairedGun))
            {
                // Dirty should already have been called in AttemptShoot, but call it again to be sure
                Dirty(paired, updatedPairedGun);

                // Force appearance update for ammo counter
                // Check if it has a ballistic provider
                if (TryComp<BallisticAmmoProviderComponent>(paired, out var ballisticComp))
                {
                    if (TryComp<AppearanceComponent>(paired, out var appearanceComp))
                    {
                        _appearance.SetData(paired, AmmoVisuals.AmmoCount, ballisticComp.Count, appearanceComp);
                        _appearance.SetData(paired, AmmoVisuals.AmmoMax, ballisticComp.Capacity, appearanceComp);
                        // Mark appearance dirty to sync to all clients
                        Dirty(paired, appearanceComp);
                    }
                    Dirty(paired, ballisticComp);
                }
            }

            // Ensure the client updates its ammo UI for the paired weapon immediately.
            // Only the server should raise this event for clients.

            // Not working :(
            if (_netManager.IsServer)
            {
                var updateClientAmmoEvent = new UpdateClientAmmoEvent();
                RaiseLocalEvent(paired, ref updateClientAmmoEvent);
            }
        }
        finally
        {
            _processing.Remove(ent.Owner);
            if (ent.Comp.PairedWeapon is not null)
                _processing.Remove(ent.Comp.PairedWeapon.Value);
        }
    }

    /// <summary>
    /// Pair two weapons for multishot when both are equipped on a player with a lit holo-cigar.
    /// </summary>
    public void PairWeapons(EntityUid weapon1, EntityUid weapon2, EntityUid owner)
    {
        if (weapon1 == weapon2)
        {
            _sawmill.Warning($"Attempted to pair same weapon {weapon1}");
            return;
        }

        var comp1 = EnsureComp<MultishotComponent>(weapon1);
        var comp2 = EnsureComp<MultishotComponent>(weapon2);

        comp1.PairedWeapon = weapon2;
        comp1.WeaponOwner = owner;

        comp2.PairedWeapon = weapon1;
        comp2.WeaponOwner = owner;

        Dirty(weapon1, comp1);
        Dirty(weapon2, comp2);

        _sawmill.Info($"Paired weapons {weapon1} <-> {weapon2} for owner {owner}");
    }

    /// <summary>
    /// Remove multishot pairing from a weapon.
    /// </summary>
    public void UnpairWeapon(EntityUid weapon)
    {
        if (!TryComp<MultishotComponent>(weapon, out var comp))
            return;

        var pairedWeapon = comp.PairedWeapon;

        RemComp<MultishotComponent>(weapon);

        // Also unpair the paired weapon if it exists
        if (pairedWeapon is not null && TryComp<MultishotComponent>(pairedWeapon.Value, out var pairedComp))
        {
            RemComp<MultishotComponent>(pairedWeapon.Value);
            _sawmill.Info($"Unpaired weapons {weapon} and {pairedWeapon}");
        }
    }

    /// <summary>
    /// Apply accuracy bonus to a shot from a multishot weapon.
    /// </summary>
    public float GetAccuracyModifier(EntityUid weapon)
    {
        if (HasComp<MultishotComponent>(weapon))
        {
            return MultishotComponent.AccuracyBonus;
        }

        return 1.0f;
    }
}
