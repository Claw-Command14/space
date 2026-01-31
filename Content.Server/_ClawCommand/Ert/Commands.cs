
using Robust.Shared.Console;
using Content.Server.Administration;
using Content.Shared.Administration;
using System.Linq;
using Content.Server.Spawners.Components;
using Robust.Shared.Prototypes;
using Content.Shared.Roles;
using Content.Server.Chat.Systems;
using Content.Server.DeltaV.Station.Components;
using Content.Shared.Inventory;
using Content.Server.Announcements.Systems;
using Robust.Shared.Player;
using Content.Server.Discord;
using Robust.Shared.Configuration;
using Content.Shared.CCVar;
namespace Content.Server._ClawCommand.Ert;

internal sealed class ErtSystem : EntitySystem
{
    [Dependency] private readonly IConsoleHost _consoleHost = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly AnnouncerSystem _announcer = default!;
    [Dependency] private readonly DiscordWebhook _discord = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;

    public ProtoId<JobPrototype>? SecurityPrototype = "RandomHumanoidSpawnerERTSecurity";
    public ProtoId<JobPrototype>? MedicalPrototype = "RandomHumanoidSpawnerERTMedical";
    public ProtoId<JobPrototype>? LeaderPrototype = "RandomHumanoidSpawnerERTLeader";
    public ProtoId<JobPrototype>? AdmiralPrototype = "RandomHumanoidSpawnerAdmiralClaw";
    IEnumerable<String> _ertTypes = ["security"];

    public override void Initialize()
    {
        base.Initialize();

        _consoleHost.RegisterCommand("ert", Loc.GetString("ert-command-desc"), "ert type amount discordNotify admiral",
            ErtCallback,
            GetCompletion);
    }

    [AdminCommand(AdminFlags.Admin)]
    public void ErtCallback(IConsoleShell shell, string argStr, string[] args)
    {

        if (args.Length > 4 || args.Length < 1)
        {
            shell.WriteError("Needs at least one argument (type).");
            return;
        }

        var type = args[0].ToLowerInvariant();
        if (!_ertTypes.Contains(type))
        {
            shell.WriteError("Invalid type.");
            return;
        }

        var amount = 4;
        if (args.Length > 1 && !int.TryParse(args[1], out amount))
        {
            shell.WriteError("Unable to parse amount.");
            return;
        }
        if (amount <= 0)
        {
            shell.WriteError("Amount must be a positive integer.");
            return;
        }
        if (amount > 14)
        {
            shell.WriteError("Amount must be less than or equal to 14.");
            return;
        }

        bool discordNotify = false;
        if (args.Length > 2 && !bool.TryParse(args[2], out discordNotify))
        {
            shell.WriteError("Unable to parse discordNotify.");
            return;
        }
        bool admiral = false;
        if (args.Length > 3 &&
            !bool.TryParse(args[3], out admiral))
        {
            shell.WriteError("Unable to parse admiral.");
            return;
        }

        if (type == "security")
        {
            // Reset counts
            int securityAmount = 0;
            int medicalAmount = 0;

            if (amount > 0)
            {
                // One leader always takes one slot
                int remaining = amount - 1;

                if (remaining > 0)
                {
                    // Ratio 1 medical : 4 security
                    const int medicalRatio = 1;
                    const int securityRatio = 4;
                    const int ratioTotal = medicalRatio + securityRatio;

                    medicalAmount = (int) Math.Round((double) remaining * medicalRatio / ratioTotal);
                    securityAmount = remaining - medicalAmount;

                    // Enforce minimum medical rule
                    if (medicalAmount == 0 && amount >= 3)
                    {
                        medicalAmount = 1;
                        securityAmount = remaining - medicalAmount;
                    }
                }
            }

            var points = EntityQueryEnumerator<SpawnPointComponent, TransformComponent>();
            var unitName = SecurityUnitNameGenerator.Generate();

            int securityI = 0;
            int medicalI = 0;
            while (points.MoveNext(out var uid, out var spawnPoint, out var xform))
            {
                var isMatchingSecurityJob = //spawnPoint.SpawnType == SpawnPointType.Job &&
                                    spawnPoint.Job?.ID == "ERTSecurity";
                var isMatchingMedicalJob = spawnPoint.Job?.ID == "ERTMedical";
                var isMatchingLeaderJob = spawnPoint.Job?.ID == "ERTLeader";
                var isMatchingAdmiralJob = spawnPoint.Job?.ID == "AdmiralClaw";

                EntityUid? mob = null;
                if (securityI < securityAmount && isMatchingSecurityJob)
                {
                    mob = Spawn(SecurityPrototype, xform.Coordinates);
                    securityI++;

                }
                else if (medicalI < medicalAmount && isMatchingMedicalJob)
                {
                    mob = Spawn(MedicalPrototype, xform.Coordinates);
                    medicalI++;
                }
                else if (isMatchingLeaderJob)
                {
                    mob = Spawn(LeaderPrototype, xform.Coordinates);
                }
                else if (isMatchingAdmiralJob && admiral)
                {
                    mob = Spawn(AdmiralPrototype, xform.Coordinates);
                }
            }
            var admiralText = "";
            if (admiral)
            {
                admiralText = " and 1 admiral";
            }
            var mainStation = EntityQueryEnumerator<CaptainStateComponent>();
            EntityUid? station = null;
            while (mainStation.MoveNext(out var uid, out var _))
            {
                station = uid;
                break;
            }
            if (station is null)
            {
                shell.WriteError("No main station found.");
                return;
            }
            shell.WriteLine($"Team detached. Spawning {securityI} security staff, {medicalI} medical staff, 1 leader" + admiralText + ".");
            _chatSystem.DispatchStationAnnouncement(station.Value, "Emergency Response: " + unitName + " is being detached and briefed at centcomm. ETA 10 minutes.",
                colorOverride: Color.FromHex("#ff2768ff"),
                sender: "Claw Command",
                playDefaultSound: false);
            _announcer.SendAnnouncementAudio(unitName, Filter.Broadcast());
            if (discordNotify)
            {
                SendERTDiscordMessage(amount);
            }

        }

    }

    private async void SendERTDiscordMessage(int amount)
    {
        try
        {
            var webhookIdentifier = _cfg.GetCVar(CCVars.DiscordERTNotificationWebhook);
            if (webhookIdentifier == null)
                return;
            if (await _discord.GetWebhook(webhookIdentifier) is not { } identifier)
                return;

            var discordRoundEndRoleWebhook = _cfg.GetCVar(CCVars.DiscordERTNotificationRoleWebhook);

            if (discordRoundEndRoleWebhook == null)
                return;

            var content = "<@&" + discordRoundEndRoleWebhook + "> attention, an emergency response is in progress. An ERT team with " + amount + " members has been tasked. Help is requested! Please join the response team at centcomm to get briefed.";
            var payload = new WebhookPayload { Content = content };
            payload.AllowedMentions.AllowRoleMentions();

            await _discord.CreateMessage(identifier.ToIdentifier(), payload);
        }
        catch (Exception e)
        {
            Log.Error($"Error while sending discord ert message:\n{e}");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(_ertTypes, "Determines which type of ERT roles spawn.");
        }
        else if (args.Length == 2)
        {
            return CompletionResult.FromHintOptions(["4"], "Optional integer: Amount of ERT roles to spawn.");
        }
        else if (args.Length == 3)
        {
            return CompletionResult.FromHintOptions(["false"], "Optional boolean: Notify Discord (ONLY ONE NOTIFY PER ROUND).");
        }
        else if (args.Length == 4)
        {
            return CompletionResult.FromHintOptions(["false"], "Optional boolean: Spawn admin only admiral role.");
        }

        return CompletionResult.Empty;
    }

}

public static class SecurityUnitNameGenerator
{
    private static readonly Random Rng = new Random();

    private static readonly string[] CoreNames =
    {
        "Aegis", "Iron", "Obsidian", "Vanguard",
        "Sentinel", "Helios", "Phalanx", "Nova",
        "Atlas", "Onyx", "Cerberus", "Delta", "Sigma", "Omega", "Alpha", "Beta"
    };

    private static readonly string[] Suffixes =
    {
        "Wing","Task Group","Division","Detachment","Unit", "Team", "Squad", "Response"
    };

    public static string Generate()
    {
        var name = Pick(CoreNames);

        // 70% chance to add a short suffix
        if (Chance(0.7f))
            name += " " + Pick(Suffixes);

        // 60% chance to add a number between 1–14
        if (Chance(0.6f))
        {
            var number = Rng.Next(1, 15);
            name += Chance(0.5f)
                ? $"-{number}"
                : $" {number}";
        }

        return name;
    }

    private static string Pick(string[] array)
        => array[Rng.Next(array.Length)];

    private static bool Chance(float probability)
        => Rng.NextDouble() < probability;

    public static (int leader, int medical, int security) DistributeSecurity(int total)
    {
        if (total <= 0)
            return (0, 0, 0);

        int leader = 1;
        int remaining = total - leader;

        if (remaining <= 0)
            return (leader, 0, 0);

        // Ratio parts: 1 medical, 4 security
        const int medicalRatio = 1;
        const int securityRatio = 4;
        const int ratioTotal = medicalRatio + securityRatio;

        int medical = (int) Math.Round((double) remaining * medicalRatio / ratioTotal);
        int security = remaining - medical;

        // Enforce minimum medical rule
        if (medical == 0 && total >= 3)
        {
            medical = 1;
            security = remaining - medical;
        }

        return (leader, medical, security);
    }


}
