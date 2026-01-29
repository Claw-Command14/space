using Content.Server.Chat.Systems;
using Content.Server._ClawCommand.Station.Components;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.AlertLevel;

namespace Content.Server._ClawCommand.Station.Systems;

public sealed class CodeBlueSecretStateSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly AlertLevelSystem _alertLevelSystem = default!;
    [Dependency] protected readonly ILogManager _logManager = default!;

    public ISawmill _sawmill { get; private set; } = default!;
    private TimeSpan _acoDelay = TimeSpan.FromMinutes(8);
    private bool _isAutoCodeBlueInPlay = false;
    private int _latestRound = -1;

    private TimeSpan _lastRoundTime = TimeSpan.FromHours(10);

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("autocodeblue");
    }
    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var timePassed = _ticker.RoundDuration();
        if (timePassed < _acoDelay) // Avoid timing issues. No need to run before _acoDelay is reached anyways.
            return;
        if (_latestRound != _ticker.RoundId)
        {
            if (_latestRound != -1)
            {
                _lastRoundTime = timePassed;
            }
            _latestRound = _ticker.RoundId;
            _isAutoCodeBlueInPlay = false;
            return;
        }
        if (_isAutoCodeBlueInPlay)
        {
            return;
        }
        if (_ticker.IsGameRuleAdded<SecretRuleComponent>())
        {

            var query = EntityQueryEnumerator<CodeBlueSecretStateComponent>();
            while (query.MoveNext(out var station, out var c))
            {


                _isAutoCodeBlueInPlay = true;
                if (_alertLevelSystem.GetLevel(station) == "green")
                {
                    _alertLevelSystem.SetLevel(station, "blueAuto", true, true);
                }
            }


        }


    }

}
