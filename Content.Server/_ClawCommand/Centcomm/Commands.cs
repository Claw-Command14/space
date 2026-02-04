
using Robust.Shared.Console;
using Content.Shared.Administration;
using System.Linq;
using Content.Server.Spawners.Components;
using Content.Server.Mind;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Content.Server.Preferences.Managers;
using Robust.Shared.Player;
using Content.Shared.Players;
using Robust.Shared.Network;
namespace Content.Server._ClawCommand.Centcomm;

using Content.Server.Administration.Managers;
using Content.Shared.Roles.Jobs;

internal sealed class CentcommSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _consoleHost = default!;

    [Dependency] private readonly MindSystem _mindSystem = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;


    [Dependency] private readonly IServerPreferencesManager _prefs = default!;
    IEnumerable<String> _centcommTypes = ["centcommofficial"];



    public override void Initialize()
    {
        base.Initialize();

        _consoleHost.RegisterCommand("centcomm", Loc.GetString("centcomm-spawn-command-desc"), "centcomm type",
            CentcommCallback,
            GetCompletion);
    }

    [AnyCommand]
    public void CentcommCallback(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is null)
        {
            shell.WriteError("A player must execute this command.");
            return;
        }
        if (shell.Player is not ICommonSession player)
        {
            shell.WriteError(Loc.GetString("shell-only-players-can-run-this-command"));
            return;
        }
        var data = player.ContentData();

        if (data?.UserId == null)
        {
            shell.WriteError(Loc.GetString("shell-entity-is-not-mob"));
            return;
        }
        if (!_adminManager.HasAdminFlag(shell.Player, AdminFlags.VIPPlus) && !_adminManager.HasAdminFlag(shell.Player, AdminFlags.Admin))
        {
            shell.WriteError("You need to be a VIP Plus tier patron for access to this command.");
            return;
        }

        if (args.Length > 1 /*|| args.Length <= 0*/)
        {
            shell.WriteError("One argument max.");
            return;
        }

        //var type = args[0].ToLowerInvariant();
        var type = "CentcommOfficial".ToLowerInvariant();
        if (!_centcommTypes.Contains(type))
        {
            shell.WriteError("Invalid type.");
            return;
        }

        if (type == "centcommofficial")
        {

            var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();

            while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
            {
                var isMatchingCentcommJob = spawnPoint.Job?.ID == "CentralCommandOfficial";

                EntityUid? mob = null;
                if (isMatchingCentcommJob)
                {
                    if (shell.Player.AttachedEntity is null)
                    {
                        shell.WriteLine("You must be attached to an entity, observe as ghost.");
                        break;
                    }
                    if (!_mindSystem.TryGetMind(shell.Player.AttachedEntity.Value, out var mindId, out var mind))
                    {
                        shell.WriteLine("You must have a mind, try observe as ghost.");
                        break;
                    }



                    HumanoidCharacterProfile character;


                    character = (HumanoidCharacterProfile) _prefs.GetPreferences(data.UserId).SelectedCharacter;

                    mob = _entityManager.System<StationSpawningSystem>()
            .SpawnPlayerMob(xform.Coordinates, profile: character, entity: null, job: new JobComponent { Prototype = "CentralCommandOfficial" }, station: null);

                    _mindSystem.TransferTo(mindId, mob);

                    shell.WriteLine($"Success.");
                    break;
                }
            }


        }

    }
    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(_centcommTypes, "Optional: determines which type of centcomm role you spawn as.");
        }

        return CompletionResult.Empty;
    }
    // copy of Content.Server.DeltaV.Administration.Commands;
    public bool FetchCharacters(NetUserId player, out HumanoidCharacterProfile[] characters)
    {
        characters = null!;
        if (!_prefs.TryGetCachedPreferences(player, out var prefs))
            return false;

        characters = prefs.Characters
            .Where(kv => kv.Value is HumanoidCharacterProfile)
            .Select(kv => (HumanoidCharacterProfile) kv.Value)
            .ToArray();

        return true;
    }
}
