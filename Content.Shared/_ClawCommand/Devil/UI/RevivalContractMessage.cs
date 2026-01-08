using Robust.Shared.Player;
using Robust.Shared.Serialization;

namespace Content.Shared._ClawCommand.devil.UI;

[Serializable, NetSerializable]
public sealed class RevivalContractMessage(bool accepted) : BoundUserInterfaceMessage
{
    public bool Accepted { get; } = accepted;
}
