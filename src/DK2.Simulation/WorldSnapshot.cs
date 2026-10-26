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
    private const int FormatVersion = 309;

    private static void WriteLink(BinaryWriter w, ThingTileLink link)
    {
        w.Write(link.TileIndex);
        w.Write(link.Order);
    }

    private static ThingTileLink ReadLink(BinaryReader r) => new(r.ReadInt32(), r.ReadInt32());

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
            w.Write(t.StoreGoldHeading ?? ushort.MaxValue);
            w.Write(t.BurnTicksRemaining);
            w.Write(t.EntranceIdleTicks);
            w.Write(t.EntranceCreaturesGenerated);
            w.Write(t.HatchTicks);
            w.Write(t.ChickensGenerated);
            w.Write(t.PitFightSeenTick);
            w.Write(t.CasinoParty);
            w.Write(t.CasinoPartyStartTick);
            w.Write(t.PrisonDoor);
            w.Write(t.PrisonDoorAnimationTick);
            w.Write(t.SlabOrder);
            w.Write(t.RoomListPosition.Value);
            w.Write(t.RoomSizeChangedTick);
            w.Write(t.RoomKickedTick);
            w.Write(t.RoomRuined);
            w.Write(t.RoomHealthLost);
            w.Write(t.KeptManaGain);
            w.Write(t.TaggedBy.Bits);
            w.Write(t.SecretDoor);
            w.Write(t.SecretDoorTasks.Bits);
            w.Write(t.DiscoveredBy.Bits);
            w.Write(t.ExploredBy.Bits);
            w.Write(t.FreshlyDug);
            w.Write(t.DigTaskOutlivesWallFor.Bits);
            w.Write(t.TagOrder);
        }

        w.Write(world.NextSlabOrder);
        w.Write(world.NextRoomListPosition);
        w.Write(world.NextPlaceClaim);
        w.Write(world.NextTagOrder);
        w.Write(world.SecretDoorEntries.Count);
        foreach (var pair in world.SecretDoorEntries.OrderBy(e => e.Key.Player.Value)
            .ThenBy(e => e.Key.Tile.Y).ThenBy(e => e.Key.Tile.X))
        {
            w.Write(pair.Key.Player.Value);
            w.Write(pair.Key.Tile.X);
            w.Write(pair.Key.Tile.Y);
            w.Write(pair.Value.Order);
            foreach (int holder in pair.Value.Holders) w.Write(holder);
        }
        w.Write(world.PlayerCount);
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState p = ref world.PlayerAtIndex(i);
            w.Write(p.PlayerId.Value);
            w.Write(p.Mana);
            w.Write(p.OneShotManaSpent);
            w.Write(p.OneShotReaperUsed);
            w.Write(p.TrapNoManaMessageAt);
            w.Write(p.GoldMined);
            w.Write(p.KilledByPlayerId);
            w.Write(p.HeroesDestroyed);
            w.Write(p.SlapCount);
            w.Write(p.PossessedCreatureId);
            w.Write(p.ResearchPoints);
            w.Write(p.ResearchTarget);
            w.Write(p.NextFreeImpTick.Value);
            w.Write(p.NextPaydayTick.Value);
            w.Write(p.LastPaydayTick.Value);
            w.Write(p.Status);
            w.Write(p.StatusTick.Value);
            w.Write(p.HeartHealth);
            w.Write(p.HeartLastAttackerPlayerId);
            w.Write(p.HeartDiedTick.Value);
            w.Write(p.HeartRuined);
            w.Write(p.ManaPressure.WarningDeadline);
            w.Write(p.ManaPressure.WarningStage);
            w.Write(p.ManaPressure.WarningsSuppressed);
            w.Write(p.ManaPressure.RemovalEnabled);
            w.Write(p.ManaPressure.ScanDeadline);
            w.Write(p.CallToArms.On);
            w.Write(p.CallToArms.ObjectId);
            w.Write(p.CallToArms.X);
            w.Write(p.CallToArms.Y);
            w.Write(p.CallToArms.ManaTick);
            w.Write(p.PortalsClosed);
            w.Write(p.CameraPathEndTick);
            w.Write(p.CasinoParties);
            w.Write(p.GraveyardWantedPosted);
            w.Write(p.Availabilities.Count);
            foreach (AvailabilityState availability in p.Availabilities)
            {
                w.Write((ushort)availability.Kind);
                w.Write(availability.TypeId);
                w.Write(availability.Value);
                w.Write(availability.Upgraded);
                w.Write(availability.LastCastTick);
                w.Write(availability.LastCheckedTick);
            }
            w.Write(p.EntranceInfo.Count);
            foreach (EntranceInfoState entrance in p.EntranceInfo)
            {
                w.Write(entrance.CreatureTypeId);
                w.Write(entrance.Boost);
                w.Write(entrance.Block);
            }
            w.Write(p.IntroducedCreatureTypes.Count);
            foreach (byte creatureType in p.IntroducedCreatureTypes) w.Write(creatureType);
            w.Write(p.ManufacturePoints);
            w.Write(p.ManufacturingCreatures);
            w.Write(p.InformationGiven);
            w.Write(p.ItemsManufactured);
            w.Write(p.ManufactureQueue.Count);
            foreach (ManufactureOrder order in p.ManufactureQueue)
            {
                w.Write(order.Kind);
                w.Write(order.TypeId);
                w.Write(order.Cost);
            }
            w.Write(p.Blueprints.Count);
            foreach (BlueprintRef blueprint in p.Blueprints)
            {
                w.Write(blueprint.Kind);
                w.Write(blueprint.Id);
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
        foreach (RoomBuildWaveState? wave in world.RoomBuildWaveSlots)
        {
            w.Write(wave.HasValue);
            if (wave is not RoomBuildWaveState active) continue;
            w.Write((byte)active.RoomId);
            w.Write(active.Press.X);
            w.Write(active.Press.Y);
            w.Write(active.Release.X);
            w.Write(active.Release.Y);
            w.Write(active.QueuedTick.Value);
            w.Write(active.AccumulatedCost);
        }

        w.Write(world.PendingWork.Count);
        foreach (TileWorkOrder o in world.PendingWork)
        {
            w.Write(o.TileIndex);
            w.Write((int)o.Kind);
            w.Write(o.PlayerId.Value);
            w.Write(o.WorkerCreatureId);
            w.Write(o.Multiplier);
            w.Write((byte)o.Strike);
        }

        w.Write(world.NextCreatureId);
        w.Write(world.Creatures.Count);
        foreach (CreatureState c in world.Creatures)
        {
            w.Write(c.Id);
            w.Write(c.CreatureTypeId);
            w.Write(c.OwnerPlayerId);
            w.Write(c.NameIndex);
            w.Write(c.BloodGroupIndex);
            w.Write(c.TriggerId);
            w.Write(c.TriggerRepeat);
            w.Write(c.PosX);
            w.Write(c.PosY);
            w.Write(c.PosZ);
            w.Write(c.CreationVelocityZ);
            w.Write(c.Heading);
            w.Write((int)c.RouteOptions);
            w.Write(c.HeartTargetPlayerId);
            w.Write(c.DeathShotFired);
            w.Write(c.GoldHeld);
            w.Write(c.CasinoBigWinnerTicks);
            w.Write(c.Health);
            w.Write(c.HungerTicks);
            w.Write(c.HungerNeed);
            w.Write(c.FoodOwedAfterMeal);
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
            w.Write(c.GetPaid);
            w.Write(c.SkipNextPayment);
            w.Write(c.DieInstantly);
            w.Write(c.Level);
            w.Write(c.ExperiencePoints);
            w.Write((int)c.Mood);
            w.Write(c.LastAngryChoiceTick);
            w.Write(c.LastHappyChoiceTick);
            w.Write(c.LastUnhappyChoiceTick);
            w.Write((int)c.CurrentJob);
            w.Write((int)c.DroppedToDoJob);
            w.Write(c.JobTileIndex);
            w.Write(c.InHand);
            w.Write(c.PickupTick);
            w.Write(c.DroppedTick);
            w.Write(c.DroppedOnARoom);
            w.Write(c.NextPayTryTick);
            w.Write(c.LastSlapTick);
            w.Write(c.WorkFasterTicks);
            w.Write(c.JustDropped);
            w.Write(c.WorkerIdle);
            w.Write(c.ManaRemovalStamp);
            w.Write(c.StatusFlowerUntilTurn);
            w.Write(c.CorpseSuppressed);
            w.Write((byte)c.DroppedTask);
            w.Write((byte)c.LastTaskKind);
            w.Write(c.LastTaskTileIndex);
            w.Write(c.CarriedCreatureId);
            w.Write(c.CarriedByCreatureId);
            w.Write(c.TargetBodyId);
            w.Write(c.PrisonOwnerPlayerId);
            w.Write(c.RescuePriorityMask);
            w.Write(c.CarriedObjectId);
            w.Write(c.TargetObjectId);
            w.Write(c.Dk2StateId);
            w.Write(c.StateBeforeUtilityState);
            w.Write(c.UtilityNewState);
            w.Write(c.HookCountdown);
            w.Write(c.HeldPlace.TileIndex);
            w.Write(c.HeldPlace.Units);
            w.Write(c.TorturedDuration);
            w.Write(c.TortureTargetTile);
            w.Write(c.WhipTurnsLeft);
            w.Write(c.ContactTileIndex);
            w.Write(c.RescueTurnsLeft);
            w.Write(c.RebelCountdownTicks);
            w.Write(c.RebelAgainstPlayerId);
            w.Write(c.TargetTileIndex);
            w.Write((int)c.TargetWork);
            w.Write(c.SecretDoorTask);
            w.Write(c.WorkSlot);
            w.Write(c.TimerTicks);
            w.Write(c.FightTargetId);
            w.Write((byte)c.AttackType);
            w.Write(c.AttackRangeSquared);
            w.Write(c.FightProcessCounter);
            w.Write(c.LastFightScanTileIndex);
            w.Write((byte)c.FightTargetKind);
            w.Write(c.FightTargetPlayerId);
            w.Write(c.FightTargetTileIndex);
            w.Write(c.TimeLastUsedMelee);
            w.Write(c.SwingInProgress);
            w.Write(c.SwingStartTick);
            w.Write((byte)c.SwingTargetKind);
            w.Write(c.SwingTargetId);
            w.Write(c.SwingTargetPlayerId);
            w.Write(c.TempFear);
            w.Write(c.CurrentFear);
            w.Write(c.OverThreatCounter);
            w.Write(c.BackOffTimer);
            w.Write(c.Running);
            w.Write(c.CombatStateEnteredTick);
            w.Write(c.StateEnteredTick);
            w.Write(c.ActionArtSlot);
            w.Write(c.ActionAnimationPhase);
            w.Write(c.ActionAnimationRate);
            w.Write((byte)c.ActionAnimationFlags);
            w.Write(c.ActionHandlerDone);
            w.Write(c.ActionCount);
            w.Write(c.AlarmX);
            w.Write(c.AlarmY);
            w.Write(c.AlarmDeficit);
            w.Write(c.AlarmRoomTile);
            w.Write(c.ReportTicks);
            w.Write(c.GuardFacing);
            w.Write(c.GuardDeadlineTick);
            w.Write(c.StateDeadlineTick);
            w.Write(c.WaypointDeadlineTick);
            w.Write(c.PartySpeedRaw);
            w.Write(c.OriginTileIndex);
            w.Write(c.Tunnel.StandX);
            w.Write(c.Tunnel.StandY);
            w.Write(c.Tunnel.DigX);
            w.Write(c.Tunnel.DigY);
            w.Write(c.Tunnel.TargetX);
            w.Write(c.Tunnel.TargetY);
            w.Write(c.Tunnel.Phase);
            w.Write(c.Tunnel.ExitX);
            w.Write(c.Tunnel.ExitY);
            w.Write(c.Tunnel.HitX);
            w.Write(c.Tunnel.HitY);
            w.Write(c.Tunnel.Facing);
            w.Write(c.Tunnel.Resume);
            w.Write(c.Tunnel.Reach);
            w.Write(c.Tunnel.Hand);
            w.Write(c.Tunnel.ActionUntilTick);
            w.Write(c.WanderStamp);
            w.Write(c.ActionUntilTick);
            w.Write(c.TrainingActionId);
            w.Write(c.TrainingActionStartedTick);
            w.Write(c.TrainingTargetObjectId);
            w.Write(c.TrainingSpellSlot);
            w.Write(c.TrainingActionHitDone);
            w.Write(c.Fleeing);
            w.Write(c.ShouldDelayGetUp);
            w.Write(c.TremorRoutePaused);
            w.Write(c.OldFightTileX);
            w.Write(c.OldFightTileY);
            w.Write(c.OldFightTick);
            w.Write(c.GuardLostTick);
            w.Write(c.HeedingCallToArms);
            w.Write(c.CombatPitFighter);
            w.Write(c.CasinoGamblingDuration);
            w.Write(c.CasinoGamblingFearless);
            w.Write(c.FearlessUntilTick);
            w.Write(c.TortureVoluntary);
            w.Write(c.PortalEntranceTile);
            w.Write(c.AttackAbnormalTargets);
            w.Write(c.AttackingId);
            w.Write((byte)c.AttackingKind);
            w.Write(c.RebelActive);
            w.Write(c.ChainTileIndex);
            w.Write(c.ChainOrder);
            WriteLink(w, c.TileLink);
            w.Write(c.PitStateEnteredTick);
            w.Write(c.SpectatorPosX);
            w.Write(c.SpectatorPosY);
            w.Write(c.CasinoObjectId);
            w.Write(c.CasinoFacing);
            w.Write(c.WaitPosX);
            w.Write(c.WaitPosY);
            w.Write(c.StateTargetX);
            w.Write(c.StateTargetY);
            w.Write(c.MachineMode);
            w.Write(c.WatchedCreatureId);
            w.Write(c.HeldPlaceStamp);
            w.Write(c.HeldPlaceSerial);
            w.Write(c.DrunkTicks);
            w.Write(c.JackpotDeadlineTicks);
            w.Write(c.JackpotWinnings);
            w.Write(c.GraveyardSlot);
            w.Write((byte)c.BodyOwnerPlayerId);
            w.Write(c.AttackSpellSlot);
            w.Write(c.SwingKind);
            w.Write(c.SwingSpellSlot);
            w.Write(c.SwingPointX);
            w.Write(c.SwingPointY);
            w.Write(c.SwingPointZ);
            w.Write(c.SwingPitch);
            w.Write(c.SwingRange);
            w.Write(c.LastPosX);
            w.Write(c.LastPosY);
            w.Write(c.LastPosZ);
            w.Write(c.BloodFootprintTicks);
            w.Write(c.WaterFootprintTicks);
            w.Write(c.ArmourTicks);
            w.Write(c.InvulnerableTicks);
            w.Write(c.FrozenTicks);
            w.Write(c.InvisibleTicks);
            w.Write(c.SpeedEffectTicks);
            w.Write(c.SpeedPercent);
            w.Write(c.PoisonTicks);
            w.Write(c.PoisonAmount);
            w.Write(c.PoisonOwner);
            w.Write(c.HealthSourcePlayerId);
            w.Write(c.DeathUncounted);
            w.Write(c.StunTicks);
            w.Write(c.ChickenTicks);
            w.Write(c.TurncoatTicks);
            w.Write(c.TurncoatPlayerId);
            w.Write(c.Possessed);
            w.Write(c.InfluencedByPossessed);
            w.Write(c.NextPossessionInfluenceTick.Value);
            w.Write(c.FollowLeaderId);
            w.Write(c.FollowRouteTick);
            w.Write(c.FollowerCount);
            w.Write((byte)c.PossessedTargetKind);
            w.Write(c.PossessedTargetId);
            w.Write(c.PossessedTargetTick);
            w.Write(c.SneakingAs);
            w.Write(c.SniperMode);
            w.Write(c.HypnotisedCreatureId);
            w.Write(c.HypnosisOwnerPlayerId);
            w.Write(c.Hypnotised);
            w.Write(c.AttackCreatureHits);
            w.Write(c.HypnotisedAttacks);
            w.Write(c.ClaimActionTile);
            w.Write(c.ReaperTicks);
            w.Write(c.HornyTick);
            w.Write(c.IsOneShotReaper);
            w.Write(c.ReaperComplete);
            w.Write(c.ReaperAlpha);
            w.Write(c.ReaperActionId);
            w.Write(c.ReaperStateAgeTicks);
            w.Write(c.ReaperCanAcquireFight);
            w.Write(c.ReaperIsAttackable);
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
            w.Write(c.GemCarrier);
            for (int i = 0; i < c.SpellStamps.Length; i++) w.Write(c.SpellStamps[i]);
            for (int i = 0; i < c.EngagedMelee.Length; i++) w.Write(c.EngagedMelee[i]);
            for (int i = 0; i < c.EngagedRanged.Length; i++) w.Write(c.EngagedRanged[i]);

            // The route window is state, not a cache: a creature restored without it would
            // request again from a different position and the restored run would diverge.
            WriteBody(w, c.Body);
        }

        foreach (CreatureState c in world.CreatureOwnershipOrder) w.Write(c.Id);

        w.Write(world.NextShotId);
        w.Write(world.Shots.Count);
        foreach (ShotState s in world.Shots)
        {
            w.Write(s.Id);
            w.Write(s.ShotTypeId);
            w.Write(s.OwnerPlayerId);
            w.Write(s.PosX);
            w.Write(s.PosY);
            WriteLink(w, s.TileLink);
            w.Write(s.VelX);
            w.Write(s.VelY);
            w.Write(s.Health);
            w.Write(s.Age);
            w.Write(s.Data1);
            w.Write(s.Data2);
            w.Write(s.CasterId);
            w.Write((byte)s.TargetKind);
            w.Write(s.TargetId);
            w.Write(s.TargetPlayerId);
            w.Write(s.TargetTileIndex);
            w.Write(s.TargetX);
            w.Write(s.TargetY);
            w.Write(s.ForcedCollision);
            w.Write(s.Sniper);
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
            w.Write(o.Cell);
            w.Write(o.CarriedByCreatureId);
            w.Write(o.TimerTicks);
            w.Write(o.InHand);
            w.Write(o.AutoPlaced);
            w.Write(o.Setting);
            w.Write(o.ResearchKind);
            w.Write(o.ResearchTypeId);
            w.Write(o.ProcessState);
            w.Write(o.ProcessDeadline);
            w.Write(o.ProcessCount);
            w.Write(o.ProcessEnding);
            w.Write(o.Health);
            w.Write(o.PosX);
            w.Write(o.PosY);
            w.Write(o.Heading);
            w.Write(o.ProcessAgeTicks);
            w.Write(o.AnimationStartedTick);
            w.Write(o.PosZ);
            w.Write(o.PhysicalVelocityX);
            w.Write(o.PhysicalVelocityY);
            w.Write(o.PhysicalVelocityZ);
            w.Write(o.DrawPitch);
            w.Write(o.DrawYaw);
            w.Write(o.DrawRoll);
            w.Write(o.ChickenCanEscape);
            w.Write(o.HatcheryRoomAnchorTileIndex);
            w.Write(o.ChickenScared);
            w.Write(o.ChickenReactionTicks);
            w.Write(o.MoveResumeState);
            w.Write(o.MoveDestinationX);
            w.Write(o.MoveDestinationY);
            w.Write(o.MoveVelocityX);
            w.Write(o.MoveVelocityY);
            w.Write(o.MoveTicksRemaining);
            w.Write((byte)o.MoveFlags);
            WriteLink(w, o.TileLink);
            w.Write(o.RollX);
            w.Write(o.RollY);
            w.Write(o.RollSpeed);
            w.Write(o.Wear);
            w.Write(o.RollSignX);
            w.Write(o.RollSignY);
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

        world.TileHighlights.Write(w);
        w.Write(world.NextDoorId);
        w.Write(world.Doors.Count);
        foreach (DoorState door in world.Doors)
        {
            w.Write(door.Id);
            w.Write(door.X);
            w.Write(door.Y);
            w.Write(door.Unknown_0008);
            w.Write(door.TriggerId);
            w.Write(door.DoorId);
            WriteLink(w, door.TileLink);
            w.Write(door.PlayerId);
            w.Write(door.Flags);
            w.Write(door.UnderConstruction);
            w.Write(door.LockMode);
            w.Write(door.LockPickTicks);
            w.Write(door.Open);
            w.Write(door.Orientation);
            w.Write(door.Health);
            w.Write(door.RecoveryDeadline);
            w.Write(door.Unknown_0011.ToArray());
        }
        w.Write(world.NavStamps.Count);
        foreach (NavStamp stamp in world.RawNavStamps)
        {
            w.Write(stamp.Tile);
            w.Write(stamp.TerrainId);
            w.Write(stamp.Byte);
        }

        w.Write(world.NextTrapId);
        w.Write(world.Traps.Count);
        foreach (TrapState trap in world.Traps)
        {
            w.Write(trap.Id);
            w.Write(trap.X);
            w.Write(trap.Y);
            w.Write(trap.Unknown_0008);
            w.Write(trap.NumberOfShots);
            w.Write(trap.TrapId);
            WriteLink(w, trap.TileLink);
            w.Write(trap.PlayerId);
            w.Write(trap.Unknown_000F);
            w.Write(trap.UnderConstruction);
            w.Write(trap.Health);
            w.Write(trap.Heading);
            w.Write(trap.RechargeAt);
            w.Write(trap.RecoveryAt);
            w.Write(trap.Charge);
            w.Write(trap.TriggerCount);
            w.Write(trap.Hidden);
            w.Write(trap.ThreatDuration);
            w.Write(trap.Threat);
            w.Write(trap.TriggerCountdown);
            w.Write(trap.TriggerCreatureId);
            w.Write(trap.SightTurn);
            w.Write(trap.SightTargetId);
            w.Write(trap.GuardRoomTile);
        }
        w.Write(world.NextChainOrder);
        w.Write(world.NextTileLinkOrder);
        foreach (TremorState tremor in world.Tremors)
        {
            w.Write(tremor.WorldX);
            w.Write(tremor.WorldY);
            w.Write(tremor.StartedAt);
            w.Write(tremor.DurationTicks);
            w.Write(tremor.Radius);
        }
        foreach (HeroSlot slot in world.HeroSlots)
        {
            w.Write(slot.CreatureId);
            w.Write(slot.Value);
        }
        w.Write(world.RoomBorderCursors.Count);
        foreach (RoomBorderCursor cursor in world.RoomBorderCursors)
        {
            w.Write(cursor.X);
            w.Write(cursor.Y);
            w.Write(cursor.Orientation);
            w.Write(cursor.Phase);
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
        world.Messages.Write(w);
        for (int i = 0; i < TriggerState.FlagCount; i++) w.Write(world.Triggers.RawFlags[i]);
        for (int i = 0; i < TriggerState.TimerCount; i++) w.Write(world.Triggers.RawTimers[i]);
        w.Write(world.Triggers.SpecialValue);
        w.Write(world.Triggers.SpecialValueWritten);
        w.Write(world.Triggers.RawWorldOptions);
        w.Write(world.Triggers.LevelTimeLimit);
        w.Write(world.Triggers.SpecialInfoNextRoomTypeId);
        w.Write(world.Triggers.SpecialInfoPlayerId);
        w.Write(world.Triggers.MaximumSlaps);
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

        simulation.Navigation.Write(w);
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
                StoreGoldHeading = r.ReadUInt16() is ushort heading && heading != ushort.MaxValue ? heading : null,
                BurnTicksRemaining = r.ReadInt32(),
                EntranceIdleTicks = r.ReadInt32(),
                EntranceCreaturesGenerated = r.ReadInt32(),
                HatchTicks = r.ReadInt32(),
                ChickensGenerated = r.ReadInt32(),
                PitFightSeenTick = r.ReadInt64(),
                CasinoParty = r.ReadByte(),
                CasinoPartyStartTick = r.ReadInt64(),
                PrisonDoor = r.ReadByte(),
                PrisonDoorAnimationTick = r.ReadInt64(),
                SlabOrder = r.ReadInt32(),
                RoomListPosition = new RoomListPosition(r.ReadInt64()),
                RoomSizeChangedTick = r.ReadInt64(),
                RoomKickedTick = r.ReadInt64(),
                RoomRuined = r.ReadBoolean(),
                RoomHealthLost = r.ReadInt32(),
                KeptManaGain = r.ReadByte(),
                TaggedBy = new PlayerMask(r.ReadUInt16()),
                SecretDoor = r.ReadBoolean(),
                SecretDoorTasks = new PlayerMask(r.ReadUInt16()),
                DiscoveredBy = new PlayerMask(r.ReadUInt16()),
                ExploredBy = new PlayerMask(r.ReadUInt16()),
                FreshlyDug = r.ReadBoolean(),
                DigTaskOutlivesWallFor = new PlayerMask(r.ReadUInt16()),
                TagOrder = r.ReadInt32(),
            };
        }

        int nextSlabOrder = r.ReadInt32();
        long nextRoomListPosition = r.ReadInt64();
        long nextPlaceClaim = r.ReadInt64();
        int nextTagOrder = r.ReadInt32();
        int secretDoorEntryCount = r.ReadInt32();
        var secretDoorEntries = new Dictionary<(PlayerId Player, TilePos Tile), SecretDoorTaskEntry>();
        for (int i = 0; i < secretDoorEntryCount; i++)
        {
            PlayerId player = r.ReadByte();
            var tile = new TilePos(r.ReadInt32(), r.ReadInt32());
            var entry = new SecretDoorTaskEntry { Order = r.ReadInt32() };
            for (int side = 0; side < 4; side++) entry.Holders[side] = r.ReadInt32();
            secretDoorEntries.Add((player, tile), entry);
        }
        int playerCount = r.ReadInt32();
        var players = new PlayerState[playerCount];
        for (int i = 0; i < playerCount; i++)
        {
            players[i] = new PlayerState
            {
                PlayerId = r.ReadByte(),
                Mana = r.ReadInt32(),
                OneShotManaSpent = r.ReadInt32(),
                OneShotReaperUsed = r.ReadBoolean(),
                TrapNoManaMessageAt = r.ReadInt64(),
                GoldMined = r.ReadInt32(),
                KilledByPlayerId = r.ReadByte(),
                HeroesDestroyed = r.ReadInt32(),
                SlapCount = r.ReadInt32(),
                PossessedCreatureId = r.ReadInt32(),
                ResearchPoints = r.ReadInt32(),
                ResearchTarget = r.ReadInt32(),
                NextFreeImpTick = new GameTick(r.ReadInt64()),
                NextPaydayTick = new GameTick(r.ReadInt64()),
                LastPaydayTick = new GameTick(r.ReadInt64()),
                Status = r.ReadByte(),
                StatusTick = new GameTick(r.ReadInt64()),
                HeartHealth = r.ReadInt32(),
                HeartLastAttackerPlayerId = r.ReadByte(),
                HeartDiedTick = new GameTick(r.ReadInt64()),
                HeartRuined = r.ReadBoolean(),
                ManaPressure = new ManaPressureState(
                    r.ReadUInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadBoolean(), r.ReadUInt32()),
                CallToArms = new CallToArmsState(
                    r.ReadBoolean(), r.ReadInt32(), r.ReadUInt16(), r.ReadUInt16(), r.ReadUInt32()),
                PortalsClosed = r.ReadBoolean(),
                CameraPathEndTick = r.ReadInt64(),
                CasinoParties = r.ReadInt32(),
                GraveyardWantedPosted = r.ReadBoolean(),
                Availabilities = [],
                EntranceInfo = [],
                IntroducedCreatureTypes = [],
                ManufactureQueue = [],
                Blueprints = [],
                Hand = [],
            };

            int availabilityCount = r.ReadInt32();
            for (int a = 0; a < availabilityCount; a++)
                players[i].Availabilities.Add(new AvailabilityState(
                    (AvailabilityKind)r.ReadUInt16(), r.ReadInt32(), r.ReadInt32(), r.ReadBoolean(), r.ReadInt64(), r.ReadInt64()));
            int entranceCount = r.ReadInt32();
            for (int a = 0; a < entranceCount; a++)
                players[i].EntranceInfo.Add(new EntranceInfoState(r.ReadByte(), r.ReadInt32(), r.ReadInt32()));
            int introducedCount = r.ReadInt32();
            if ((uint)introducedCount > 256) throw new InvalidDataException("Invalid introduced creature count.");
            for (int a = 0; a < introducedCount; a++) players[i].IntroducedCreatureTypes.Add(r.ReadByte());

            players[i].ManufacturePoints = r.ReadInt32();
            players[i].ManufacturingCreatures = r.ReadInt32();
            players[i].InformationGiven = r.ReadUInt64();
            players[i].ItemsManufactured = r.ReadInt32();
            int orderCount = r.ReadInt32();
            for (int o = 0; o < orderCount; o++)
                players[i].ManufactureQueue.Add(new ManufactureOrder(r.ReadByte(), r.ReadByte(), r.ReadInt32()));
            int blueprintCount = r.ReadInt32();
            for (int o = 0; o < blueprintCount; o++)
                players[i].Blueprints.Add(new BlueprintRef(r.ReadByte(), r.ReadInt32()));

            int handCount = r.ReadInt32();
            for (int h = 0; h < handCount; h++)
            {
                var kind = (HandItemKind)r.ReadByte();
                players[i].Hand.Add(new HandItem(kind, r.ReadInt32()));
            }
        }

        WorldState world = WorldState.FromParts(width, height, tiles, players, fogOfWarEnabled);
        world.NextSlabOrder = nextSlabOrder;
        world.NextRoomListPosition = nextRoomListPosition;
        world.NextPlaceClaim = nextPlaceClaim;
        world.NextTagOrder = nextTagOrder;
        foreach (var pair in secretDoorEntries) world.SecretDoorEntries.Add(pair.Key, pair.Value);
        world.CurrentTick = tick;

        for (int i = 0; i < world.RoomBuildWaveSlots.Length; i++)
            if (r.ReadBoolean())
                world.RoomBuildWaveSlots[i] = new RoomBuildWaveState((RoomTypeId)r.ReadByte(),
                    new TilePos(r.ReadInt32(), r.ReadInt32()), new TilePos(r.ReadInt32(), r.ReadInt32()),
                    new GameTick(r.ReadInt64()), r.ReadUInt16());

        int workCount = r.ReadInt32();
        for (int i = 0; i < workCount; i++)
        {
            world.QueueWork(new TileWorkOrder(
                r.ReadInt32(), (TileWorkKind)r.ReadInt32(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(), (TileWorkStrike)r.ReadByte()));
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
                NameIndex = r.ReadUInt16(),
                BloodGroupIndex = r.ReadByte(),
                TriggerId = r.ReadUInt16(),
                TriggerRepeat = r.ReadByte(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                PosZ = r.ReadInt32(),
                CreationVelocityZ = r.ReadInt32(),
                Heading = r.ReadInt32(),
                RouteOptions = (Navigation.MoveOptions)r.ReadInt32(),
                HeartTargetPlayerId = r.ReadByte(),
                DeathShotFired = r.ReadBoolean(),
                GoldHeld = r.ReadInt32(),
                CasinoBigWinnerTicks = r.ReadUInt16(),
                Health = r.ReadInt32(),
                HungerTicks = r.ReadInt32(),
                HungerNeed = r.ReadInt32(),
                FoodOwedAfterMeal = r.ReadBoolean(),
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
                GetPaid = r.ReadBoolean(),
                SkipNextPayment = r.ReadBoolean(),
                DieInstantly = r.ReadBoolean(),
                Level = r.ReadInt32(),
                ExperiencePoints = r.ReadInt32(),
                Mood = (CreatureMood)r.ReadInt32(),
                LastAngryChoiceTick = r.ReadInt64(),
                LastHappyChoiceTick = r.ReadInt64(),
                LastUnhappyChoiceTick = r.ReadInt64(),
                CurrentJob = (JobType)r.ReadInt32(),
                DroppedToDoJob = (JobType)r.ReadInt32(),
                JobTileIndex = r.ReadInt32(),
                InHand = r.ReadBoolean(),
                PickupTick = r.ReadInt64(),
                DroppedTick = r.ReadInt64(),
                DroppedOnARoom = r.ReadBoolean(),
                NextPayTryTick = r.ReadInt64(),
                LastSlapTick = r.ReadInt64(),
                WorkFasterTicks = r.ReadInt32(),
                JustDropped = r.ReadBoolean(),
                WorkerIdle = r.ReadBoolean(),
                ManaRemovalStamp = r.ReadUInt32(),
                StatusFlowerUntilTurn = r.ReadUInt32(),
                CorpseSuppressed = r.ReadBoolean(),
                DroppedTask = (ImpTaskKind)r.ReadByte(),
                LastTaskKind = (ImpTaskKind)r.ReadByte(),
                LastTaskTileIndex = r.ReadInt32(),
                CarriedCreatureId = r.ReadInt32(),
                CarriedByCreatureId = r.ReadInt32(),
                TargetBodyId = r.ReadInt32(),
                PrisonOwnerPlayerId = r.ReadInt32(),
                RescuePriorityMask = r.ReadUInt16(),
                CarriedObjectId = r.ReadInt32(),
                TargetObjectId = r.ReadInt32(),
                Dk2StateId = r.ReadInt32(),
                StateBeforeUtilityState = r.ReadInt32(),
                UtilityNewState = r.ReadInt32(),
                HookCountdown = r.ReadInt32(),
                HeldPlace = new RoomPlace(r.ReadInt32(), r.ReadInt32()),
                TorturedDuration = r.ReadInt32(),
                TortureTargetTile = r.ReadInt32(),
                WhipTurnsLeft = r.ReadInt32(),
                ContactTileIndex = r.ReadInt32(),
                RescueTurnsLeft = r.ReadInt32(),
                RebelCountdownTicks = r.ReadInt32(),
                RebelAgainstPlayerId = r.ReadByte(),
                TargetTileIndex = r.ReadInt32(),
                TargetWork = (TileWorkKind)r.ReadInt32(),
                SecretDoorTask = r.ReadBoolean(),
                WorkSlot = r.ReadByte(),
                TimerTicks = r.ReadInt32(),
                FightTargetId = r.ReadInt32(),
                AttackType = (Combat.CombatRules.AttackMode)r.ReadByte(),
                AttackRangeSquared = r.ReadInt32(),
                FightProcessCounter = r.ReadByte(),
                LastFightScanTileIndex = r.ReadInt32(),
                FightTargetKind = (Combat.FightTargetKind)r.ReadByte(),
                FightTargetPlayerId = r.ReadByte(),
                FightTargetTileIndex = r.ReadInt32(),
                TimeLastUsedMelee = r.ReadInt64(),
                SwingInProgress = r.ReadBoolean(),
                SwingStartTick = r.ReadInt64(),
                SwingTargetKind = (Combat.FightTargetKind)r.ReadByte(),
                SwingTargetId = r.ReadInt32(),
                SwingTargetPlayerId = r.ReadByte(),
                TempFear = r.ReadUInt16(),
                CurrentFear = r.ReadUInt16(),
                OverThreatCounter = r.ReadByte(),
                BackOffTimer = r.ReadByte(),
                Running = r.ReadBoolean(),
                CombatStateEnteredTick = r.ReadInt64(),
                StateEnteredTick = r.ReadInt64(),
                ActionArtSlot = r.ReadByte(),
                ActionAnimationPhase = r.ReadInt32(),
                ActionAnimationRate = r.ReadInt32(),
                ActionAnimationFlags = (ActionAnimationFlags)r.ReadByte(),
                ActionHandlerDone = r.ReadBoolean(),
                ActionCount = r.ReadByte(),
                AlarmX = r.ReadInt32(),
                AlarmY = r.ReadInt32(),
                AlarmDeficit = r.ReadInt32(),
                AlarmRoomTile = r.ReadInt32(),
                ReportTicks = r.ReadInt32(),
                GuardFacing = r.ReadInt32(),
                GuardDeadlineTick = r.ReadInt64(),
                StateDeadlineTick = r.ReadInt64(),
                WaypointDeadlineTick = r.ReadInt64(),
                PartySpeedRaw = r.ReadInt32(),
                OriginTileIndex = r.ReadInt32(),
                Tunnel = new TunnelRecord
                {
                    StandX = r.ReadInt32(),
                    StandY = r.ReadInt32(),
                    DigX = r.ReadInt32(),
                    DigY = r.ReadInt32(),
                    TargetX = r.ReadInt32(),
                    TargetY = r.ReadInt32(),
                    Phase = r.ReadInt32(),
                    ExitX = r.ReadInt32(),
                    ExitY = r.ReadInt32(),
                    HitX = r.ReadInt32(),
                    HitY = r.ReadInt32(),
                    Facing = r.ReadInt32(),
                    Resume = r.ReadInt32(),
                    Reach = r.ReadBoolean(),
                    Hand = r.ReadBoolean(),
                    ActionUntilTick = r.ReadInt64(),
                },
                WanderStamp = r.ReadInt64(),
                ActionUntilTick = r.ReadInt64(),
                TrainingActionId = r.ReadByte(),
                TrainingActionStartedTick = r.ReadInt64(),
                TrainingTargetObjectId = r.ReadInt32(),
                TrainingSpellSlot = r.ReadByte(),
                TrainingActionHitDone = r.ReadBoolean(),
                Fleeing = r.ReadBoolean(),
                ShouldDelayGetUp = r.ReadBoolean(),
                TremorRoutePaused = r.ReadBoolean(),
                OldFightTileX = r.ReadInt32(),
                OldFightTileY = r.ReadInt32(),
                OldFightTick = r.ReadInt64(),
                GuardLostTick = r.ReadInt64(),
                HeedingCallToArms = r.ReadBoolean(),
                CombatPitFighter = r.ReadBoolean(),
                CasinoGamblingDuration = r.ReadInt32(),
                CasinoGamblingFearless = r.ReadBoolean(),
                FearlessUntilTick = r.ReadInt64(),
                TortureVoluntary = r.ReadBoolean(),
                PortalEntranceTile = r.ReadInt32(),
                AttackAbnormalTargets = r.ReadBoolean(),
                AttackingId = r.ReadInt32(),
                AttackingKind = (Combat.FightTargetKind)r.ReadByte(),
                RebelActive = r.ReadBoolean(),
                ChainTileIndex = r.ReadInt32(),
                ChainOrder = r.ReadInt32(),
                TileLink = ReadLink(r),
                PitStateEnteredTick = r.ReadInt64(),
                SpectatorPosX = r.ReadInt32(),
                SpectatorPosY = r.ReadInt32(),
                CasinoObjectId = r.ReadInt32(),
                CasinoFacing = r.ReadBoolean(),
                WaitPosX = r.ReadInt32(),
                WaitPosY = r.ReadInt32(),
                StateTargetX = r.ReadInt32(),
                StateTargetY = r.ReadInt32(),
                MachineMode = r.ReadByte(),
                WatchedCreatureId = r.ReadInt32(),
                HeldPlaceStamp = r.ReadInt64(),
                HeldPlaceSerial = r.ReadInt64(),
                DrunkTicks = r.ReadInt32(),
                JackpotDeadlineTicks = r.ReadInt32(),
                JackpotWinnings = r.ReadInt32(),
                GraveyardSlot = r.ReadInt32(),
                BodyOwnerPlayerId = r.ReadByte(),
                AttackSpellSlot = r.ReadByte(),
                SwingKind = r.ReadByte(),
                SwingSpellSlot = r.ReadByte(),
                SwingPointX = r.ReadInt32(),
                SwingPointY = r.ReadInt32(),
                SwingPointZ = r.ReadInt32(),
                SwingPitch = r.ReadInt32(),
                SwingRange = r.ReadInt32(),
                LastPosX = r.ReadInt32(),
                LastPosY = r.ReadInt32(),
                LastPosZ = r.ReadInt32(),
                BloodFootprintTicks = r.ReadByte(),
                WaterFootprintTicks = r.ReadByte(),
                ArmourTicks = r.ReadInt32(),
                InvulnerableTicks = r.ReadInt32(),
                FrozenTicks = r.ReadInt32(),
                InvisibleTicks = r.ReadInt32(),
                SpeedEffectTicks = r.ReadInt32(),
                SpeedPercent = r.ReadInt32(),
                PoisonTicks = r.ReadInt32(),
                PoisonAmount = r.ReadInt32(),
                PoisonOwner = r.ReadByte(),
                HealthSourcePlayerId = r.ReadByte(),
                DeathUncounted = r.ReadBoolean(),
                StunTicks = r.ReadInt32(),
                ChickenTicks = r.ReadInt32(),
                TurncoatTicks = r.ReadUInt16(),
                TurncoatPlayerId = r.ReadUInt16(),
                Possessed = r.ReadBoolean(),
                InfluencedByPossessed = r.ReadBoolean(),
                NextPossessionInfluenceTick = new GameTick(r.ReadInt64()),
                FollowLeaderId = r.ReadInt32(),
                FollowRouteTick = r.ReadInt64(),
                FollowerCount = r.ReadInt32(),
                PossessedTargetKind = (Combat.FightTargetKind)r.ReadByte(),
                PossessedTargetId = r.ReadInt32(),
                PossessedTargetTick = r.ReadInt64(),
                SneakingAs = r.ReadUInt16(),
                SniperMode = r.ReadBoolean(),
                HypnotisedCreatureId = r.ReadInt32(),
                HypnosisOwnerPlayerId = r.ReadByte(),
                Hypnotised = r.ReadBoolean(),
                AttackCreatureHits = r.ReadUInt16(),
                HypnotisedAttacks = r.ReadUInt16(),
                ClaimActionTile = r.ReadInt32(),
                ReaperTicks = r.ReadUInt16(),
                HornyTick = r.ReadUInt32(),
                IsOneShotReaper = r.ReadBoolean(),
                ReaperComplete = r.ReadBoolean(),
                ReaperAlpha = r.ReadByte(),
                ReaperActionId = r.ReadByte(),
                ReaperStateAgeTicks = r.ReadUInt32(),
                ReaperCanAcquireFight = r.ReadBoolean(),
                ReaperIsAttackable = r.ReadBoolean(),
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
                GemCarrier = r.ReadBoolean(),
            };
            for (int k = 0; k < creature.SpellStamps.Length; k++) creature.SpellStamps[k] = r.ReadInt64();
            for (int k = 0; k < creature.EngagedMelee.Length; k++) creature.EngagedMelee[k] = r.ReadInt32();
            for (int k = 0; k < creature.EngagedRanged.Length; k++) creature.EngagedRanged[k] = r.ReadInt32();

            ReadBody(r, creature.Body);
            Navigation.WorldSteering.SyncBody(creature);
            world.RawCreatures.Add(creature);
            world.AttachLoaded(creature);
        }

        // Creatures are put back with their stored ids rather than re-spawned,
        // so the counter is restored rather than derived: a level that has had
        // creatures die must not hand out an id it has already used.
        world.NextCreatureId = nextCreatureId;

        int[] ownershipOrder = new int[creatureCount];
        for (int i = 0; i < ownershipOrder.Length; i++) ownershipOrder[i] = r.ReadInt32();
        world.RestoreCreatureOwnershipOrder(ownershipOrder);

        int nextShotId = r.ReadInt32();
        int shotCount = r.ReadInt32();
        for (int i = 0; i < shotCount; i++)
        {
            var shot = new ShotState
            {
                Id = r.ReadInt32(),
                ShotTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                TileLink = ReadLink(r),
                VelX = r.ReadInt32(),
                VelY = r.ReadInt32(),
                Health = r.ReadInt32(),
                Age = r.ReadInt32(),
                Data1 = r.ReadInt32(),
                Data2 = r.ReadInt32(),
                CasterId = r.ReadInt32(),
                TargetKind = (Combat.FightTargetKind)r.ReadByte(),
                TargetId = r.ReadInt32(),
                TargetPlayerId = r.ReadByte(),
                TargetTileIndex = r.ReadInt32(),
                TargetX = r.ReadInt32(),
                TargetY = r.ReadInt32(),
                ForcedCollision = r.ReadBoolean(),
                Sniper = r.ReadBoolean(),
            };
            world.RawShots.Add(shot);
            world.AttachLoaded(shot);
        }
        world.NextShotId = nextShotId;

        int nextObjectId = r.ReadInt32();
        int objectCount = r.ReadInt32();
        for (int i = 0; i < objectCount; i++)
        {
            var loaded = new ObjectState
            {
                Id = r.ReadInt32(),
                ObjectTypeId = r.ReadByte(),
                OwnerPlayerId = r.ReadByte(),
                TileIndex = r.ReadInt32(),
                CreatureId = r.ReadInt32(),
                GoldHeld = r.ReadInt32(),
                Cell = r.ReadByte(),
                CarriedByCreatureId = r.ReadInt32(),
                TimerTicks = r.ReadInt32(),
                InHand = r.ReadBoolean(),
                AutoPlaced = r.ReadBoolean(),
                Setting = r.ReadByte(),
                ResearchKind = r.ReadByte(),
                ResearchTypeId = r.ReadByte(),
                ProcessState = r.ReadByte(),
                ProcessDeadline = r.ReadInt64(),
                ProcessCount = r.ReadByte(),
                ProcessEnding = r.ReadBoolean(),
                Health = r.ReadInt32(),
                PosX = r.ReadInt32(),
                PosY = r.ReadInt32(),
                Heading = r.ReadInt32(),
                ProcessAgeTicks = r.ReadInt32(),
                AnimationStartedTick = r.ReadInt64(),
                PosZ = r.ReadInt32(),
                PhysicalVelocityX = r.ReadInt32(),
                PhysicalVelocityY = r.ReadInt32(),
                PhysicalVelocityZ = r.ReadInt32(),
                DrawPitch = r.ReadInt32(),
                DrawYaw = r.ReadInt32(),
                DrawRoll = r.ReadInt32(),
                ChickenCanEscape = r.ReadBoolean(),
                HatcheryRoomAnchorTileIndex = r.ReadInt32(),
                ChickenScared = r.ReadBoolean(),
                ChickenReactionTicks = r.ReadInt32(),
                MoveResumeState = r.ReadByte(),
                MoveDestinationX = r.ReadInt32(),
                MoveDestinationY = r.ReadInt32(),
                MoveVelocityX = r.ReadInt32(),
                MoveVelocityY = r.ReadInt32(),
                MoveTicksRemaining = r.ReadUInt16(),
                MoveFlags = (ObjectMoveFlags)r.ReadByte(),
                TileLink = ReadLink(r),
                RollX = r.ReadInt32(),
                RollY = r.ReadInt32(),
                RollSpeed = r.ReadInt32(),
                Wear = r.ReadInt32(),
                RollSignX = r.ReadSByte(),
                RollSignY = r.ReadSByte(),
            };
            world.RawObjects.Add(loaded);
            world.AttachLoaded(loaded);
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

        world.TileHighlights.Read(r);
        world.NextDoorId = r.ReadInt32();
        int doorCount = r.ReadInt32();
        for (int i = 0; i < doorCount; i++)
        {
            world.RawDoors.Add(new DoorState
            {
                Id = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Unknown_0008 = r.ReadInt32(),
                TriggerId = r.ReadUInt16(),
                DoorId = r.ReadByte(),
                TileLink = ReadLink(r),
                PlayerId = r.ReadByte(),
                Flags = r.ReadByte(),
                UnderConstruction = r.ReadBoolean(),
                LockMode = r.ReadByte(),
                LockPickTicks = r.ReadInt32(),
                Open = r.ReadBoolean(),
                Orientation = r.ReadByte(),
                Health = r.ReadInt32(),
                RecoveryDeadline = r.ReadInt64(),
                Unknown_0011 = r.ReadBytes(DoorState.Unknown0011Length),
            });
        }
        int navStampCount = r.ReadInt32();
        for (int i = 0; i < navStampCount; i++) world.RawNavStamps.Add(new NavStamp(r.ReadInt32(), r.ReadByte(), r.ReadByte()));

        world.NextTrapId = r.ReadInt32();
        int trapCount = r.ReadInt32();
        for (int i = 0; i < trapCount; i++)
        {
            world.RawTraps.Add(new TrapState
            {
                Id = r.ReadInt32(),
                X = r.ReadInt32(),
                Y = r.ReadInt32(),
                Unknown_0008 = r.ReadInt32(),
                NumberOfShots = r.ReadByte(),
                TrapId = r.ReadByte(),
                TileLink = ReadLink(r),
                PlayerId = r.ReadByte(),
                Unknown_000F = r.ReadByte(),
                UnderConstruction = r.ReadBoolean(),
                Health = r.ReadInt32(),
                Heading = r.ReadInt32(),
                RechargeAt = r.ReadInt64(),
                RecoveryAt = r.ReadInt64(),
                Charge = r.ReadByte(),
                TriggerCount = r.ReadByte(),
                Hidden = r.ReadBoolean(),
                ThreatDuration = r.ReadByte(),
                Threat = r.ReadInt32(),
                TriggerCountdown = r.ReadByte(),
                TriggerCreatureId = r.ReadInt32(),
                SightTurn = r.ReadInt64(),
                SightTargetId = r.ReadInt32(),
                GuardRoomTile = r.ReadInt32(),
            });
        }
        world.NextChainOrder = r.ReadInt32();
        world.NextTileLinkOrder = r.ReadInt32();
        for (int i = 0; i < WorldState.TremorSlotCount; i++)
        {
            world.TremorSlots[i] = new TremorState
            {
                WorldX = r.ReadInt32(), WorldY = r.ReadInt32(), StartedAt = r.ReadUInt32(),
                DurationTicks = r.ReadUInt16(), Radius = r.ReadUInt16(),
            };
        }
        for (int i = 0; i < WorldState.HeroSlotCount; i++)
            world.HeroSlotArray[i] = new HeroSlot { CreatureId = r.ReadInt32(), Value = r.ReadByte() };
        int borderCursorCount = r.ReadInt32();
        for (int i = 0; i < borderCursorCount; i++)
            world.RoomBorderCursorList.Add(new RoomBorderCursor
            {
                X = r.ReadByte(), Y = r.ReadByte(), Orientation = r.ReadByte(), Phase = r.ReadInt32(),
            });

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

        world.Messages.Read(r);
        for (int i = 0; i < TriggerState.FlagCount; i++) world.Triggers.RawFlags[i] = r.ReadByte();
        for (int i = 0; i < TriggerState.TimerCount; i++) world.Triggers.RawTimers[i] = r.ReadInt32();
        world.Triggers.SpecialValue = r.ReadInt32();
        world.Triggers.SpecialValueWritten = r.ReadBoolean();
        world.Triggers.RawWorldOptions = r.ReadByte();
        world.Triggers.LevelTimeLimit = r.ReadInt32();
        world.Triggers.SpecialInfoNextRoomTypeId = r.ReadByte();
        world.Triggers.SpecialInfoPlayerId = r.ReadByte();
        world.Triggers.MaximumSlaps = r.ReadInt32();
        int triggerRecordCount = r.ReadInt32();
        for (int i = 0; i < triggerRecordCount; i++)
            world.Triggers.RestoreRecord(r.ReadInt32(), r.ReadUInt16(), r.ReadBoolean(), r.ReadByte());

        var simulation = new GameSimulation(database, world, rng, systems, triggers);
        simulation.Navigation.Read(r);
        return simulation;
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

    /// <summary>A saved game loaded as DK2 loads one, where <see cref="FromBytes"/> restores an exact
    /// checkpoint. DK2's load (<c>0x0044F4C0</c>) re-adds every room tile row by row, each to its saved
    /// room, so every chain comes back row-major (<see cref="SlabChain.AsLoaded"/>). The owner saw a
    /// dragged Prison's door move to the west end on a reload (room-slab-chain.md §6).</summary>
    public static GameSimulation LoadGame(
        byte[] bytes,
        GameDatabase database,
        IEnumerable<ISimulationSystem>? systems = null,
        TriggerTree? triggers = null)
    {
        GameSimulation simulation = FromBytes(bytes, database, systems, triggers);
        SlabChain.AsLoaded(simulation.World);
        return simulation;
    }
}
