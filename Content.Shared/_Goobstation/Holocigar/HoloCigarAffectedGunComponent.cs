// SPDX-FileCopyrightText: 2025 August Eymann <august.eymann@gmail.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
// SPDX-FileCopyrightText: 2025 pheenty <fedorlukin2006@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

// Stolen from Goob, modified to work

namespace Content.Shared.HoloCigar;

/// <summary>
/// This is used for tracking affected HoloCigar weapons.
/// </summary>
[RegisterComponent]
public sealed partial class HoloCigarAffectedGunComponent : Component
{
    [ViewVariables]
    public EntityUid GunOwner = EntityUid.Invalid;

    [ViewVariables]
    public bool WasOriginallyGunRequiresWield = false;

    /// <summary>
    /// Whether this gun was originally part of a multishot pair (tracked for unppairing on cigar deactivation).
    /// </summary>
    [ViewVariables]
    public bool WasOriginallyMultishot = false;

    /// <summary>
    /// The weapon this gun was paired with when multishot was active (null if not paired).
    /// </summary>
    [ViewVariables]
    public EntityUid? OriginalMultishotPair = null;
}
