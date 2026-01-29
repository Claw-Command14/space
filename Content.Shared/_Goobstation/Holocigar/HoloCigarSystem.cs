// Based from Goob, had to be scrapped and remade, coded by DeKwadrat22
// with copious amount of help (FE4R helped mentally)

using Content.Shared.TheManWhoSoldTheWorld;
using Content.Shared.NoWieldNeeded;
using Content.Shared.Multishot;

using Content.Shared.Clothing.Components;
using Content.Shared.Clothing.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Smoking;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Containers;
using Robust.Shared.Log;

namespace Content.Shared.HoloCigar;

/// <summary>
/// This is the system for the Holo-Cigar. Beware, polish coding below.
/// </summary>
public sealed class HoloCigarSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ClothingSystem _clothing = default!;
    [Dependency] private readonly SharedItemSystem _items = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly MultishotSystem _multishot = default!;
    [Dependency] private readonly SharedContainerSystem _containerSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    private ISawmill _sawmill = default!;

    private const string LitPrefix = "lit";
    private const string UnlitPrefix = "unlit";
    private const string MaskSlot = "mask";

    /// <inheritdoc/>o
    public override void Initialize()
    {
        SubscribeLocalEvent<HoloCigarComponent, GetVerbsEvent<AlternativeVerb>>(OnAddInteractVerb);
        SubscribeLocalEvent<HoloCigarComponent, ComponentHandleState>(OnComponentHandleState);

        // Check if sucker has the damned cigar in mounth
        SubscribeLocalEvent<HoloCigarComponent, EntGotInsertedIntoContainerMessage>(OnCigarInserted);
        SubscribeLocalEvent<HoloCigarComponent, EntGotRemovedFromContainerMessage>(OnCigarRemoved);
        SubscribeLocalEvent<HoloCigarAffectedGunComponent, DroppedEvent>(OnDroppedEvent);
        SubscribeLocalEvent<HoloCigarAffectedGunComponent, EntGotInsertedIntoContainerMessage>(OnEntGotInsertedIntoContainer);
        SubscribeLocalEvent<HoloCigarAffectedGunComponent, EntGotRemovedFromContainerMessage>(OnEntGotRemovedFromContainer);

        SubscribeLocalEvent<TheManWhoSoldTheWorldComponent, PickupAttemptEvent>(OnPickupAttempt);
        SubscribeLocalEvent<TheManWhoSoldTheWorldComponent, MapInitEvent>(OnMapInitEvent);
        SubscribeLocalEvent<TheManWhoSoldTheWorldComponent, ComponentShutdown>(OnComponentShutdown);
        SubscribeLocalEvent<TheManWhoSoldTheWorldComponent, MobStateChangedEvent>(OnMobStateChangedEvent);

        _sawmill = Logger.GetSawmill("holocigar");
    }

    private void OnAddInteractVerb(Entity<HoloCigarComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands is null)
            return;

        AlternativeVerb verb = new()
        {
            Act = () =>
            {
                HandleToggle(ent);
                ent.Comp.Lit = !ent.Comp.Lit;
                Dirty(ent);
            },
            Message = Loc.GetString("holo-cigar-verb-desc"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/clock.svg.192dpi.png")),
            Text = Loc.GetString("holo-cigar-verb-text"),
        };

        args.Verbs.Add(verb);
    }

    #region Event Methods

    private void OnMobStateChangedEvent(Entity<TheManWhoSoldTheWorldComponent> ent, ref MobStateChangedEvent args)
    {
        if (!TryComp<HoloCigarComponent>(ent.Comp.HoloCigarEntity, out var holoCigarComponent))
            return;

        if (args.NewMobState == MobState.Dead)
            _audio.Stop(holoCigarComponent.MusicEntity); // no music out of mouth duh

        if (_net.IsServer)
            _audio.PlayPvs(ent.Comp.DeathAudio, ent, AudioParams.Default.WithVolume(3f));
    }

    private void OnComponentShutdown(Entity<TheManWhoSoldTheWorldComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<HoloCigarComponent>(ent.Comp.HoloCigarEntity, out var holoCigarComponent))
            return;

        _audio.Stop(holoCigarComponent.MusicEntity);
        ShutDownEnumerateRemoval(ent);

        if (!ent.Comp.AddedNoWieldNeeded)
            return;

        RemComp<NoWieldNeededComponent>(ent);
        ent.Comp.HoloCigarEntity = null;
    }

    private void ShutDownEnumerateRemoval(Entity<TheManWhoSoldTheWorldComponent> ent)
    {
        var query = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
        while (query.MoveNext(out var gun, out var comp))
        {
            if (comp.GunOwner != ent.Owner)
                continue;

            RestoreGun(gun);
        }
    }

    private void OnMapInitEvent(Entity<TheManWhoSoldTheWorldComponent> ent, ref MapInitEvent args)
    {
        if (!HasComp<NoWieldNeededComponent>(ent))
        {
            ent.Comp.AddedNoWieldNeeded = true;
            AddComp<NoWieldNeededComponent>(ent);
        }
        if (!_inventory.TryGetSlotEntity(ent, MaskSlot, out var cigarEntity) ||
            !HasComp<HoloCigarComponent>(cigarEntity))
            return;
        ent.Comp.HoloCigarEntity = cigarEntity;
    }

    private void OnDroppedEvent(Entity<HoloCigarAffectedGunComponent> ent, ref DroppedEvent args)
    {
        RestoreGun(ent);
    }

    private void OnPickupAttempt(Entity<TheManWhoSoldTheWorldComponent> ent, ref PickupAttemptEvent args)
    {
        if (!HasComp<GunComponent>(args.Item) || HasComp<HoloCigarAffectedGunComponent>(args.Item))
            return;

        var affected = EnsureComp<HoloCigarAffectedGunComponent>(args.Item);
        affected.GunOwner = ent.Owner;

        // Track whether this gun originally required wielding so we can restore it later.
        // Who needs to wield shotgun anyway? Surely badasses don't.
        affected.WasOriginallyGunRequiresWield = HasComp<GunRequiresWieldComponent>(args.Item);

        // If the wearer is big boss already, remove the wield requirement.
        if (ent.Comp.HoloCigarEntity is not null &&
            TryComp<HoloCigarComponent>(ent.Comp.HoloCigarEntity, out var holo) &&
            holo.Lit &&
            HasComp<GunRequiresWieldComponent>(args.Item))
        {
            RemComp<GunRequiresWieldComponent>(args.Item);
        }

        _gun.RefreshModifiers(args.Item);
        // If the wearer is big boss, reevaluate hand multishot pairing so
        // newly picked-up guns in hands get paired immediately.
        var wasLit = false;
        if (ent.Comp.HoloCigarEntity is not null && TryComp<HoloCigarComponent>(ent.Comp.HoloCigarEntity, out var holoComp))
        {
            wasLit = holoComp.Lit;
        }

        if (wasLit)
        {
            // PickupAttempt runs before the item is actually inserted into the hand container.
            // Defer re-evaluation until the entity is inserted into the container (see OnEntGotInsertedIntoContainer).
            ReevaluateHandMultishot(ent.Owner);
        }
    }

    private void OnEntGotInsertedIntoContainer(EntityUid uid, HoloCigarAffectedGunComponent component, EntGotInsertedIntoContainerMessage args)
    {
        // If the item was inserted into a container owned by the big boss,
        // re-evaluate hand pairing now that the item is actually in hands.
        var owner = args.Container.Owner;
        if (TryComp<TheManWhoSoldTheWorldComponent>(owner, out var manComp) &&
            manComp.HoloCigarEntity is not null && TryComp<HoloCigarComponent>(manComp.HoloCigarEntity, out var holo) &&
            holo.Lit)
        {
            _sawmill.Debug($"Ent {uid} inserted into container owned by {owner}; reeval multishot");
            ReevaluateHandMultishot(owner);
        }
    }

    private void OnCigarInserted(Entity<HoloCigarComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        var owner = args.Container.Owner;
        if (!TryComp<TheManWhoSoldTheWorldComponent>(owner, out var manComp))
            return;

        // Track that this wearer now has the cigar equipped.
        manComp.HoloCigarEntity = ent.Owner;

        if (!_net.IsServer)
            return;

        // If the cigar was lit while not equipped, apply its effects now.
        if (ent.Comp.Lit)
        {
            // Remove wield requirement for all affected guns owned by wearer.
            var gunQuery = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
            while (gunQuery.MoveNext(out var gun, out var comp))
            {
                if (comp.GunOwner != owner)
                    continue;

                if (HasComp<GunRequiresWieldComponent>(gun))
                {
                    comp.WasOriginallyGunRequiresWield = true;
                    RemComp<GunRequiresWieldComponent>(gun);
                }
                else
                {
                    comp.WasOriginallyGunRequiresWield = false;
                }
            }

            // Reevaluate hand pairing for the wearer.
            ReevaluateHandMultishot(owner);

            // Start music associated with the cigar.
            var audio = _audio.PlayPvs(ent.Comp.Music, ent);
            if (audio is not null)
                ent.Comp.MusicEntity = audio.Value.Entity;
        }
    }

    private void OnCigarRemoved(Entity<HoloCigarComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        var owner = args.Container.Owner;
        if (!TryComp<TheManWhoSoldTheWorldComponent>(owner, out var manComp))
            return;

        // Clear wearer's reference to this cigar.
        manComp.HoloCigarEntity = null;

        if (!_net.IsServer)
            return;

        // If cigar was lit, stop music and restore affected guns.
        if (ent.Comp.Lit)
        {
            _audio.Stop(ent.Comp.MusicEntity);

            // Restore wield requirements and unpair multishot for guns owned by the previous wearer.
            var query = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
            while (query.MoveNext(out var gun, out var comp))
            {
                if (comp.GunOwner != owner)
                    continue;

                RestoreGun(gun);
            }
        }
    }

    private void OnEntGotRemovedFromContainer(EntityUid uid, HoloCigarAffectedGunComponent component, EntGotRemovedFromContainerMessage args)
    {
        // If the item was removed from a hand container, re-evaluate pairing for the previous owner.
        var owner = args.Container.Owner;
        if (TryComp<TheManWhoSoldTheWorldComponent>(owner, out var manComp) &&
            manComp.HoloCigarEntity is not null && TryComp<HoloCigarComponent>(manComp.HoloCigarEntity, out var holo) &&
            holo.Lit)
        {
            _sawmill.Debug($"Ent {uid} removed from container owned by {owner}; reeval multishot");
            ReevaluateHandMultishot(owner);
        }
    }

    private void HandleToggle(Entity<HoloCigarComponent> ent,
        AppearanceComponent? appearance = null,
        ClothingComponent? clothing = null)
    {
        if (!Resolve(ent, ref appearance, ref clothing) ||
            !_gameTiming.IsFirstTimePredicted) // fuck predicting this shit
            return;

        var state = ent.Comp.Lit ? SmokableState.Unlit : SmokableState.Lit;
        var prefix = ent.Comp.Lit ? UnlitPrefix : LitPrefix;

        _appearance.SetData(ent, SmokingVisuals.Smoking, state, appearance);
        _clothing.SetEquippedPrefix(ent, prefix, clothing);
        _items.SetHeldPrefix(ent, prefix);

        if (!_net.IsServer) // mary copium right here
            return;

        // Find the big boss.
        EntityUid? wearer = null;
        var manQuery = EntityQueryEnumerator<TheManWhoSoldTheWorldComponent>();
        while (manQuery.MoveNext(out var man, out var manComp))
        {
            if (manComp.HoloCigarEntity == ent.Owner)
            {
                wearer = man;
                break;
            }
        }

        // If there is a wearer, iterate all affected guns and add/remove the GunRequiresWield component
        // depending on whether we're turning the cigar on (newLit == true) or off.
        if (wearer is not null)
        {
            var newLit = !ent.Comp.Lit; // ent.Comp.Lit is the current state; newLit is the state after toggle
            var gunQuery = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
            while (gunQuery.MoveNext(out var gun, out var comp))
            {
                if (comp.GunOwner != wearer)
                    continue;

                if (newLit)
                {
                    // Becoming big boss: record original and remove wield requirement if present.
                    if (HasComp<GunRequiresWieldComponent>(gun))
                    {
                        comp.WasOriginallyGunRequiresWield = true;
                        RemComp<GunRequiresWieldComponent>(gun);
                    }
                    else
                    {
                        comp.WasOriginallyGunRequiresWield = false;
                    }
                }
                else
                {
                    // Stopping being big boss: restore wield requirement if it was originally present.
                    if (comp.WasOriginallyGunRequiresWield)
                    {
                        EnsureComp<GunRequiresWieldComponent>(gun);
                    }
                }
            }

            // Handle multishot pairing
            if (newLit)
            {
                // When lighting, only pair guns that are in the wearer's hands.
                ReevaluateHandMultishot(wearer.Value);
            }
            else
            {
                // Turning the cigar off: unpair all multishot weapons
                var unpairedQuery = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
                while (unpairedQuery.MoveNext(out var gun, out var comp))
                {
                    if (comp.GunOwner != wearer)
                        continue;

                    if (HasComp<MultishotComponent>(gun))
                    {
                        _multishot.UnpairWeapon(gun);
                    }
                }
            }
        }

        if (ent.Comp.Lit == false)
        {
            var audio = _audio.PlayPvs(ent.Comp.Music, ent);

            if (audio is null)
                return;
            ent.Comp.MusicEntity = audio.Value.Entity;
            return;
        }

        _audio.Stop(ent.Comp.MusicEntity);
    }

    private void OnComponentHandleState(Entity<HoloCigarComponent> ent, ref ComponentHandleState args)
    {
        if (args.Current is not HoloCigarComponentState state)
            return;

        if (ent.Comp.Lit == state.Lit)
            return;

        ent.Comp.Lit = state.Lit;
        HandleToggle(ent);
    }

    #endregion

    #region Helper Methods

    private void ReevaluateHandMultishot(EntityUid wearer)
    {
        if (!Exists(wearer))
            return;
        // Collect guns currently in hands
        var handGuns = new List<EntityUid>();
        _sawmill.Debug($"Reevaluating hand multishot for wearer {wearer}");
        if (TryComp(wearer, out Content.Shared.Hands.Components.HandsComponent? handsComp))
        {
            foreach (var hand in handsComp.Hands.Values)
            {
                var held = hand.HeldEntity;
                if (held is null)
                    continue;

                if (!Exists(held.Value) || !HasComp<GunComponent>(held.Value))
                    continue;

                handGuns.Add(held.Value);
                _sawmill.Debug($"Found gun in hand: {held.Value}");

                // Ensure the affected-gun component exists for proper tracking
                var affected = EnsureComp<HoloCigarAffectedGunComponent>(held.Value);
                affected.GunOwner = wearer;

                // Remove wield requirement immediately
                if (HasComp<GunRequiresWieldComponent>(held.Value))
                {
                    affected.WasOriginallyGunRequiresWield = true;
                    RemComp<GunRequiresWieldComponent>(held.Value);
                }
            }
        }

        // Unpair any multishot guns owned by wearer that are not in hands
        var query = EntityQueryEnumerator<HoloCigarAffectedGunComponent>();
        while (query.MoveNext(out var gun, out var comp))
        {
            if (comp.GunOwner != wearer)
                continue;

            if (!handGuns.Contains(gun) && HasComp<MultishotComponent>(gun))
            {
                _sawmill.Debug($"Unpairing gun not in hands: {gun}");
                _multishot.UnpairWeapon(gun);
            }
        }

        // Pair weapons in hands in sequence (1+2, 3+4, ...)
        for (int i = 0; i < handGuns.Count - 1; i += 2)
        {
            var gun1 = handGuns[i];
            var gun2 = handGuns[i + 1];
            _multishot.PairWeapons(gun1, gun2, wearer);
        }
    }

    private void RestoreGun(EntityUid gun,
        HoloCigarAffectedGunComponent? cigarAffectedGunComponent = null)
    {
        if (!Resolve(gun, ref cigarAffectedGunComponent))
            return;

        // Restore wield requirement if the gun originally required wielding.
        if (cigarAffectedGunComponent.WasOriginallyGunRequiresWield)
        {
            EnsureComp<GunRequiresWieldComponent>(gun);
        }

        // Unpair multishot if this weapon is currently paired
        if (HasComp<MultishotComponent>(gun))
        {
            _multishot.UnpairWeapon(gun);
        }

        RemComp<HoloCigarAffectedGunComponent>(gun);
    }

    #endregion
}
// So you actually read all of this shitcode? Congrats, you are a true masochist.
// Now, go grab holo-cigar and become big boss.
