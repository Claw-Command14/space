using System.Linq;
using System.Diagnostics.CodeAnalysis;

using Content.Shared.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Mind;
using Content.Shared.Roles.Jobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Roles;

public abstract class SharedRoleSystem : EntitySystem
{
    [Dependency] private readonly ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;

    // TODO please lord make role entities
    private readonly HashSet<Type> _antagTypes = new();

    public override void Initialize()
    {
        // TODO make roles entities
        SubscribeLocalEvent<JobComponent, MindGetAllRolesEvent>(OnJobGetAllRoles);
    }
    /// <summary>
    ///     Adds multiple mind roles to a mind
    /// </summary>
    /// <param name="mindId">The mind entity to add the role to</param>
    /// <param name="roles">The list of mind roles to add</param>
    /// <param name="mind">If the mind component is provided, it will be checked if it belongs to the mind entity</param>
    /// <param name="silent">If true, no briefing will be generated upon receiving the mind role</param>
    public void MindAddRoles(EntityUid mindId,
        List<EntProtoId>? roles,
        MindComponent? mind = null,
        bool silent = false)
    {
        if (roles is null || roles.Count == 0)
            return;

        foreach (var proto in roles)
        {
            MindAddRole(mindId, proto, mind, silent);
        }
    }

    /// <summary>
    ///     Adds a mind role to a mind
    /// </summary>
    /// <param name="mindId">The mind entity to add the role to</param>
    /// <param name="protoId">The mind role to add</param>
    /// <param name="mind">If the mind component is provided, it will be checked if it belongs to the mind entity</param>
    /// <param name="silent">If true, no briefing will be generated upon receiving the mind role</param>
    public void MindAddRole(EntityUid mindId,
        EntProtoId protoId,
        MindComponent? mind = null,
        bool silent = false)
    {
        if (protoId == "MindRoleJob")
            MindAddJobRole(mindId, mind, silent, "");
        else
            MindAddRoleDo(mindId, protoId, mind, silent);
    }
    public void MindAddJobRole(EntityUid mindId,
        MindComponent? mind = null,
        bool silent = false,
        string? jobPrototype = null)
    {
        if (!Resolve(mindId, ref mind))
        {
            Log.Warning($"No Mind found for {ToPrettyString(mindId)} when attempting to add job role.");
            return;
        }

        // Can't have someone get paid for two jobs now, can we
        if (MindHasRole<JobComponent>((mindId, mind), out var jobRole)
            && jobRole.Value.Comp1.JobPrototype != jobPrototype)
        {
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"Job Role of {ToPrettyString(mind.OwnedEntity)} changed from '{jobRole.Value.Comp1.JobPrototype}' to '{jobPrototype}'");

            jobRole.Value.Comp1.JobPrototype = jobPrototype;
        }
        else
            MindAddRoleDo(mindId, "MindRoleJob", mind, silent, jobPrototype);
    }

    /// <summary>
    ///     Creates a Mind Role
    /// </summary>
    private void MindAddRoleDo(EntityUid mindId,
        EntProtoId protoId,
        MindComponent? mind = null,
        bool silent = false,
        string? jobPrototype = null)
    {
        if (!Resolve(mindId, ref mind))
        {
            Log.Error($"Failed to add role {protoId} to {ToPrettyString(mindId)} : Mind does not match provided mind component");
            return;
        }

        if (!_prototypes.TryIndex(protoId, out var protoEnt))
        {
            Log.Error($"Failed to add role {protoId} to {ToPrettyString(mindId)} : Role prototype does not exist");
            return;
        }

        //TODO don't let a prototype being added a second time
        //If that was somehow to occur, a second mindrole for that comp would be created
        //Meaning any mind role checks could return wrong results, since they just return the first match they find

        var mindRoleId = Spawn(protoId, MapCoordinates.Nullspace);
        EnsureComp<MindRoleComponent>(mindRoleId);
        var mindRoleComp = Comp<MindRoleComponent>(mindRoleId);

        mindRoleComp.Mind = (mindId, mind);
        if (jobPrototype is not null)
        {
            mindRoleComp.JobPrototype = jobPrototype;
            EnsureComp<JobComponent>(mindRoleId);
            DebugTools.AssertNull(mindRoleComp.AntagPrototype);
            DebugTools.Assert(!mindRoleComp.Antag);
            DebugTools.Assert(!mindRoleComp.ExclusiveAntag);
        }

        mind.MindRoles.Add(mindRoleId);

        var update = MindRolesUpdate((mindId, mind));

        // RoleType refresh, Role time tracking, Update Admin playerlist

        var message = new RoleAddedEvent(mindId, mind, update, silent);
        RaiseLocalEvent(mindId, message, true);

        var name = Loc.GetString(protoEnt.Name);
        if (mind.OwnedEntity is not null)
        {
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"{name} added to mind of {ToPrettyString(mind.OwnedEntity)}");
        }
        else
        {
            //TODO: This is not tied to the player on the Admin Log filters.
            //Probably only happens when Job Role is added on initial spawn, before the mind entity is put in a mob
            Log.Error($"{ToPrettyString(mindId)} does not have an OwnedEntity!");
            _adminLogger.Add(LogType.Mind,
                LogImpact.Low,
                $"{name} added to {ToPrettyString(mindId)}");
        }
    }
    private void OnJobGetAllRoles(EntityUid uid, JobComponent component, ref MindGetAllRolesEvent args)
    {
        var name = "game-ticker-unknown-role";
        var prototype = "";
        string? playTimeTracker = null;
        if (component.Prototype != null && _prototypes.TryIndex(component.Prototype, out JobPrototype? job))
        {
            name = job.Name;
            prototype = job.ID;
            playTimeTracker = job.PlayTimeTracker;
        }

        name = Loc.GetString(name);

        args.Roles.Add(new RoleInfo(component, name, false, playTimeTracker, prototype));
    }

    /// <summary>
    ///     Select the mind's currently "active" mind role entity, and update the mind's role type, if necessary
    /// </summary>
    /// <returns>
    ///     True if this changed the mind's role type
    /// </returns>>
    private bool MindRolesUpdate(Entity<MindComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp))
            return false;

        //get the most important/latest mind role
        var roleType = GetRoleTypeByTime(ent.Comp);

        if (ent.Comp.RoleType == roleType)
            return false;

        SetRoleType(ent.Owner, roleType);
        return true;
    }

    private ProtoId<RoleTypePrototype> GetRoleTypeByTime(MindComponent mind)
    {
        // If any Mind Roles specify a Role Type, return the most recent. Otherwise return Neutral

        var roles = new List<ProtoId<RoleTypePrototype>>();

        foreach (var role in mind.MindRoles)
        {
            if (!TryComp<MindRoleComponent>(role, out var comp))
                continue;

            if (comp.RoleType is not null)
                roles.Add(comp.RoleType.Value);
        }

        ProtoId<RoleTypePrototype> result = (roles.Count > 0) ? roles.LastOrDefault() : "Neutral";
        return (result);
    }

    private void SetRoleType(EntityUid mind, ProtoId<RoleTypePrototype> roleTypeId)
    {
        if (!TryComp<MindComponent>(mind, out var comp))
        {
            Log.Error($"Failed to update Role Type of mind entity {ToPrettyString(mind)} to {roleTypeId}. MindComponent not found.");
            return;
        }

        if (!_prototypes.HasIndex(roleTypeId))
        {
            Log.Error($"Failed to change Role Type of {_minds.MindOwnerLoggingString(comp)} to {roleTypeId}. Invalid role");
            return;
        }

        comp.RoleType = roleTypeId;
        Dirty(mind, comp);

        // Update player character window
        if (_minds.TryGetSession(mind, out var session))
            RaiseNetworkEvent(new MindRoleTypeChangedEvent(), session.Channel);
        else
        {
            var error = $"The Character Window of {_minds.MindOwnerLoggingString(comp)} potentially did not update immediately : session error";
            _adminLogger.Add(LogType.Mind, LogImpact.High, $"{error}");
        }

        if (comp.OwnedEntity is null)
        {
            Log.Error($"{ToPrettyString(mind)} does not have an OwnedEntity!");
            _adminLogger.Add(LogType.Mind,
                LogImpact.High,
                $"Role Type of {ToPrettyString(mind)} changed to {roleTypeId}");
            return;
        }

        _adminLogger.Add(LogType.Mind,
            LogImpact.High,
            $"Role Type of {ToPrettyString(comp.OwnedEntity)} changed to {roleTypeId}");
    }
    protected void SubscribeAntagEvents<T>() where T : AntagonistRoleComponent
    {
        SubscribeLocalEvent((EntityUid _, T component, ref MindGetAllRolesEvent args) =>
        {
            var name = "game-ticker-unknown-role";
            var prototype = "";
            if (component.PrototypeId != null && _prototypes.TryIndex(component.PrototypeId, out AntagPrototype? antag))
            {
                name = antag.Name;
                prototype = antag.ID;
            }
            name = Loc.GetString(name);

            args.Roles.Add(new RoleInfo(component, name, true, null, prototype));
        });

        SubscribeLocalEvent((EntityUid _, T _, ref MindIsAntagonistEvent args) => { args.IsAntagonist = true; args.IsExclusiveAntagonist |= typeof(T).TryGetCustomAttribute<ExclusiveAntagonistAttribute>(out _); });
        _antagTypes.Add(typeof(T));
    }

    public void MindAddRoles(EntityUid mindId, ComponentRegistry components, MindComponent? mind = null, bool silent = false)
    {
        if (!Resolve(mindId, ref mind))
            return;

        EntityManager.AddComponents(mindId, components);
        var antagonist = false;
        foreach (var compReg in components.Values)
        {
            var compType = compReg.Component.GetType();

            var comp = EntityManager.ComponentFactory.GetComponent(compType);
            if (IsAntagonistRole(comp.GetType()))
            {
                antagonist = true;
                break;
            }
        }

        var mindEv = new MindRoleAddedEvent(silent);
        RaiseLocalEvent(mindId, ref mindEv);

        var message = new RoleAddedEvent(mindId, mind, antagonist, silent);
        if (mind.OwnedEntity != null)
        {
            RaiseLocalEvent(mind.OwnedEntity.Value, message, true);
        }

        _adminLogger.Add(LogType.Mind, LogImpact.Low,
            $"Role components {string.Join(components.Keys.ToString(), ", ")} added to mind of {_minds.MindOwnerLoggingString(mind)}");
    }

    public void MindAddRole(EntityUid mindId, Component component, MindComponent? mind = null, bool silent = false)
    {
        if (!Resolve(mindId, ref mind))
            return;

        if (HasComp(mindId, component.GetType()))
        {
            throw new ArgumentException($"We already have this role: {component}");
        }

        EntityManager.AddComponent(mindId, component);
        var antagonist = IsAntagonistRole(component.GetType());

        var mindEv = new MindRoleAddedEvent(silent);
        RaiseLocalEvent(mindId, ref mindEv);

        var message = new RoleAddedEvent(mindId, mind, antagonist, silent);
        if (mind.OwnedEntity != null)
        {
            RaiseLocalEvent(mind.OwnedEntity.Value, message, true);
        }

        _adminLogger.Add(LogType.Mind, LogImpact.Low,
            $"'Role {component}' added to mind of {_minds.MindOwnerLoggingString(mind)}");
    }

    /// <summary>
    ///     Gives this mind a new role.
    /// </summary>
    /// <param name="mindId">The mind to add the role to.</param>
    /// <param name="component">The role instance to add.</param>
    /// <typeparam name="T">The role type to add.</typeparam>
    /// <param name="silent">Whether or not the role should be added silently</param>
    /// <returns>The instance of the role.</returns>
    /// <exception cref="ArgumentException">
    ///     Thrown if we already have a role with this type.
    /// </exception>
    public void MindAddRole<T>(EntityUid mindId, T component, MindComponent? mind = null, bool silent = false) where T : IComponent, new()
    {
        if (!Resolve(mindId, ref mind))
            return;

        if (HasComp<T>(mindId))
        {
            throw new ArgumentException($"We already have this role: {typeof(T)}");
        }

        AddComp(mindId, component);
        var antagonist = IsAntagonistRole<T>();

        var mindEv = new MindRoleAddedEvent(silent);
        RaiseLocalEvent(mindId, ref mindEv);

        var message = new RoleAddedEvent(mindId, mind, antagonist, silent);
        if (mind.OwnedEntity != null)
        {
            RaiseLocalEvent(mind.OwnedEntity.Value, message, true);
        }

        _adminLogger.Add(LogType.Mind, LogImpact.Low,
            $"'Role {typeof(T).Name}' added to mind of {_minds.MindOwnerLoggingString(mind)}");
    }

    /// <summary>
    ///     Removes a role from this mind.
    /// </summary>
    /// <param name="mindId">The mind to remove the role from.</param>
    /// <typeparam name="T">The type of the role to remove.</typeparam>
    /// <exception cref="ArgumentException">
    ///     Thrown if we do not have this role.
    /// </exception>
    public void MindRemoveRole<T>(EntityUid mindId) where T : IComponent
    {
        if (!RemComp<T>(mindId))
        {
            throw new ArgumentException($"We do not have this role: {typeof(T)}");
        }

        var mind = Comp<MindComponent>(mindId);
        var antagonist = IsAntagonistRole<T>();
        var message = new RoleRemovedEvent(mindId, mind, antagonist);

        if (mind.OwnedEntity != null)
        {
            RaiseLocalEvent(mind.OwnedEntity.Value, message, true);
        }
        _adminLogger.Add(LogType.Mind, LogImpact.Low,
            $"'Role {typeof(T).Name}' removed from mind of {_minds.MindOwnerLoggingString(mind)}");
    }

    public bool MindTryRemoveRole<T>(EntityUid mindId) where T : IComponent
    {
        if (!MindHasRole<T>(mindId))
            return false;

        MindRemoveRole<T>(mindId);
        return true;
    }
    /// <summary>
    /// Finds the first mind role of a specific T type on a mind entity.
    /// Outputs entity components for the mind role's MindRoleComponent and for T
    /// </summary>
    /// <param name="mind">The mind entity</param>
    /// <typeparam name="T">The type of the role to find.</typeparam>
    /// <param name="role">The Mind Role entity component</param>
    /// <returns>True if the role is found</returns>
    public bool MindHasRole<T>(Entity<MindComponent?> mind,
        [NotNullWhen(true)] out Entity<MindRoleComponent, T>? role) where T : IComponent
    {
        role = null;
        if (!Resolve(mind.Owner, ref mind.Comp))
            return false;

        foreach (var roleEnt in mind.Comp.MindRoles)
        {
            if (!TryComp(roleEnt, out T? tcomp))
                continue;

            if (!TryComp(roleEnt, out MindRoleComponent? roleComp))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(roleEnt)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            role = (roleEnt, roleComp, tcomp);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Finds the first mind role of a specific type on a mind entity.
    /// Outputs an entity component for the mind role's MindRoleComponent
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <param name="type">The Type to look for</param>
    /// <param name="role">The output role</param>
    /// <returns>True if the role is found</returns>
    public bool MindHasRole(EntityUid mindId,
        Type type,
        [NotNullWhen(true)] out Entity<MindRoleComponent>? role)
    {
        role = null;
        // All MindRoles have this component, it would just return the first one.
        // Order might not be what is expected.
        // Better to report null
        if (type == Type.GetType("MindRoleComponent"))
        {
            Log.Error($"Something attempted to query mind role 'MindRoleComponent' on mind {mindId}. This component is present on every single mind role.");
            return false;
        }

        if (!TryComp<MindComponent>(mindId, out var mind))
            return false;

        var found = false;

        foreach (var roleEnt in mind.MindRoles)
        {
            if (!HasComp(roleEnt, type))
                continue;

            if (!TryComp(roleEnt, out MindRoleComponent? roleComp))
            {
                Log.Error($"Encountered mind role entity {ToPrettyString(roleEnt)} without a {nameof(MindRoleComponent)}");
                continue;
            }

            role = (roleEnt, roleComp);
            found = true;
            break;
        }

        return found;
    }
    public bool MindHasRole<T>(EntityUid mindId) where T : IComponent
    {
        DebugTools.Assert(HasComp<MindComponent>(mindId));
        return HasComp<T>(mindId);
    }

    public List<RoleInfo> MindGetAllRoles(EntityUid mindId)
    {
        DebugTools.Assert(HasComp<MindComponent>(mindId));
        var ev = new MindGetAllRolesEvent(new List<RoleInfo>());
        RaiseLocalEvent(mindId, ref ev);
        return ev.Roles;
    }

    public bool MindIsAntagonist(EntityUid? mindId)
    {
        if (mindId == null)
            return false;

        DebugTools.Assert(HasComp<MindComponent>(mindId));
        var ev = new MindIsAntagonistEvent();
        RaiseLocalEvent(mindId.Value, ref ev);
        return ev.IsAntagonist;
    }

    /// <summary>
    /// Does this mind possess an exclusive antagonist role
    /// </summary>
    /// <param name="mindId">The mind entity</param>
    /// <returns>True if the mind possesses an exclusive antag role</returns>
    public bool MindIsExclusiveAntagonist(EntityUid? mindId)
    {
        if (mindId == null)
            return false;

        var ev = new MindIsAntagonistEvent();
        RaiseLocalEvent(mindId.Value, ref ev);
        return ev.IsExclusiveAntagonist;
    }

    public bool IsAntagonistRole<T>()
    {
        return _antagTypes.Contains(typeof(T));
    }

    public bool IsAntagonistRole(Type component)
    {
        return _antagTypes.Contains(component);
    }

    /// <summary>
    /// Play a sound for the mind, if it has a session attached.
    /// Use this for role greeting sounds.
    /// </summary>
    public void MindPlaySound(EntityUid mindId, SoundSpecifier? sound, MindComponent? mind = null)
    {
        if (Resolve(mindId, ref mind) && mind.Session != null)
            _audio.PlayGlobal(sound, mind.Session);
    }
}
/// <summary>
/// Raised on the client to update Role Type on the character window, in case it happened to be open.
/// </summary>
[Serializable, NetSerializable]
public sealed class MindRoleTypeChangedEvent : EntityEventArgs
{

}
