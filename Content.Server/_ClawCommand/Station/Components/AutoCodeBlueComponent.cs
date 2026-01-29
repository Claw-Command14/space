using Content.Server._ClawCommand.Station.Systems;
using Content.Server.Station.Systems;

namespace Content.Server._ClawCommand.Station.Components;

/// <summary>
/// Used for a few automated systems more than just emergency accessing the medbay.
/// </summary>
[RegisterComponent, Access(typeof(CodeBlueSecretStateSystem), typeof(StationSystem))]

public sealed partial class CodeBlueSecretStateComponent : Component
{

}
