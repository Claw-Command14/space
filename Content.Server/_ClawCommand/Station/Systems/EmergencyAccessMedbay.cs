using Content.Server.Chat.Systems;
using Content.Server.DeltaV.Cabinet;
using Content.Server._ClawCommand.Station.Components;
using Content.Server.DeltaV.Station.Events;
using Content.Server.GameTicking;
using Content.Server.Station.Components;
using Content.Shared.Access.Components;
using Content.Shared.Access;
using Content.Shared.DeltaV.CCVars;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Content.Server.Doors.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Access.Systems;

namespace Content.Server._ClawCommand.Station.Systems;

public sealed class EmergencyAccessMedbayStateSystem : EntitySystem
{
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly AirlockSystem _airlockSystem = default!;
    [Dependency] private readonly AccessReaderSystem _accessReaderSystem = default!;
    [Dependency] protected readonly ILogManager _logManager = default!;

    public ISawmill _sawmill { get; private set; } = default!;

    private TimeSpan _acoDelay = TimeSpan.FromMinutes(10);
    private int _maxDoctorsForEA = 2;
    private bool _isAAInPlay = false;
    private int _doctorCount = 0;
    private int _latestRound = -1;

    private TimeSpan _lastRoundTime = TimeSpan.FromHours(10);

    public override void Initialize()
    {
        _sawmill = _logManager.GetSawmill("eamedbay");

        SubscribeLocalEvent<EmergencyAccessMedbayStateComponent, PlayerJobAddedEvent>(OnPlayerJobAdded);
        SubscribeLocalEvent<EmergencyAccessMedbayStateComponent, PlayerJobsRemovedEvent>(OnPlayerJobsRemoved);
        base.Initialize();
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

            _isAAInPlay = false;
            _doctorCount = 0;
            return;
        }

        var query = EntityQueryEnumerator<EmergencyAccessMedbayStateComponent>();
        while (query.MoveNext(out var station, out var captainState))
        {

            if (_doctorCount <= _maxDoctorsForEA)
            {
                if (!_isAAInPlay)
                {
                    _isAAInPlay = true;
                    _chat.DispatchStationAnnouncement(station, Loc.GetString("no-doctors-aa-unlocked-announcement"), colorOverride: Color.Yellow);

                    // Extend access of spare id lockers to command so they can access emergency AA
                    var query2 = EntityQueryEnumerator<AirlockComponent>();
                    while (query2.MoveNext(out var airlockID, out var airlockComp))
                    {

                        if (_accessReaderSystem.GetMainAccessReader(airlockID, out var mainReader))
                        {

                            if (mainReader.AccessLists.Any(list => list.Contains("Medical")))
                            {

                                _airlockSystem.SetEmergencyAccess((airlockID, airlockComp), true);
                            }
                        }

                    }
                }
            }
            else if (_isAAInPlay)
            {


                _isAAInPlay = false;
                _chat.DispatchStationAnnouncement(station, Loc.GetString("doctors-arrived-revoke-aco-announcement"), colorOverride: Color.Yellow);

                var qquery = EntityQueryEnumerator<AirlockComponent>();
                while (qquery.MoveNext(out var airlockID, out var airlockComp))
                {

                    if (_accessReaderSystem.GetMainAccessReader(airlockID, out var mainReader))
                    {

                        if (mainReader.AccessLists.Any(list => list.Contains("Medical")))
                        {

                            _airlockSystem.SetEmergencyAccess((airlockID, airlockComp), false);
                        }
                    }

                }

            }

        }
    }

    private void OnPlayerJobAdded(Entity<EmergencyAccessMedbayStateComponent> ent, ref PlayerJobAddedEvent args)
    {
        if (args.JobPrototypeId == "ChiefMedicalOfficer" ||
         args.JobPrototypeId == "MedicalDoctor" ||
          args.JobPrototypeId == "Chemist" ||
          args.JobPrototypeId == "Paramedic")
        {
            _doctorCount += 1;
        }
    }

    private void OnPlayerJobsRemoved(Entity<EmergencyAccessMedbayStateComponent> ent, ref PlayerJobsRemovedEvent args)
    {
        foreach (var job in args.PlayerJobs)
        {
            if (job == "ChiefMedicalOfficer" ||
                    job == "MedicalDoctor" ||
                     job == "Chemist" ||
                     job == "Paramedic")
            {

                _doctorCount -= 1;
                if (_doctorCount < 0)
                {
                    _doctorCount = 0;
                }
            }
        }
    }
}
