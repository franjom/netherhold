// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.Simulation.Players;

namespace DK2.Simulation.Tests;

/// <summary>The detached player protocol (campaign player plan, task 1): typed identities, copied
/// read-only observation storage and a closed action family that carries no engine reference.</summary>
public class PlayerContractsTests
{
    private static readonly PlayerRunId Run = new("run-a");

    private static PlayerStamp Stamp(long revision = 1) =>
        new(Run, new PlayerId(3), new GameTick(revision), new PlayerRevision(revision));

    private static PlayerThingHandle Handle(int ordinal) => new(new PlayerRevision(1), ordinal);

    [Fact]
    public void IdentitiesRejectEmptyMalformedAndNonPositiveValues()
    {
        Assert.Throws<ArgumentException>(() => new PlayerRunId(""));
        Assert.Throws<ArgumentException>(() => new PlayerRunId("  "));
        Assert.Throws<ArgumentException>(() => new PlayerRunId("a/b"));
        Assert.Throws<ArgumentException>(() => new PlayerRunId("a\nb"));
        Assert.Throws<ArgumentException>(() => new PlayerRunId(new string('a', PlayerRunId.MaxLength + 1)));
        Assert.Throws<ArgumentNullException>(() => new PlayerRunId(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerActionId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerActionId(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerRevision(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerThingHandle(new PlayerRevision(1), -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerThingHandle(default, 0));
        Assert.Equal("Run_1.a-b", new PlayerRunId("Run_1.a-b").Value);
        Assert.Equal(new string('a', PlayerRunId.MaxLength), new PlayerRunId(new string('a', PlayerRunId.MaxLength)).Value);
        Assert.NotEqual(new PlayerRunId("run-a"), new PlayerRunId("run-b"));
        Assert.Equal(new PlayerRunId("run-a"), new PlayerRunId("run-a"));
    }

    [Fact]
    public void DefaultIdentitiesAreInvalidNotNull()
    {
        Assert.False(default(PlayerRunId).IsValid);
        Assert.Equal("", default(PlayerRunId).Value);
        Assert.Equal("", default(PlayerRunId).ToString());
        Assert.False(default(PlayerActionId).IsValid);
        Assert.False(default(PlayerRevision).IsValid);
        Assert.False(default(PlayerThingHandle).IsValid);
        Assert.False(default(PlayerStamp).IsValid);
        Assert.False((Stamp() with { RunId = default }).IsValid);
        Assert.False((Stamp() with { Revision = default }).IsValid);
        Assert.True(Stamp().IsValid);
        Assert.True(new PlayerActionId(1).IsValid);
        Assert.True(Handle(0).IsValid);
    }

    [Fact]
    public void HandlesAreScopedToTheirRevision()
    {
        Assert.NotEqual(Handle(0), new PlayerThingHandle(new PlayerRevision(2), 0));
        Assert.Equal(Handle(0), Handle(0));
        Assert.NotEqual(Handle(0), Handle(1));
    }

    [Fact]
    public void RequestRejectsANullAction()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerRequest(Stamp(), new PlayerActionId(1), null!));
        var request = new PlayerRequest(Stamp(), new PlayerActionId(1), new PlayerAction.SkipIntro());
        Assert.Throws<ArgumentNullException>(() => request with { Action = null! });
        // Default identities are representable, so the host can refuse them rather than crash.
        Assert.False(new PlayerRequest(default, default, new PlayerAction.SkipIntro()).BasedOn.IsValid);
    }

    [Fact]
    public void ReceiptsHoldConsistentStatusReasonAndTick()
    {
        var id = new PlayerActionId(1);
        Assert.Throws<ArgumentException>(() => new PlayerReceipt(id, PlayerReceiptStatus.Refused));
        Assert.Throws<ArgumentException>(() => new PlayerReceipt(id, PlayerReceiptStatus.Cancelled));
        Assert.Throws<ArgumentException>(() => new PlayerReceipt(id, PlayerReceiptStatus.Queued, PlayerRefusalReason.Terminal));
        Assert.Throws<ArgumentException>(() => new PlayerReceipt(id, PlayerReceiptStatus.Submitted));
        Assert.Throws<ArgumentException>(() => new PlayerReceipt(id, PlayerReceiptStatus.Queued, scheduledTick: new GameTick(5)));
        Assert.Equal(new GameTick(5), new PlayerReceipt(id, PlayerReceiptStatus.Submitted, scheduledTick: new GameTick(5)).ScheduledTick);
        Assert.Equal(PlayerRefusalReason.StaleObservation,
            new PlayerReceipt(id, PlayerReceiptStatus.Refused, PlayerRefusalReason.StaleObservation).Reason);
        Assert.Equal(PlayerReceiptStatus.Queued, new PlayerReceipt(id, PlayerReceiptStatus.Queued).Status);
    }

    [Fact]
    public void VisibleThingsCarryOnlyTheirKindsTypeId()
    {
        Assert.Throws<ArgumentException>(() => new PlayerVisibleThing(Handle(0), PlayerThingKind.Creature, default, default, default));
        Assert.Throws<ArgumentException>(() => new PlayerVisibleThing(Handle(0), PlayerThingKind.Creature, default, default, default,
            new CreatureTypeId(1), new ObjectTypeId(2)));
        Assert.Throws<ArgumentException>(() => new PlayerVisibleThing(Handle(0), PlayerThingKind.Special, default, default, default));
        Assert.Throws<ArgumentException>(() => new PlayerVisibleThing(Handle(0), PlayerThingKind.Door, default, default, default,
            objectType: new ObjectTypeId(2)));
        Assert.Throws<ArgumentException>(() => new PlayerVisibleThing(default, PlayerThingKind.Door, default, default, default));
        Assert.Equal(new ObjectTypeId(9), new PlayerVisibleThing(Handle(0), PlayerThingKind.Special, default, default,
            PlayerThingActions.Activate, objectType: new ObjectTypeId(9)).ObjectType);
        Assert.Null(new PlayerVisibleThing(Handle(0), PlayerThingKind.StoreGold, default, default, PlayerThingActions.PickUp).CreatureType);
    }

    [Fact]
    public void ObservationOwnsItsCollections()
    {
        var rooms = new List<PlayerRoomSummary> { new(RoomTypeId.Treasury, 9) };
        var creatures = new List<PlayerCreatureSummary> { new(new CreatureTypeId(1), new TilePos(2, 2), false) };
        var things = new List<PlayerVisibleThing>
        {
            new(Handle(0), PlayerThingKind.Creature, new TilePos(2, 2), new PlayerId(3), PlayerThingActions.PickUp, new CreatureTypeId(1)),
        };
        var notices = new List<PlayerNotice> { new(new GameTick(1), 7) };
        PlayerReceipt[] receipts = [new(new PlayerActionId(1), PlayerReceiptStatus.Queued)];
        var roomOffers = new List<PlayerRoomOffer> { new(RoomTypeId.Treasury, new GoldAmount(50), true) };
        var panels = new List<GuiButtonTarget> { GuiButtonTarget.Room(RoomTypeId.Treasury) };
        var tiles = new PlayerTile[4];
        tiles[0] = new PlayerTile(PlayerTileFlags.Known | PlayerTileFlags.Solid, default, default);

        var observation = new PlayerObservation
        {
            Stamp = Stamp(),
            Tiles = new PlayerTileGrid(2, 2, tiles),
            Rooms = rooms,
            Creatures = creatures,
            Things = things,
            Notices = notices,
            Receipts = receipts,
            Offers = new PlayerOffers { Rooms = roomOffers, Panels = panels },
        };

        rooms.Add(new PlayerRoomSummary(RoomTypeId.Lair, 9));
        creatures.Clear();
        things.Clear();
        notices.Clear();
        receipts[0] = new PlayerReceipt(new PlayerActionId(2), PlayerReceiptStatus.Queued);
        roomOffers.Clear();
        panels.Clear();
        tiles[0] = default;

        Assert.Single(observation.Rooms);
        Assert.Single(observation.Creatures);
        Assert.Single(observation.Things);
        Assert.Single(observation.Notices);
        Assert.Equal(new PlayerActionId(1), Assert.Single(observation.Receipts).Id);
        Assert.Single(observation.Offers.Rooms);
        Assert.Single(observation.Offers.Panels);
        Assert.Equal(PlayerTileFlags.Known | PlayerTileFlags.Solid, observation.Tiles[new TilePos(0, 0)].Flags);
        // Read-only storage: a consumer cannot cast a list back to the array or list it came from.
        AssertReadOnly(observation.Rooms);
        AssertReadOnly(observation.Creatures);
        AssertReadOnly(observation.Things);
        AssertReadOnly(observation.Notices);
        AssertReadOnly(observation.Receipts);
        AssertReadOnly(observation.Offers.Rooms);
        AssertReadOnly(observation.Offers.Panels);
    }

    private static void AssertReadOnly<T>(IReadOnlyList<T> list)
    {
        Assert.False(list is T[]);
        Assert.False(list is List<T>);
        Assert.True(((ICollection<T>)list).IsReadOnly);
    }

    [Fact]
    public void ObservationRejectsNullParts()
    {
        Assert.Throws<ArgumentNullException>(() => new PlayerObservation { Stamp = Stamp(), Tiles = null! });
        Assert.Throws<ArgumentNullException>(() => new PlayerObservation { Stamp = Stamp(), Offers = null! });
        Assert.Throws<ArgumentNullException>(() => new PlayerObservation { Stamp = Stamp(), Rooms = null! });
        Assert.Throws<ArgumentNullException>(() => new PlayerOffers { Panels = null! });
    }

    [Fact]
    public void EmptyObservationIsValid()
    {
        var observation = new PlayerObservation { Stamp = Stamp() };

        Assert.Equal(1, PlayerObservation.SchemaVersion);
        Assert.Equal(PlayerPhase.Playing, observation.Phase);
        Assert.Equal(PlayerResult.Playing, observation.Result);
        Assert.Empty(observation.Rooms);
        Assert.Empty(observation.Creatures);
        Assert.Empty(observation.Things);
        Assert.Empty(observation.Notices);
        Assert.Empty(observation.Receipts);
        Assert.Empty(observation.Offers.Rooms);
        Assert.Equal(PlayerActionKinds.None, observation.Offers.Actions);
        Assert.Equal(0, observation.Tiles.Width);
    }

    [Fact]
    public void TileGridRejectsAMismatchedOrOverflowingSizeAndOutOfRangeReads()
    {
        Assert.Throws<ArgumentException>(() => new PlayerTileGrid(2, 2, new PlayerTile[3]));
        Assert.Throws<ArgumentException>(() => new PlayerTileGrid(65536, 65536, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerTileGrid(-1, 2, []));
        var grid = new PlayerTileGrid(2, 1, new PlayerTile[2]);
        Assert.True(grid.Contains(new TilePos(1, 0)));
        Assert.False(grid.Contains(new TilePos(2, 0)));
        Assert.False(grid.Contains(new TilePos(-1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => grid[new TilePos(0, 1)]);
    }

    [Fact]
    public void ObservationHasNoPrivilegedReferences()
    {
        var offenders = new List<string>();
        var seen = new HashSet<Type>();
        AuditType(typeof(PlayerObservation), seen, offenders, "PlayerObservation");
        AuditType(typeof(PlayerRequest), seen, offenders, "PlayerRequest");
        foreach (Type action in ActionTypes())
            AuditType(action, seen, offenders, action.Name);
        Assert.Empty(offenders);

        // Positive control: each branch of the audit flags its own leak.
        var control = new List<string>();
        AuditMembers(typeof(LeakyControl), [], control, nameof(LeakyControl));
        Assert.Equal(
        [
            "LeakyControl.Anything: System.Object",
            "LeakyControl.Array: array Int32[]",
            "LeakyControl.Deferred: collection IEnumerable`1",
            "LeakyControl.Definition: DK2.Data.Model.CreatureDef",
            "LeakyControl.Field: public field",
            "LeakyControl.Handler: delegate",
            "LeakyControl.Mutable: collection List`1",
            "LeakyControl.Settable: settable",
            "LeakyControl.World: DK2.Simulation.WorldState",
        ], control.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ActionFamilyIsClosed()
    {
        Type[] actions = ActionTypes();
        Assert.Equal(
            ["ActivateSpecial", "BuildRoom", "CreateImpAt", "DropAt", "PickUp", "SelectPanel", "SkipIntro", "Slap", "TagArea"],
            actions.Select(t => t.Name).Order(StringComparer.Ordinal));
        Assert.All(actions, t => Assert.True(t.IsSealed, t.Name));
        // A record keeps a protected copy constructor, so closure rests on the abstract Family
        // getter, which only this assembly can override: a type elsewhere cannot derive.
        MethodInfo family = Assert.Single(typeof(PlayerAction).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic),
            m => m.IsAbstract);
        Assert.Equal("get_Family", family.Name);
        Assert.True(family.IsFamilyAndAssembly);
        Assert.Equal(PlayerActionKinds.SkipIntro, new PlayerAction.SkipIntro().Kind);
        Assert.Equal(PlayerActionKinds.DropAt, new PlayerAction.DropAt(new TilePos(1, 1)).Kind);
    }

    [Fact]
    public void RequestsCompareByContent()
    {
        var a = new PlayerRequest(Stamp(), new PlayerActionId(1), new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(1, 1), new TilePos(3, 3)));
        var same = new PlayerRequest(Stamp(), new PlayerActionId(1), new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(1, 1), new TilePos(3, 3)));
        var moved = a with { Action = new PlayerAction.BuildRoom(RoomTypeId.Treasury, new TilePos(2, 1), new TilePos(4, 3)) };
        Assert.Equal(a, same);
        Assert.NotEqual(a, moved);
        Assert.NotEqual(a, a with { BasedOn = Stamp(2) });
    }

    private static Type[] ActionTypes() =>
        typeof(PlayerAction).Assembly.GetTypes().Where(t => t.IsSubclassOf(typeof(PlayerAction))).ToArray();

    /// <summary>Allowed: primitives, enums, string, read-only lists of allowed types, value types from
    /// the engine-neutral id namespaces, and the protocol's own types (whose members are walked).</summary>
    private static void AuditType(Type type, HashSet<Type> seen, List<string> offenders, string path)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string)) return;
        if (typeof(Delegate).IsAssignableFrom(type)) { offenders.Add($"{path}: delegate"); return; }
        if (type.IsArray) { offenders.Add($"{path}: array {type.Name}"); return; }
        if (type.IsGenericType)
        {
            Type def = type.GetGenericTypeDefinition();
            if (def == typeof(Nullable<>)) { AuditType(type.GetGenericArguments()[0], seen, offenders, path); return; }
            if (def == typeof(IReadOnlyList<>)) { AuditType(type.GetGenericArguments()[0], seen, offenders, path + "[]"); return; }
            offenders.Add($"{path}: collection {type.Name}");
            return;
        }
        if (typeof(IEnumerable).IsAssignableFrom(type)) { offenders.Add($"{path}: enumerable {type.Name}"); return; }
        string ns = type.Namespace ?? "";
        bool protocol = ns == "DK2.Simulation.Players";
        bool neutralValue = type.IsValueType && (ns is "DK2.Core" or "DK2.Data.Model" || type == typeof(GuiButtonTarget));
        if (!protocol && !neutralValue) { offenders.Add($"{path}: {type.FullName}"); return; }
        if (seen.Add(type)) AuditMembers(type, seen, offenders, path);
    }

    private static void AuditMembers(Type type, HashSet<Type> seen, List<string> offenders, string path)
    {
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            offenders.Add($"{path}.{field.Name}: public field");
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.GetIndexParameters().Length > 0 || property.Name == "EqualityContract") continue;
            MethodInfo? setter = property.SetMethod;
            if (setter is { IsPublic: true }
                // By name: a netstandard2.1 assembly marks init with its own polyfilled IsExternalInit.
                && !setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == typeof(IsExternalInit).FullName))
                offenders.Add($"{path}.{property.Name}: settable");
            else
                AuditType(property.PropertyType, seen, offenders, $"{path}.{property.Name}");
        }
    }

#pragma warning disable CS0649 // never assigned: the audit only reads the declarations
    private sealed class LeakyControl
    {
        public WorldState? World { get; init; }
        public CreatureDef? Definition { get; init; }
        public List<int>? Mutable { get; init; }
        public Action? Handler { get; init; }
        public int[]? Array { get; init; }
        public IEnumerable<int>? Deferred { get; init; }
        public object? Anything { get; init; }
        public int Settable { get; set; }
        public int Field;
    }
#pragma warning restore CS0649
}
