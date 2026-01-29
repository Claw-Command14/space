// SPDX-FileCopyrightText: 2025 August Eymann <august.eymann@gmail.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Robust.Shared.GameStates;

namespace Content.Shared.Multishot;

/// <summary>
/// Indicates that this gun is part of a multishot pair and can fire simultaneously with another.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class MultishotComponent : Component
{
    /// <summary>
    /// The entity ID of the paired weapon in this multishot arrangement.
    /// </summary>
    [ViewVariables]
    public EntityUid? PairedWeapon = null;

    /// <summary>
    /// The owner of both weapons (the entity wielding/holding them).
    /// </summary>
    [ViewVariables]
    public EntityUid WeaponOwner = EntityUid.Invalid;

    /// <summary>
    /// Accuracy bonus applied to shots when using multishot (30% = 1.3f multiplier).
    /// </summary>
    [ViewVariables]
    public const float AccuracyBonus = 1.3f;
}
