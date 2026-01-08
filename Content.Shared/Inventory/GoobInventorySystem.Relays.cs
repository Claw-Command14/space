// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 GoobBot <uristmchands@proton.me>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 Solstice <solsticeofthewinter@gmail.com>
// SPDX-FileCopyrightText: 2025 SolsticeOfTheWinter <solsticeofthewinter@gmail.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later


using Content.Shared._ClawCommand.devil;
using Content.Shared.Damage.Events;
using Content.Shared.Inventory;
using Content.Shared.Inventory.Events;
using Content.Shared.Mobs;
using Content.Shared.Overlays.Switchable;
using Content.Shared._ClawCommand.Flashbang;

namespace Content.Shared._ClawCommand.Inventory;

public partial class GoobInventorySystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventorySystem = default!;

    public void InitializeRelays()
    {
        base.Initialize();
        /*SubscribeLocalEvent<InventoryComponent, DelayedKnockdownAttemptEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, VaporCheckEyeProtectionEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, CheckMagicItemEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, GetStandingUpTimeMultiplierEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, GetSpellInvocationEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, GetMessagePostfixEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, ClothingAutoInjectRelayedEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, ModifyStunTimeEvent>(RefRelayInventoryEvent);*/
        SubscribeLocalEvent<InventoryComponent, GetFlashbangedEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, FlashDurationMultiplierEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, IsEyesCoveredCheckEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, RefreshEquipmentHudEvent<NightVisionComponent>>(RefRelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, TakeStaminaDamageEvent>(RelayInventoryEvent);
        SubscribeLocalEvent<InventoryComponent, RefreshEquipmentHudEvent<ThermalVisionComponent>>(RefRelayInventoryEvent);
    }
    private void RefRelayInventoryEvent<T>(EntityUid uid, InventoryComponent component, ref T args) where T : IInventoryRelayEvent
    {
        _inventorySystem.RelayEvent((uid, component), ref args);
    }

    private void RelayInventoryEvent<T>(EntityUid uid, InventoryComponent component, T args) where T : IInventoryRelayEvent
    {
        _inventorySystem.RelayEvent((uid, component), args);
    }
}
