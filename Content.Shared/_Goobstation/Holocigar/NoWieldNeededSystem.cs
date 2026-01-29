// Bit modified Goob file.

using Content.Shared.NoWieldNeeded;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Content.Shared.Wieldable;
using Robust.Shared.Containers;

namespace Content.Shared.NoWieldNeeded;

public sealed class NoWieldNeededSystem : EntitySystem
{
    [Dependency] private readonly SharedGunSystem _gun = default!;
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NoWieldNeededComponent, EntRemovedFromContainerMessage>(OnGunDropped);
    }

    private void OnGunDropped(EntityUid uid, NoWieldNeededComponent comp, EntRemovedFromContainerMessage args)
    {
        if (!comp.GetBonus)
            return;

        comp.GunsWithBonus.Remove(args.Entity);
    }
}
