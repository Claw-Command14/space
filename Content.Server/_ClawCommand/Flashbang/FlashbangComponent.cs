namespace Content.Server._ClawCommand.Flashbang;

[RegisterComponent]
public sealed partial class FlashbangComponent : Component
{
    [DataField]
    public float StunTime = 2f;

    [DataField]
    public float KnockdownTime = 10f;
}
