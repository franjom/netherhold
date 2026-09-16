// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Text;
using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation;

/// <summary>
/// Binary save/load for the simulation. The format is entirely our own: DK2's
/// save layout is not a target (plan risk R13), because reproducing it buys
/// nothing behavioural and our state is not shaped like the original's.
/// <para>The invariant is <b>snapshot a run, restore it into a fresh simulation,
/// and both continue to produce identical state hashes forever after</b> —
/// stronger than "the bytes match", because it proves the snapshot captures
/// everything the simulation depends on, RNG position included
/// (<c>SnapshotTests</c>). Anything reachable from <see cref="WorldState"/> must
/// survive the round trip.</para>
/// <para>The static <see cref="GameDatabase"/> is deliberately NOT stored: it is
/// derived from the original game's files and is supplied on restore.</para></summary>
public static class WorldSnapshot
{
    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes("DK2S");
    /// <summary>
    /// Kept equal to <c>GameSimulation.HashVersion</c>. Changing simulation state
    /// breaks the pinned hash by design: bump both and update the literal in
    /// <c>DeterminismTests.StateHash_IsStableAcrossBuildsAndMachines</c> in the
    /// same commit, and never loosen that assertion.
    /// </summary>
    private const int FormatVersion = 61;

    public static void Write(Stream stream, GameSimulation simulation)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        w.Write(MagicBytes);
        w.Write(FormatVersion);

        WorldState world = simulation.World;
        w.Write(world.CurrentTick.Value);

        (ulong s0, ulong s1, ulong s2, ulong s3) = simulation.Rng.SaveState();
        w.Write(s0);
        w.Write(s1);
        w.Write(s2);
        w.Write(s3);

        w.Write(world.Width);
        w.Write(world.Height);
        w.Write(world.FogOfWarEnabled);
        w.Write(world.TileCount);
        for (int i = 0; i < world.TileCount; i++)
        {
            ref TileState t = ref world.TileAtIndex(i);
            w.Write(t.TerrainId);
            w.Write(t.OwnerPlayerId);
            w.Write(t.Flags);
            w.Write(t.Health);
            w.Write(t.GoldHeld);
            w.Write(t.BurnTicksRemaining);
            w.Write(t.EntranceIdleTicks);
            w.Write(t.EntranceCreaturesGenerated);
            w.Write(t.HatchTicks);
            w.Write(t.TaggedBy);
            w.Write(t.DiscoveredBy);
            w.Write(t.FreshlyDug);
        }

        w.Write(world.PlayerCount);
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState p = ref world.PlayerAtIndex(i);
            w.Write(p.PlayerId);
            w.Write(p.Mana);
            w.Write(p.GoldMined);
            w.Write(p.KilledByPlayerId);
            w.Write(p.SlapCount);
            w.Write(p.PossessedCreatureId);
            w.Write(p.ResearchPoints);
            w.Write(p.NextFreeImpTick.Value);
            w.Write(p.NextPaydayTick.Value);
            w.Write(p.Hand.Count);
            foreach (HandItem held in p.Hand)
            {
                w.Write((byte)held.Kind);
                w.Write(held.Id);
            }
        }

        // Normally empty at a tick boundary — TileWorkSystem drains it every
        // tick — but it is hashed state, so it is snapshotted state. Anything
        // reachable from WorldState has to survive the round trip or the
        // restore-and-continue invariant is a lie.
        w.Write(world.PendingWork.Count);
        foreach (TileWorkOrder o in world.PendingWork)
        {
            w.Write(o.TileIndex);
            w.Write((int)o.Kind);
            w.Write(o.PlayerId);
            w.Write(o.WorkerCreatureId);
        }

        w.Write(world.NextCreatureId);
        w.Write(world.Creatures.Count);
        foreach (CreatureState c in world.Creatures)
        {
            w.Write(c.Id);
            w.Write(c.CreatureTypeId);
            w.Write(c.OwnerPlayerId);
            w.Write(c.TriggerId);
            w.Write(c.TriggerRepeat);
            w.Write(c.PosX);
            w.Write(c.PosY);
            w.Write(c.MoveRemainder);
            w.Write(c.GoldHeld);
            w.Write(c.Health);
            w.Write(c.HungerTicks);
            w.Write(c.HungerNeed);
            w.Write(c.FoodAttemptTicks);
            w.Write(c.AwakeTicks);
            w.Write(c.SleepTicks);
            w.Write(c.DyingTicks);
            w.Write(c.Anger.Company);
            w.Write(c.Anger.Hunger);
            w.Write(c.Anger.Lair);
            w.Write(c.Anger.Work);
            w.Write(c.Anger.Pay);
            w.Write(c.Anger.Treatment);
            w.Write(c.WageOwed);
            w.Write(c.PayAttemptsFailed);
            w.Write(c.SkipNextPayment);
            w.Write(c.DieInstantly);
            w.Write(c.Level);
            w.Write(c.ExperiencePoints);
            w.Write((int)c.Mood);
            w.Write(c.LastAngryChoiceTick);
            w.Write(c.LastHappyChoiceTick);
            w.Write(c.LastUnhappyChoiceTick);
            w.Write((int)c.CurrentJob);
            w.Write(c.JobTileIndex);
            w.Write(c.InHand);
            w.Write(c.PickupTick);
            w.Write(c.LastSlapTick);
            w.Write(c.WorkFasterTicks);
            w.Write(c.JustDropped);
            w.Write((byte)c.DroppedTask);
            w.Write((byte)c.LastTaskKind);
            w.Write(c.CarriedCreatureId);
            w.Write(c.CarriedByCreatureId);
            w.Write(c.TargetBodyId);
            w.Write(c.PrisonOwnerPlayerId);
            w.Write(c.CarriedObjectId);
            w.Write(c.TargetObjectId);
            w.Write((int)c.Activity);
            w.Write(c.TargetTileIndex);
            w.Write((int)c.TargetWork);
            w.Write(c.TimerTicks);
            w.Write(c.PathIndex);

            // The path is state, not a cache: a creature restored without it
            // would re-plan and could pick a different equally short route, so
            // the restored run would diverge from the one it claims to continue.
            w.Write(c.Path.Count);
            foreach (TilePos p in c.Path)
            {
                w.Write(p.X);
                w.Write(p.Y);
            }
        }

        w.Write(world.NextObjectId);
        w.Write(world.Objects.Count);
        foreach (ObjectState o in world.Objects)
        {
            w.Write(o.Id);
            w.Write(o.ObjectTypeId);
            w.Write(o.OwnerPlayerId);
            w.Write(o.TileIndex);
            w.Write(o.CreatureId);
            w.Write(o.GoldHeld);
            w.Write(o.CarriedByCreatureId);
            w.Write(o.TimerTicks);
            w.Write(o.InHand);
            w.Write(o.AutoPlaced);
        }

        w.Write(world.ActionPoints.Count);
        foreach (ActionPointState actionPoint in world.ActionPoints)
        {
            w.Write(actionPoint.Id);
            w.Write(actionPoint.StartX);
            w.Write(actionPoint.StartY);
            w.Write(actionPoint.EndX);
            w.Write(actionPoint.EndY);
            w.Write(actionPoint.TriggerId);
            w.Write(actionPoint.NextWaypointId);
            w.Write(actionPoint.Flags);
            w.Write(actionPoint.PassageCount);
            w.Write(actionPoint.WaitTimerTicks);
        }

        w.Write(world.Doors.Count);
        foreach (DoorState door in world.Doors)
        {
            w.Write(door.X);
            w.Write(door.Y);
            w.Write(door.Unknown_0008);
            w.Write(door.TriggerId);
            w.Write(door.DoorId);
            w.Write(door.PlayerId);
            w.Write(door.Flags);
            w.Write(door.UnderConstruction);
            w.Write(door.Unknown_0011.ToArray());
        }

        w.Write(world.Traps.Count);
        foreach (TrapState trap in world.Traps)
        {
            w.Write(trap.X);
            w.Write(trap.Y);
            w.Write(trap.Unknown_0008);
            w.Write(trap.NumberOfShots);
            w.Write(trap.TrapId);
            w.Write(trap.PlayerId);
            w.Write(trap.Unknown_000F);
            w.Write(trap.UnderConstruction);
        }

        w.Write(world.HeroParties.Count);
        foreach (HeroPartyState party in world.HeroParties)
        {
            w.Write(party.Name);
            w.Write(party.TriggerId);
            w.Write(party.PartyId);
            w.Write(party.Unknown_0023);
            w.Write(party.Unknown_0027);
            w.Write(party.Members.Count);
            foreach (HeroPartyMemberState member in party.Members)
            {
                w.Write(member.X); w.Write(member.Y); w.Write(member.Z);
                w.Write(member.GoldHeld); w.Write(member.Level); w.Write(member.Flags);
                w.Write(member.ObjectiveTargetActionPointId); w.Write(member.InitialHealth);
                w.Write(member.TriggerId); w.Write(member.ObjectiveTargetPlayerId);
                w.Write(member.Objective); w.Write(member.CreatureId);
                w.Write(member.Unknown_001D.ToArray()); w.Write(member.Flags2);
            }
        }

        w.Write(world.EffectGenerators.Count);
        foreach (EffectGeneratorState generator in world.EffectGenerators)
        {
            w.Write(generator.X); w.Write(generator.Y);
            w.Write(generator.Unknown_0008); w.Write(generator.Unknown_000C);
            w.Write(generator.Unknown_0010); w.Write(generator.Unknown_0012);
            w.Write(generator.EffectIds.Count);
            foreach (ushort effectId in generator.EffectIds) w.Write(effectId);
            w.Write(generator.Frequency); w.Write(generator.GeneratorId);
            w.Write(generator.Unknown_001E.ToArray());
        }

        w.Write(world.TempleSacrifices.Count);
        foreach (TempleSacrificeState temple in world.TempleSacrifices)
        {
            w.Write(temple.AnchorTileIndex);
            w.Write(temple.OwnerPlayerId);
            w.Write(temple.Count);
            w.Write(temple.NextInsertionIndex);
            for (int i = 0; i < TempleSacrificeState.Capacity; i++)
            {
                SacrificeIngredient ingredient = temple.Slot(i);
                w.Write(ingredient.Type);
                w.Write(ingredient.Id);
                w.Write(ingredient.CreatureLevel);
            }
        }

        w.Write(world.Graveyards.Count);
        foreach (GraveyardState graveyard in world.Graveyards)
        {
            w.Write(graveyard.AnchorTileIndex);
            w.Write(graveyard.OwnerPlayerId);
            w.Write(graveyard.DecomposeValue);
        }

        // The trigger banks. Written whole rather than sparsely: they are a
        // fixed 128 bytes and 16 dwords, and a sparse form would have to
        // encode which slots are set, which costs more than the slots.
        for (int i = 0; i < TriggerState.FlagCount; i++) w.Write(world.Triggers.RawFlags[i]);
        for (int i = 0; i < TriggerState.TimerCount; i++) w.Write(world.Triggers.RawTimers[i]);
        w.Write(world.Triggers.SpecialValue);
        w.Write(world.Triggers.SpecialValueWritten);
        KeyValuePair<(int Tag, ushort Id), TriggerRecordState>[] triggerRecords =
            world.Triggers.RawRecords.ToArray();
        w.Write(triggerRecords.Length);
        foreach (((int tag, ushort id), TriggerRecordState record) in triggerRecords)
        {
            w.Write(tag);
            w.Write(id);
            w.Write(record.Active);
            w.Write(record.Repeat);
        }
    }

    public static byte[] ToBytes(GameSimulation simulation)
    {
        using var ms = new MemoryStream();
        Write(ms, simulation);
        return ms.ToArray();
    }

    public static GameSimulation Read(
        Stream stream,
        GameDatabase database,
        IEnumerable<ISimulationSystem>? systems = null,
        TriggerTree? triggers = null)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        byte[] magic = r.ReadBytes(MagicBytes.Length);
        if (magic.Length != MagicBytes.Length || !magic.SequenceEqual(MagicBytes))
            throw new InvalidDataException("Not a DK2 simulation snapshot (bad magic).");

        int version = r.ReadInt32();
        if (version != FormatVersion)
            throw new InvalidDataException(
                $"Snapshot format version {version} is not supported (this build reads {FormatVersion}).");

        var tick = new GameTick(r.ReadInt64());
        SimRandom rng = SimRandom.Restore((r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64(), r.ReadUInt64()));

        int width = r.ReadInt32();
        int height = r.ReadInt32();
        bool fogOfWarEnabled = r.ReadBoolean();
        int tileCount = r.ReadInt32();
        if (tileCount != width * height)
            throw new InvalidDataException(
                $"Snapshot claims {width}x{height} but carries {tileCount} tiles.");

        var tiles = new TileState[tileCount];
        for (int i = 0; i < tileCount; i++)
        {
            tiles[i] = new TileState
            {
                TerrainId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                Flags = r.ReadByte(),
                Health = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                BurnTicksRemaining = r.ReadInt32(),
                EntranceIdleTicks = r.ReadInt32(),
                EntranceCreaturesGenerated = r.ReadInt32(),
                HatchTicks = r.ReadInt32(),
                TaggedBy = r.ReadUInt16(),
                DiscoveredBy = r.ReadUInt16(),
                FreshlyDug = r.ReadBoolean(),
            };
        }

        int playerCount = r.ReadInt32();
        var players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            players[i] = new PlayerState
            {
                PlayerId = r.ReadByte(),
                Mana = r.ReadInt32(),
                GoldMined = r.ReadInt32(),
                KilledByPlayerId = r.ReadByte(),
                SlapCount = r.ReadInt32(),
                PossessedCreatureId = r.ReadInt32(),
                ResearchPoints = r.ReadInt32(),
                NextFreeImpTick = new GameTick(r.ReadInt64()),
                NextPaydayTick = new GameTick(r.ReadInt64()),
                Hand = [],
            };

            int handCount = r.ReadInt32();
            for (int h = 0; h < handCount; h++)
            {
                var kind = (HandItemKind)r.ReadByte();
                players[i].Hand.Add(new HandItem(kind, r.ReadInt32()));
            }
        }

        WorldState world = WorldState.FromParts(width, height, tiles, players, fogOfWarEnabled);
        world.CurrentTick = tick;

        int workCount = r.ReadInt32();
        for (int i = 0; i < workCount; i++)
        {
            world.QueueWork(new TileWorkOrder(
                r.ReadInt32(), (TileWorkKind)r.ReadInt32(), r.ReadByte(), r.ReadInt32()));
        }

        int nextCreatureId = r.ReadInt32();
        int creatureCount = r.ReadInt32();
        for (int i = 0; i < creatureCount; i++)
        {
            var creature = new CreatureState
            {
                Id = r.ReadInt32(),
                CreatureTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                TriggerId = r.ReadUInt16(),
                TriggerRepeat = r.ReadByte(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                MoveRemainder = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                Health = r.ReadInt32(),
                HungerTicks = r.ReadInt32(),
                HungerNeed = r.ReadInt32(),
                FoodAttemptTicks = r.ReadInt32(),
                AwakeTicks = r.ReadInt32(),
                SleepTicks = r.ReadInt32(),
                DyingTicks = r.ReadInt32(),
                Anger = new CreatureAnger
                {
                    Company = r.ReadInt32(),
                    Hunger = r.ReadInt32(),
                    Lair = r.ReadInt32(),
                    Work = r.ReadInt32(),
                    Pay = r.ReadInt32(),
                    Treatment = r.ReadInt32(),
                },
                WageOwed = r.ReadInt32(),
                PayAttemptsFailed = r.ReadInt32(),
                SkipNextPayment = r.ReadBoolean(),
                DieInstantly = r.ReadBoolean(),
                Level = r.ReadInt32(),
                ExperiencePoints = r.ReadInt32(),
                Mood = (CreatureMood)r.ReadInt32(),
                LastAngryChoiceTick = r.ReadInt64(),
                LastHappyChoiceTick = r.ReadInt64(),
                LastUnhappyChoiceTick = r.ReadInt64(),
                CurrentJob = (JobType)r.ReadInt32(),
                JobTileIndex = r.ReadInt32(),
                InHand = r.ReadBoolean(),
                PickupTick = r.ReadInt64(),
                LastSlapTick = r.ReadInt64(),
                WorkFasterTicks = r.ReadInt32(),
                JustDropped = r.ReadBoolean(),
                DroppedTask = (ImpTaskKind)r.ReadByte(),
                LastTaskKind = (ImpTaskKind)r.ReadByte(),
                CarriedCreatureId = r.ReadInt32(),
                CarriedByCreatureId = r.ReadInt32(),
                TargetBodyId = r.ReadInt32(),
                PrisonOwnerPlayerId = r.ReadInt32(),
                CarriedObjectId = r.ReadInt32(),
                TargetObjectId = r.ReadInt32(),
                Activity = (CreatureActivity)r.ReadInt32(),
                TargetTileIndex = r.ReadInt32(),
                TargetWork = (TileWorkKind)r.ReadInt32(),
                TimerTicks = r.ReadInt32(),
                PathIndex = r.ReadInt32(),
            };

            int pathLength = r.ReadInt32();
            for (int p = 0; p < pathLength; p++)
                creature.Path.Add(new TilePos(r.ReadInt32(), r.ReadInt32()));

            world.RawCreatures.Add(creature);
        }

        // Creatures are put back with their stored ids rather than re-spawned,
        // so the counter is restored rather than derived: a level that has had
        // creatures die must not hand out an id it has already used.
        world.NextCreatureId = nextCreatureId;

        int nextObjectId = r.ReadInt32();
        int objectCount = r.ReadInt32();
        for (int i = 0; i < objectCount; i++)
        {
            world.RawObjects.Add(new ObjectState
            {
                Id = r.ReadInt32(),
                ObjectTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                TileIndex = r.ReadInt32(),
                CreatureId = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                CarriedByCreatureId = r.ReadInt32(),
                TimerTicks = r.ReadInt32(),
                InHand = r.ReadBoolean(),
                AutoPlaced = r.ReadBoolean(),
            });
        }

        // Restored rather than re-derived, for the reason the creature counter
        // is: a bed destroyed when its owner died must not have its id handed
        // out again to the next bed.
        world.NextObjectId = nextObjectId;

        int actionPointCount = r.ReadInt32();
        for (int i = 0; i < actionPointCount; i++)
        {
            world.RawActionPoints.Add(new ActionPointState
            {
                Id = r.ReadByte(),
                StartX = r.ReadUInt16(),
                StartY = r.ReadUInt16(),
                EndX = r.ReadUInt16(),
                EndY = r.ReadUInt16(),
                TriggerId = r.ReadUInt16(),
                NextWaypointId = r.ReadByte(),
                Flags = r.ReadByte(),
                PassageCount = r.ReadUInt16(),
                WaitTimerTicks = r.ReadUInt16(),
            });
        }

        int doorCount = r.ReadInt32();
        for (int i = 0; i < doorCount; i++)
        {
            world.RawDoors.Add(new DoorState
            {
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Unknown_0008 = r.ReadInt32(),
                TriggerId = r.ReadUInt16(),
                DoorId = r.ReadByte(),
                PlayerId = r.ReadByte(),
                Flags = r.ReadByte(),
                UnderConstruction = r.ReadBoolean(),
                Unknown_0011 = r.ReadBytes(DoorState.Unknown0011Length),
            });
        }

        int trapCount = r.ReadInt32();
        for (int i = 0; i < trapCount; i++)
        {
            world.RawTraps.Add(new TrapState
            {
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Unknown_0008 = r.ReadInt32(),
                NumberOfShots = r.ReadByte(),
                TrapId = r.ReadByte(),
                PlayerId = r.ReadByte(),
                Unknown_000F = r.ReadByte(),
                UnderConstruction = r.ReadBoolean(),
            });
        }

        int heroPartyCount = r.ReadInt32();
        for (int i = 0; i < heroPartyCount; i++)
        {
            string name = r.ReadString();
            ushort triggerId = r.ReadUInt16();
            byte partyId = r.ReadByte();
            int unknown0023 = r.ReadInt32();
            int unknown0027 = r.ReadInt32();
            int memberCount = r.ReadInt32();
            var members = new HeroPartyMemberState[memberCount];
            for (int m = 0; m < memberCount; m++)
            {
                members[m] = new HeroPartyMemberState
                {
                    X = r.ReadInt32(), Y = r.ReadInt32(), Z = r.ReadInt32(),
                    GoldHeld = r.ReadUInt16(), Level = r.ReadByte(), Flags = r.ReadByte(),
                    ObjectiveTargetActionPointId = r.ReadInt32(), InitialHealth = r.ReadInt32(),
                    TriggerId = r.ReadUInt16(), ObjectiveTargetPlayerId = r.ReadByte(),
                    Objective = r.ReadByte(), CreatureId = r.ReadByte(),
                    Unknown_001D = r.ReadBytes(HeroPartyMemberState.Unknown001DLength),
                    Flags2 = r.ReadByte(),
                };
            }
            world.RawHeroParties.Add(new HeroPartyState
            {
                Name = name, TriggerId = triggerId, PartyId = partyId,
                Unknown_0023 = unknown0023, Unknown_0027 = unknown0027, Members = members,
            });
        }

        int effectGeneratorCount = r.ReadInt32();
        for (int i = 0; i < effectGeneratorCount; i++)
        {
            int x = r.ReadInt32(); int y = r.ReadInt32();
            int unknown0008 = r.ReadInt32(); int unknown000C = r.ReadInt32();
            ushort unknown0010 = r.ReadUInt16(); ushort unknown0012 = r.ReadUInt16();
            int effectIdCount = r.ReadInt32();
            var effectIds = new ushort[effectIdCount];
            for (int e = 0; e < effectIdCount; e++) effectIds[e] = r.ReadUInt16();
            world.RawEffectGenerators.Add(new EffectGeneratorState
            {
                X = x, Y = y, Unknown_0008 = unknown0008, Unknown_000C = unknown000C,
                Unknown_0010 = unknown0010, Unknown_0012 = unknown0012, EffectIds = effectIds,
                Frequency = r.ReadByte(), GeneratorId = r.ReadByte(),
                Unknown_001E = r.ReadBytes(EffectGeneratorState.Unknown001ELength),
            });
        }

        int templeCount = r.ReadInt32();
        for (int i = 0; i < templeCount; i++)
        {
            var temple = new TempleSacrificeState(r.ReadInt32(), r.ReadByte());
            int count = r.ReadInt32();
            int nextInsertionIndex = r.ReadInt32();
            var entries = new SacrificeIngredient[TempleSacrificeState.Capacity];
            for (int slot = 0; slot < entries.Length; slot++)
                entries[slot] = new SacrificeIngredient(r.ReadByte(), r.ReadByte(), r.ReadInt32());
            temple.Restore(count, nextInsertionIndex, entries);
            world.RawTempleSacrifices.Add(temple);
        }

        int graveyardCount = r.ReadInt32();
        for (int i = 0; i < graveyardCount; i++)
        {
            var graveyard = new GraveyardState(r.ReadInt32(), r.ReadByte());
            graveyard.Restore(r.ReadInt32());
            world.RawGraveyards.Add(graveyard);
        }

        for (int i = 0; i < TriggerState.FlagCount; i++) world.Triggers.RawFlags[i] = r.ReadByte();
        for (int i = 0; i < TriggerState.TimerCount; i++) world.Triggers.RawTimers[i] = r.ReadInt32();
        world.Triggers.SpecialValue = r.ReadInt32();
        world.Triggers.SpecialValueWritten = r.ReadBoolean();
        int triggerRecordCount = r.ReadInt32();
        for (int i = 0; i < triggerRecordCount; i++)
            world.Triggers.RestoreRecord(r.ReadInt32(), r.ReadUInt16(), r.ReadBoolean(), r.ReadByte());

        return new GameSimulation(database, world, rng, systems, triggers);
    }

    public static GameSimulation FromBytes(
        byte[] bytes,
        GameDatabase database,
        IEnumerable<ISimulationSystem>? systems = null,
        TriggerTree? triggers = null)
    {
        using var ms = new MemoryStream(bytes, writable: false);
        return Read(ms, database, systems, triggers);
    }
}
