using Robust.Shared.GameStates;

namespace Content.Shared._ClawCommand.Grab;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class BeingGrabbedComponent : Component
{
    [ViewVariables(VVAccess.ReadOnly), AutoNetworkedField]
    public EntityUid? GrabberItemUid;
}
