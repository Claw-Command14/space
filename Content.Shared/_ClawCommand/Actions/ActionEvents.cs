using Robust.Shared.Serialization;

namespace Content.Shared._ClawCommand.Actions;

[Serializable, NetSerializable]
public sealed class LoadActionsEvent(NetEntity entity) : EntityEventArgs
{
    public NetEntity Entity = entity;
}
