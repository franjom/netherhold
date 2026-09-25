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
    private const int FormatVersion = 104;

    /// <summary>The steering body's surviving fields, in the order <see cref="Steering.SteeringBody.HashInto"/> folds them.</summary>
    private static void WriteBody(BinaryWriter w, Steering.SteeringBody body)
    {
        WriteVector(w, body.Vel);
        WriteVector(w, body.LocalXAxis);
        WriteVector(w, body.LocalYAxis);
        WriteVector(w, body.PathTargetPos);
        for (int i = 0; i < Steering.SteeringBody.WindowSize; i++)
        {
            WriteVector(w, body.Subpath[i]);
            w.Write(body.Segment[i]);
        }

        w.Write(body.PathTotalDist);
        w.Write(body.PathSubpathDist);
        w.Write(body.PathTailDist);
        w.Write(body.PathLastProgress);
        w.Write(body.PathStuckBuffer);
        w.Write(body.PathTotalCount);
        w.Write(body.PathFlags);
        w.Write(body.PathBufferCount);
        w.Write(body.SubpathIndex);
        w.Write(body.PathFinished);
        w.Write(body.IsBlocker);
        w.Write(body.IgnoreBlocker);
        w.Write(body.EscapeStuckMode);
    }

    private static void ReadBody(BinaryReader r, Steering.SteeringBody body)
    {
        body.Vel = ReadVector(r);
        body.LocalXAxis = ReadVector(r);
        body.LocalYAxis = ReadVector(r);
        body.PathTargetPos = ReadVector(r);
        for (int i = 0; i < Steering.SteeringBody.WindowSize; i++)
        {
            body.Subpath[i] = ReadVector(r);
            body.Segment[i] = r.ReadInt32();
        }

        body.PathTotalDist = r.ReadInt32();
        body.PathSubpathDist = r.ReadInt32();
        body.PathTailDist = r.ReadInt32();
        body.PathLastProgress = r.ReadInt32();
        body.PathStuckBuffer = r.ReadUInt16();
        body.PathTotalCount = r.ReadByte();
        body.PathFlags = r.ReadByte();
        body.PathBufferCount = r.ReadInt32();
        body.SubpathIndex = r.ReadInt32();
        body.PathFinished = r.ReadBoolean();
        body.IsBlocker = r.ReadBoolean();
        body.IgnoreBlocker = r.ReadBoolean();
        body.EscapeStuckMode = r.ReadBoolean();
    }

    private static void WriteVector(BinaryWriter w, Steering.Fixed16Vector v)
    {
        w.Write(v.X);
        w.Write(v.Y);
    }

    private static Steering.Fixed16Vector ReadVector(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());

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
            w.Write(p.Status);
            w.Write(p.StatusTick.Value);
            w.Write(p.HeartHealth);
            w.Write(p.HeartLastAttackerPlayerId);
            w.Write(p.HeartDiedTick.Value);
            w.Write(p.HeartRuined);
            w.Write(p.Availabilities.Count);
            foreach (AvailabilityState availability in p.Availabilities)
            {
                w.Write(availability.Kind);
                w.Write(availability.TypeId);
                w.Write(availability.Value);
            }
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
            w.Write(c.Heading);
            w.Write(c.RouteOptions);
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
            w.Write(c.DroppedOnARoom);
            w.Write(c.NextPayTryTick);
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
            w.Write(c.Dk2StateId);
            w.Write(c.StateBeforeUtilityState);
            w.Write(c.UtilityNewState);
            w.Write(c.HookCountdown);
            w.Write(c.TorturedDuration);
            w.Write(c.TortureTargetTile);
            w.Write(c.WhipTurnsLeft);
            w.Write(c.ContactTileIndex);
            w.Write(c.RescueTurnsLeft);
            w.Write(c.RebelCountdownTicks);
            w.Write(c.RebelAgainstPlayerId);
            w.Write(c.TargetTileIndex);
            w.Write((int)c.TargetWork);
            w.Write(c.TimerTicks);
            w.Write(c.FightTargetId);
            w.Write(c.AttackType);
            w.Write(c.AttackRangeSquared);
            w.Write(c.FightProcessCounter);
            w.Write(c.LastFightScanTileIndex);
            w.Write(c.FightTargetKind);
            w.Write(c.FightTargetPlayerId);
            w.Write(c.FightTargetTileIndex);
            w.Write(c.TimeLastUsedMelee);
            w.Write(c.SwingInProgress);
            w.Write(c.SwingStartTick);
            w.Write(c.SwingTargetKind);
            w.Write(c.SwingTargetId);
            w.Write(c.SwingTargetPlayerId);
            w.Write(c.TempFear);
            w.Write(c.CurrentFear);
            w.Write(c.OverThreatCounter);
            w.Write(c.BackOffTimer);
            w.Write(c.Running);
            w.Write(c.CombatStateEnteredTick);
            w.Write(c.AlarmX);
            w.Write(c.AlarmY);
            w.Write(c.AlarmDeficit);
            w.Write(c.AlarmRoomTile);
            w.Write(c.ReportTicks);
            w.Write(c.AttackSpellSlot);
            w.Write(c.SwingKind);
            w.Write(c.SwingSpellSlot);
            w.Write(c.LastPosX);
            w.Write(c.LastPosY);
            w.Write(c.ArmourTicks);
            w.Write(c.InvulnerableTicks);
            w.Write(c.FrozenTicks);
            w.Write(c.InvisibleTicks);
            w.Write(c.SpeedEffectTicks);
            w.Write(c.SpeedPercent);
            w.Write(c.PoisonTicks);
            w.Write(c.PoisonAmount);
            w.Write(c.PoisonOwner);
            w.Write(c.StunTicks);
            w.Write(c.Approaching);
            w.Write(c.RouteRefreshCountdown);
            w.Write(c.ChaseTicks);
            w.Write(c.RetreatingCreature);
            w.Write(c.CelebrationTicks);
            w.Write(c.BodyTargetId);
            w.Write(c.PartyId);
            w.Write(c.PositionInParty);
            w.Write(c.LeaderId);
            w.Write(c.WanderRadius);
            w.Write(c.Flags);
            w.Write(c.DestroysRooms);
            w.Write(c.IsTool);
            w.Write(c.IsMercenary);
            w.Write((int)c.GoodJob);
            w.Write(c.GoodJobPlayerId);
            w.Write(c.GoodJobActionPointId);
            for (int i = 0; i < c.SpellStamps.Length; i++) w.Write(c.SpellStamps[i]);
            for (int i = 0; i < c.EngagedMelee.Length; i++) w.Write(c.EngagedMelee[i]);
            for (int i = 0; i < c.EngagedRanged.Length; i++) w.Write(c.EngagedRanged[i]);

            // The route window is state, not a cache: a creature restored without it would
            // request again from a different position and the restored run would diverge.
            WriteBody(w, c.Body);
        }

        w.Write(world.NextShotId);
        w.Write(world.Shots.Count);
        foreach (ShotState s in world.Shots)
        {
            w.Write(s.Id);
            w.Write(s.ShotTypeId);
            w.Write(s.OwnerPlayerId);
            w.Write(s.PosX);
            w.Write(s.PosY);
            w.Write(s.VelX);
            w.Write(s.VelY);
            w.Write(s.Health);
            w.Write(s.Age);
            w.Write(s.Data1);
            w.Write(s.Data2);
            w.Write(s.CasterId);
            w.Write(s.TargetKind);
            w.Write(s.TargetId);
            w.Write(s.TargetPlayerId);
            w.Write(s.TargetTileIndex);
            w.Write(s.TargetX);
            w.Write(s.TargetY);
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
            w.Write(o.Setting);
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
            w.Write(door.LockMode);
            w.Write(door.LockPickTicks);
            w.Write(door.Open);
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
            w.Write(party.SpawnCount);
            w.Write(party.Members.Count);
            foreach (HeroPartyMemberState member in party.Members)
            {
                w.Write(member.X); w.Write(member.Y); w.Write(member.Z);
                w.Write(member.GoldHeld); w.Write(member.Level); w.Write(member.Flags);
                w.Write(member.ObjectiveTargetActionPointId); w.Write(member.InitialHealth);
                w.Write(member.TriggerId); w.Write(member.ObjectiveTargetPlayerId);
                w.Write(member.Objective); w.Write(member.CreatureId);
                w.Write(member.WanderRadius); w.Write(member.PartyId); w.Write(member.Flags2);
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
        w.Write(world.Triggers.RawWorldOptions);
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
                Status = r.ReadByte(),
                StatusTick = new GameTick(r.ReadInt64()),
                HeartHealth = r.ReadInt32(),
                HeartLastAttackerPlayerId = r.ReadByte(),
                HeartDiedTick = new GameTick(r.ReadInt64()),
                HeartRuined = r.ReadBoolean(),
                Availabilities = [],
                Hand = [],
            };

            int availabilityCount = r.ReadInt32();
            for (int a = 0; a < availabilityCount; a++)
                players[i].Availabilities.Add(new AvailabilityState(
                    r.ReadUInt16(), r.ReadInt32(), r.ReadInt32()));

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
                Heading = r.ReadInt32(),
                RouteOptions = r.ReadInt32(),
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
                DroppedOnARoom = r.ReadBoolean(),
                NextPayTryTick = r.ReadInt64(),
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
                Dk2StateId = r.ReadInt32(),
                StateBeforeUtilityState = r.ReadInt32(),
                UtilityNewState = r.ReadInt32(),
                HookCountdown = r.ReadInt32(),
                TorturedDuration = r.ReadInt32(),
                TortureTargetTile = r.ReadInt32(),
                WhipTurnsLeft = r.ReadInt32(),
                ContactTileIndex = r.ReadInt32(),
                RescueTurnsLeft = r.ReadInt32(),
                RebelCountdownTicks = r.ReadInt32(),
                RebelAgainstPlayerId = r.ReadByte(),
                TargetTileIndex = r.ReadInt32(),
                TargetWork = (TileWorkKind)r.ReadInt32(),
                TimerTicks = r.ReadInt32(),
                FightTargetId = r.ReadInt32(),
                AttackType = r.ReadByte(),
                AttackRangeSquared = r.ReadInt32(),
                FightProcessCounter = r.ReadByte(),
                LastFightScanTileIndex = r.ReadInt32(),
                FightTargetKind = r.ReadByte(),
                FightTargetPlayerId = r.ReadByte(),
                FightTargetTileIndex = r.ReadInt32(),
                TimeLastUsedMelee = r.ReadInt64(),
                SwingInProgress = r.ReadBoolean(),
                SwingStartTick = r.ReadInt64(),
                SwingTargetKind = r.ReadByte(),
                SwingTargetId = r.ReadInt32(),
                SwingTargetPlayerId = r.ReadByte(),
                TempFear = r.ReadUInt16(),
                CurrentFear = r.ReadUInt16(),
                OverThreatCounter = r.ReadByte(),
                BackOffTimer = r.ReadByte(),
                Running = r.ReadBoolean(),
                CombatStateEnteredTick = r.ReadInt64(),
                AlarmX = r.ReadInt32(),
                AlarmY = r.ReadInt32(),
                AlarmDeficit = r.ReadInt32(),
                AlarmRoomTile = r.ReadInt32(),
                ReportTicks = r.ReadInt32(),
                AttackSpellSlot = r.ReadByte(),
                SwingKind = r.ReadByte(),
                SwingSpellSlot = r.ReadByte(),
                LastPosX = r.ReadInt32(),
                LastPosY = r.ReadInt32(),
                ArmourTicks = r.ReadInt32(),
                InvulnerableTicks = r.ReadInt32(),
                FrozenTicks = r.ReadInt32(),
                InvisibleTicks = r.ReadInt32(),
                SpeedEffectTicks = r.ReadInt32(),
                SpeedPercent = r.ReadInt32(),
                PoisonTicks = r.ReadInt32(),
                PoisonAmount = r.ReadInt32(),
                PoisonOwner = r.ReadByte(),
                StunTicks = r.ReadInt32(),
                Approaching = r.ReadBoolean(),
                RouteRefreshCountdown = r.ReadInt32(),
                ChaseTicks = r.ReadInt32(),
                RetreatingCreature = r.ReadInt32(),
                CelebrationTicks = r.ReadInt32(),
                BodyTargetId = r.ReadInt32(),
                PartyId = r.ReadByte(),
                PositionInParty = r.ReadByte(),
                LeaderId = r.ReadInt32(),
                WanderRadius = r.ReadByte(),
                Flags = r.ReadByte(),
                DestroysRooms = r.ReadBoolean(),
                IsTool = r.ReadBoolean(),
                IsMercenary = r.ReadBoolean(),
                GoodJob = (JobType)r.ReadInt32(),
                GoodJobPlayerId = r.ReadByte(),
                GoodJobActionPointId = r.ReadInt32(),
            };
            for (int k = 0; k < creature.SpellStamps.Length; k++) creature.SpellStamps[k] = r.ReadInt64();
            for (int k = 0; k < creature.EngagedMelee.Length; k++) creature.EngagedMelee[k] = r.ReadInt32();
            for (int k = 0; k < creature.EngagedRanged.Length; k++) creature.EngagedRanged[k] = r.ReadInt32();

            ReadBody(r, creature.Body);
            Navigation.WorldSteering.SyncBody(creature);
            world.RawCreatures.Add(creature);
        }

        // Creatures are put back with their stored ids rather than re-spawned,
        // so the counter is restored rather than derived: a level that has had
        // creatures die must not hand out an id it has already used.
        world.NextCreatureId = nextCreatureId;

        int nextShotId = r.ReadInt32();
        int shotCount = r.ReadInt32();
        for (int i = 0; i < shotCount; i++)
        {
            world.RawShots.Add(new ShotState
            {
                Id = r.ReadInt32(),
                ShotTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                VelX = r.ReadInt32(),
                VelY = r.ReadInt32(),
                Health = r.ReadInt32(),
                Age = r.ReadInt32(),
                Data1 = r.ReadInt32(),
                Data2 = r.ReadInt32(),
                CasterId = r.ReadInt32(),
                TargetKind = r.ReadByte(),
                TargetId = r.ReadInt32(),
                TargetPlayerId = r.ReadByte(),
                TargetTileIndex = r.ReadInt32(),
                TargetX = r.ReadInt32(),
                TargetY = r.ReadInt32(),
            });
        }
        world.NextShotId = nextShotId;

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
                Setting = r.ReadByte(),
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
                LockMode = r.ReadByte(),
                LockPickTicks = r.ReadInt32(),
                Open = r.ReadBoolean(),
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
            byte spawnCount = r.ReadByte();
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
                    WanderRadius = r.ReadByte(), PartyId = r.ReadByte(),
                    Flags2 = r.ReadByte(),
                };
            }
            world.RawHeroParties.Add(new HeroPartyState
            {
                Name = name, TriggerId = triggerId, PartyId = partyId,
                Unknown_0023 = unknown0023, Unknown_0027 = unknown0027, SpawnCount = spawnCount, Members = members,
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
        world.Triggers.RawWorldOptions = r.ReadByte();
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
