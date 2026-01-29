using Content.Server.Access;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Power;
using Robust.Shared.Physics.Components;

namespace Content.Server.Doors.Systems;

public sealed class DoorSystem : SharedDoorSystem
{
    [Dependency] private readonly AirtightSystem _airtightSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DoorBoltComponent, PowerChangedEvent>(OnBoltPowerChanged);
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<DoorComponent, AirlockAtmosBlockOpenComponent, AirtightComponent>();

        while (query.MoveNext(out var uid, out var door, out var airlock, out var airtight))
        {
            if (!door.BlockOpenAtmos)
            {
                continue;
            }
            airlock.Time += frameTime;
            if (airlock.Time >= 1)
            {
                if (door.State == DoorState.Open)
                {
                    _airtightSystem.SetAirblocked((uid, airtight), false);
                }
                RemComp<AirlockAtmosBlockOpenComponent>(uid);
            }
        }

    }
    protected override void SetCollidable(
        EntityUid uid,
        bool collidable,
        DoorComponent? door = null,
        PhysicsComponent? physics = null,
        OccluderComponent? occluder = null)
    {
        if (!Resolve(uid, ref door))
            return;

        if (door.ChangeAirtight && TryComp(uid, out AirtightComponent? airtight))
        {
            // Claw Command
            if (door.BlockOpenAtmos && collidable == false)
            {
                AddComp<AirlockAtmosBlockOpenComponent>(uid);
            }
            else
            {
                _airtightSystem.SetAirblocked((uid, airtight), collidable);
            }
        }

        // Pathfinding / AI stuff.
        RaiseLocalEvent(new AccessReaderChangeEvent(uid, collidable));

        base.SetCollidable(uid, collidable, door, physics, occluder);
    }

    private void OnBoltPowerChanged(Entity<DoorBoltComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            if (ent.Comp.BoltWireCut)
                SetBoltsDown(ent, true);
        }

        UpdateBoltLightStatus(ent);
        ent.Comp.Powered = args.Powered;
        Dirty(ent, ent.Comp);
    }

}

// Claw Command
[RegisterComponent]
public sealed partial class AirlockAtmosBlockOpenComponent : Component
{
    public float Time;
}
