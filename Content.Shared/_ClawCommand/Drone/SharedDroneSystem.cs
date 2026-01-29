using Robust.Shared.Serialization;

namespace Content.Shared._ClawCommand.Drone;

public abstract class SharedDroneSystem : EntitySystem
{
    [Serializable, NetSerializable]
    public enum DroneVisuals : byte
    {
        Status
    }

    [Serializable, NetSerializable]
    public enum DroneStatus : byte
    {
        Off,
        On
    }
}
