using Content.Shared.Damage.Systems;
using Robust.Shared.GameStates;

namespace Content.Shared.Damage.Components;

/// <summary>
/// Multiplies the entity's <see cref="StaminaComponent.StaminaDamage"/> by the <see cref="Modifier"/>.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GetUpComponent : Component
{
    [ViewVariables(VVAccess.ReadWrite), DataField("modifier"), AutoNetworkedField]
    public float Modifier = 0.65f;
}
