
namespace Content.Server.Flash.Components
{
    [RegisterComponent]
    public sealed partial class FlashImmunityComponent : Component
    {
        [ViewVariables(VVAccess.ReadWrite)]
        public bool Enabled { get; set; } = true;
    }
}
