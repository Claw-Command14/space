using Robust.Shared.GameStates;

namespace Content.Shared._ClawCommand.Grab;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GrabbingItemComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? GrabbedEntity;

    [DataField]
    public TimeSpan GrabBreakDelay = TimeSpan.FromSeconds(5);
}
