// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Combat;
using DK2.Simulation.Navigation;
using DK2.Simulation.Steering;
using DK2.Simulation.Systems;

namespace DK2.Simulation;

/// <summary>The trap's update <c>0x004A84E0</c> and what it calls — docs/research/traps.md: the
/// owner test, the threat clock, the turn toward a target, the recharge gate and its three
/// selectors, the charge, readiness and the firing (<c>0x004A7FD0</c>, <c>0x004A80E0</c>), the
/// threat it then shares among nearby enemies every update (<c>0x004B32A0</c>), the Trigger's
/// propagation (<c>0x004A8E70</c>), the Guard Post's report to its room and the recovery. A trap
/// fires only when its shot's damage type and process have a built handler (<see cref="FiringBuilt"/>);
/// the reveal scan and every effect, sound and animation are not built, and disarming is unreachable
/// in v1.70: nothing makes a creature a trap's disarmer (Q455).</summary>
[Dk2Behavior(Confidence.StronglyInferred, "docs/research/traps.md")]
public static class TrapRules
{
    private const string SystemName = "traps";

    /// <summary>Definition flags (<c>def+0x88</c>) the update and firing read.</summary>
    public const uint RevealsOnFire = 0x2;
    public const uint StartsHidden = 0x20;
    public const uint TurnsToTarget = 0x80;
    public const uint GuardPostBranch = 0x800;

    /// <summary>Automatic trigger selectors (<c>def+0xBD</c>, table <c>0x0066D430</c>).</summary>
    public const byte SightSelector = 1;
    public const byte TileSelector = 2;
    public const byte TriggerSelector = 3;

    /// <summary>The trap's turn rate, vtable <c>+0x58</c> (<c>0x004B5C50</c>), and the sweep a turning
    /// trap makes with nobody in sight.</summary>
    public const int TurnRate = 0x100;
    public const int SweepStep = 10;

    /// <summary>The shot definition's <c>Flags</c> bit <c>0x004A7FD0</c> tests before launching a
    /// shot fired at a target.</summary>
    public const uint ShotLaunches = 0x20;

    /// <summary>The threat's radius for every selector but sight, five tiles.</summary>
    public const int ThreatRadius = 0x5000;

    /// <summary>A Trigger counts and reaches traps within one tile: squared distance <c>&lt;= 0x1000</c>.</summary>
    public const uint TriggerReachSquared = 0x1000;

    /// <summary>Trap 16, the Jack In The Box: the tile selector wants a worker and the propagation
    /// fires it without a sight test. Its shot's process 39 (<c>0x004B02E0</c>) makes a creature of
    /// type 0x2E in state 273, which is not built, so it never fires here.</summary>
    public const byte JackInTheBoxId = 16;

    // ------------------------------------------------------------------ the sine table

    /// <summary><c>0x006C1488</c>: 2560 dwords, <c>trunc(65536·sin(2πi/2048))</c> for every entry,
    /// the cosine (<c>0x006C1C88</c>) the same table a quarter turn on. Built from the first quarter
    /// by the symmetry the binary's table has exactly, so no platform's last ulp can move it.</summary>
    private static readonly int[] SineTable = BuildSine();

    private static int[] BuildSine()
    {
        var table = new int[2560];
        for (int i = 0; i <= 512; i++) table[i] = (int)(65536.0 * Math.Sin(2.0 * Math.PI * i / 2048.0));
        table[512] = 65536;
        for (int i = 513; i <= 1024; i++) table[i] = table[1024 - i];
        for (int i = 1025; i < 2048; i++) table[i] = -table[i - 1024];
        for (int i = 2048; i < 2560; i++) table[i] = table[i - 2048];
        return table;
    }

    public static int Sine(int heading) => SineTable[heading & Bearing.Mask];

    public static int Cosine(int heading) => SineTable[(heading & Bearing.Mask) + 512];

    // ------------------------------------------------------------------ scope

    /// <summary>Whether this trap's firing is built: a shot whose damage type and process, and
    /// those of every shot it chains, have a handler here (shots.md), or no shot, whose firing
    /// <c>0x004A80E0</c> ends at once. In the shipped table: Sentry, Fireburst, Lightning, Freeze,
    /// Trigger, Guard Post, Magic Door, Spare Door 1 and Hero Sentry.</summary>
    public static bool FiringBuilt(GameDatabase database, TrapDef definition)
    {
        Dictionary<byte, bool> known = Scope.GetValue(database, _ => []);
        lock (known)
        {
            if (!known.TryGetValue(definition.TrapId, out bool built))
                known[definition.TrapId] = built = definition.TrapId != JackInTheBoxId
                    && (definition.ShotTypeId == 0 || ShotBuilt(database, definition.ShotTypeId, 0));
            return built;
        }
    }

    // The answer depends only on the database's definitions, so it is worked out once per trap type.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameDatabase, Dictionary<byte, bool>> Scope = new();

    private static bool ShotBuilt(GameDatabase database, byte shotId, int depth)
    {
        if (shotId == 0 || depth > 4 || !database.Shots.TryGetValue(shotId, out ShotDef? shot)) return false;
        if (shot.DamageType is not (0 or ShotRules.SquareDamageType or ShotRules.AreaDamageType or ShotRules.ContactDamageType)) return false;
        if (shot.ProcessType != 0 && !BuiltProcesses.Contains(shot.ProcessType)) return false;
        if (shot.ProcessType == ShotRules.GasProcess && shot.DamageType == 0) return false;
        return Chained(database, shot.DeathShotId, depth) && Chained(database, shot.HitSolidShotId, depth)
            && Chained(database, shot.HitLavaShotId, depth) && Chained(database, shot.HitWaterShotId, depth)
            && Chained(database, shot.HitThingShotId, depth);
    }

    private static bool Chained(GameDatabase database, byte shotId, int depth) =>
        shotId == 0 || ShotBuilt(database, shotId, depth + 1);

    private static readonly byte[] BuiltProcesses =
    [
        ShotRules.CreateObjectProcess, ShotRules.ModifyHealthProcess, ShotRules.SpeedProcess, ShotRules.ArmourProcess,
        ShotRules.LightningProcess, ShotRules.InvulnerableProcess, ShotRules.FreezeProcess, ShotRules.ChickenProcess,
        ShotRules.InvisibleProcess, ShotRules.ReaperStunProcess, ShotRules.InfernoProcess, ShotRules.InstantProcess,
        ShotRules.DrainProcess, ShotRules.PoisonProcess, ShotRules.AlarmProcess, ShotRules.FearProcess, ShotRules.GasProcess,
    ];

    // ------------------------------------------------------------------ the constructor and completion

    /// <summary>The constructor's seeding (<c>0x004A7940</c>): health from the definition, hidden
    /// from its flag <c>0x20</c>, a Guard Post's room (<c>0x004A7A9C</c>); every timer and counter zero.</summary>
    public static void Seed(WorldState world, GameDatabase database, TrapState trap, TrapDef? definition)
    {
        if (definition is null) return;
        trap.Health = definition.Health;
        trap.Hidden = (definition.Flags & StartsHidden) != 0;
        if ((definition.Flags & GuardPostBranch) != 0) trap.GuardRoomTile = NearestGuardRoom(world, database, trap);
    }

    // ------------------------------------------------------------------ the Guard Post

    /// <summary><c>0x004C61C0(9, &amp;trap position, &amp;room, 0)</c> on the trap's owner: of the
    /// owner's Guard Rooms, the one whose first slab's centre is strictly nearest the trap, as that
    /// slab's tile; -1 for none. Anywhere on the map, not only the room the trap stands in. DK2
    /// walks its room list, ours the rooms in tile order, so only an exact tie could differ.</summary>
    public static int NearestGuardRoom(WorldState world, GameDatabase database, TrapState trap)
    {
        int found = -1;
        uint best = uint.MaxValue;
        foreach (RoomInstance room in RoomIndex.Build(world, database))
        {
            if (room.OwnerPlayerId != trap.PlayerId || room.Definition.TypeId != RoomTypeId.GuardRoom) continue;
            int first = SlabChain.FirstSlab(world, room);
            uint distance = DistanceSquared(CreatureState.CentreOf(first % world.Width), CreatureState.CentreOf(first / world.Width),
                trap.PosX, trap.PosY);
            if (distance >= best) continue;
            best = distance;
            found = first;
        }
        return found;
    }

    /// <summary>Whether a Guard Post's room (<c>+0x113</c>) is the one a creature holds a place in
    /// (<c>+0x3B9</c>). DK2 compares two ids, so a post with no room matches a creature with none.</summary>
    public static bool SameRoom(WorldState world, GameDatabase database, TrapState trap, RoomPlace place)
    {
        if (trap.GuardRoomTile < 0) return !place.IsHeld;
        return place.IsHeld && RoomIndex.RoomAt(world, database, trap.GuardRoomTile) is { } room && place.In(room);
    }

    /// <summary>The update's Guard Post arm (<c>0x004A85BB</c>–<c>0x004A8AA1</c>), on the turns
    /// whose sum with the trap's id has bit 3 set: refresh the room, then, with one, report the first
    /// enemy in reach to it. The effect it makes every eighth turn is presentation.</summary>
    private static void GuardPost(SimulationContext context, ISightBlocker sight, TrapState trap, long now)
    {
        if (((now + trap.Id) & 8) == 0) return;
        WorldState world = context.World;
        GameDatabase database = context.Database;
        trap.GuardRoomTile = NearestGuardRoom(world, database, trap);
        if (trap.GuardRoomTile < 0 || PostEnemy(world, database, sight, trap) is not { } enemy) return;
        ReportToRoom(context, trap, enemy);
    }

    /// <summary><c>0x004A86A7</c>–<c>0x004A87E2</c>: the first creature of a hostile player, strictly
    /// within <c>GuardPostReportingDistanceTiles</c>, in sight and attackable. The first, not the
    /// nearest. Players and creatures in <see cref="FindSightTarget"/>'s order.</summary>
    private static CreatureState? PostEnemy(WorldState world, GameDatabase database, ISightBlocker sight, TrapState trap)
    {
        int reach = database[VariableTypeId.GuardPostReportingDistanceTiles];
        int limit = (reach << 12) * reach;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        foreach (byte player in world.PlayerIds)
        {
            if (!CombatRules.IsHostile(world, trap.PlayerId, player)) continue;
            for (int i = creatures.Count - 1; i >= 0; i--)
            {
                CreatureState creature = creatures[i];
                if (creature.OwnerPlayerId != player || !IsHostileOnMap(world, trap.PlayerId, creature)) continue;
                if ((int)DistanceSquared(trap.PosX, trap.PosY, creature.PosX, creature.PosY) >= limit) continue;
                if (!LineOfSight.Clear(sight, creature.PosX, creature.PosY, trap.PosX, trap.PosY)) continue;
                if (CombatRules.IsAttackable(creature)) return creature;
            }
        }
        return null;
    }

    /// <summary><c>0x004A87EA</c>–<c>0x004A8A9B</c>: the enemy's strength near it less three quarters
    /// of the owner's (<c>0x004FAC40</c> at five tiles), and nobody already responding there
    /// (<c>0x004754D0</c>); then the owner's creatures holding a place in the post's room run to the
    /// enemy in state 254 until their level-scaled <c>Threat</c> covers it. combat-alarm.md §3's
    /// filters, without its job-walk alternative, and with the creature's own run speed.</summary>
    private static void ReportToRoom(SimulationContext context, TrapState trap, CreatureState enemy)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        int wanted = FearRules.LocalStrength(context, enemy.OwnerPlayerId, enemy.PosX, enemy.PosY)
            - (FearRules.LocalStrength(context, trap.PlayerId, enemy.PosX, enemy.PosY) * 3 / 4);
        if (wanted <= 0 || FearRules.AlreadyResponding(world, trap.PlayerId, enemy.PosX, enemy.PosY)) return;
        if (RoomIndex.RoomAt(world, database, trap.GuardRoomTile) is not { } room) return;

        int sent = 0;
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = 0; i < creatures.Count && wanted >= 0; i++)
        {
            CreatureState c = creatures[i];
            if (c.OwnerPlayerId != trap.PlayerId || !c.HeldPlace.In(room)) continue;
            CreatureDef def = database.CreatureById(c.CreatureTypeId);
            if (def.Flags.HasFlag(CreatureFlags.IsWorker) || def.Flags.HasFlag(CreatureFlags.IsHorny)) continue;
            if (CallToArmsRules.IsUnderCallToArms(world, c) || NeedRules.ResolvedCategory(c) == CreatureStateCategory.Combat) continue;
            if (!CombatRules.MayLookForAFight(world, database, c, force: false)) continue;
            if (c.Health <= CreatureLevelRules.MaxHealth(database, def, c.Level) / 4) continue;
            // state+0x3C == 2 is the alarm's unread test too (Q391).
            if (CreatureStateTable.Get(c.Dk2StateId).Group is 3 or 7) continue;
            if (c.Dk2StateId == StateShadow.TravelToPoint && c.UtilityNewState == CombatStates.ReportingDanger) continue;
            // +0x36F == 1 (the Imp) routes with 0x80, everyone else with 0 (0x004A89B5).
            int options = c.CreatureTypeId == CombatRules.ImpCreatureTypeId ? MoveOptions.Loose : MoveOptions.Plain;
            if (!context.Navigation.TryRouteTo(c, enemy.PosX, enemy.PosY, options)) continue;
            c.CurrentJob = JobType.None;
            c.JobTileIndex = -1;
            c.Running = true;
            CombatStates.Travel(context, c, CombatStates.ReportingDanger);
            c.ReportTicks = 0;
            c.CombatStateEnteredTick = context.Tick.Value;
            sent++;
            wanted -= FearRules.ThreatAtLevel(database, def, c.Level);
        }
        if (sent != 0)
            context.Emit(SystemName, "guard_post_reported", $"trap={trap.Id} player={trap.PlayerId} enemy={enemy.Id} sent={sent} left={wanted}");
    }

    /// <summary><c>0x004A96D0</c>: construction done. A Trigger adds itself to every own non-Trigger
    /// trap within a tile (<c>0x004A9720</c>); any other trap counts the own Triggers within a tile
    /// afresh (<c>0x004A9A00</c>). Both look at the first own trap on each tile of the 3x3 square, so
    /// only the four sides and the trap itself are near enough. The upkeep it registers
    /// (<c>0x004C1B60</c>) is not built.</summary>
    public static void Complete(WorldState world, GameDatabase database, TrapState trap)
    {
        trap.UnderConstruction = false;
        if (!database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition)) return;
        if (definition.TriggerType == TriggerSelector)
        {
            foreach (TrapState near in Near(world, trap))
                if (Selector(database, near) != TriggerSelector) near.TriggerCount = (byte)((near.TriggerCount + 1) & 0xF);
            return;
        }
        trap.TriggerCount = 0;
        foreach (TrapState near in Near(world, trap))
            if (Selector(database, near) == TriggerSelector) trap.TriggerCount = (byte)((trap.TriggerCount + 1) & 0xF);
    }

    /// <summary>The cleanup <c>0x004A7B20</c>'s gameplay half: a completed Trigger takes itself off
    /// its neighbours' counts (<c>0x004A9890</c>, never below zero). Its upkeep release, the sounds
    /// and the unlinking are the rest.</summary>
    public static void Remove(SimulationContext context, TrapState trap)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (!trap.UnderConstruction && Selector(database, trap) == TriggerSelector)
            foreach (TrapState near in Near(world, trap))
                if (Selector(database, near) != TriggerSelector && near.TriggerCount != 0) near.TriggerCount--;
        world.RemoveTrap(trap);
        context.Emit(SystemName, "trap_removed", $"trap={trap.Id} type={trap.TrapId} player={trap.PlayerId} at={trap.X},{trap.Y}");
    }

    private static IEnumerable<TrapState> Near(WorldState world, TrapState trap)
    {
        for (int y = Math.Max(0, trap.Y - 1); y < Math.Min(world.Height, trap.Y + 2); y++)
            for (int x = Math.Max(0, trap.X - 1); x < Math.Min(world.Width, trap.X + 2); x++)
            {
                if (world.TrapAt(x, y, trap.PlayerId) is not { } near) continue;
                if (DistanceSquared(near.PosX, near.PosY, trap.PosX, trap.PosY) <= TriggerReachSquared) yield return near;
            }
    }

    private static byte Selector(GameDatabase database, TrapState trap) =>
        database.Traps.TryGetValue(trap.TrapId, out TrapDef? definition) ? definition.TriggerType : (byte)0;

    public static bool IsDead(TrapState trap) => trap.Health <= 0;

    /// <summary><c>0x004B2CE0</c> on a trap: the signed delta applied flat. Its callers that reach a
    /// trap here are the recovery and area damage type 2 (<c>0x004AC6D0</c>); a swing reaches one only
    /// through possession, which is not built (traps.md §10).</summary>
    public static void Damage(TrapState trap, int delta) => trap.Health += delta;

    // ------------------------------------------------------------------ the tile chain

    /// <summary>The tile chain's order (<c>0x004B5020</c>, <c>0x004B4E70</c>): a creature that has
    /// changed tile is linked at the head of its new tile's chain. Run once a turn before the traps,
    /// over the creatures newest first as DK2's thing pass updates them (<c>0x004B4A10</c>); ours
    /// notices a move at the turn's end where DK2 links at the move itself.</summary>
    public static void RelinkCreatures(WorldState world)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        for (int i = creatures.Count - 1; i >= 0; i--)
        {
            CreatureState creature = creatures[i];
            int tile = CreatureDeathRules.IsCorpse(creature) || !CombatPitRules.IsOnTheMap(creature) || !world.Contains(creature.Tile)
                ? -1
                : (creature.Tile.Y * world.Width) + creature.Tile.X;
            if (tile == creature.ChainTileIndex) continue;
            creature.ChainTileIndex = tile;
            creature.ChainOrder = tile < 0 ? 0 : ++world.NextChainOrder;
        }
    }

    /// <summary><c>0x00453AC0</c>: the first creature in the tile's chain whose owner is neither the
    /// trap's nor neutral and who is not some trap's disarmer. No alliance test.</summary>
    public static CreatureState? FirstOnTile(WorldState world, int x, int y, byte trapOwner)
    {
        int tile = (y * world.Width) + x;
        CreatureState? first = null;
        foreach (CreatureState creature in world.Creatures)
        {
            if (creature.ChainTileIndex != tile) continue;
            if (creature.OwnerPlayerId == trapOwner || creature.OwnerPlayerId == PlayerIds.Neutral) continue;
            if (first is null || creature.ChainOrder > first.ChainOrder) first = creature;
        }
        return first;
    }

    // ------------------------------------------------------------------ the update

    /// <summary>One update (<c>0x004A84E0</c>), in its order. A trap whose firing is not built still
    /// loses its owner, counts its clock down and recovers; it does not look for a target.</summary>
    public static void Update(SimulationContext context, ISightBlocker sight, TrapState trap, TrapDef definition)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        long now = context.Tick.Value;

        // The tile's owner is not the trap's: a blueprint leaves its keeper's list, and the trap dies.
        if (world.Contains(trap.X, trap.Y) && world.Tile(trap.X, trap.Y).OwnerPlayerId != trap.PlayerId)
        {
            if (trap.UnderConstruction) CrateRules.Unregister(world, trap.PlayerId, new BlueprintRef(CrateRules.TrapKind, trap.Id));
            if (trap.Health > 0)
                context.Emit(SystemName, "trap_owner_lost", $"trap={trap.Id} player={trap.PlayerId} tile_owner={world.Tile(trap.X, trap.Y).OwnerPlayerId}");
            trap.Health = 0;
        }
        if (trap.ThreatDuration != 0) trap.ThreatDuration--;

        if (!trap.UnderConstruction && !IsDead(trap) && (definition.Flags & GuardPostBranch) != 0)
            GuardPost(context, sight, trap, now);
        if (!trap.UnderConstruction && !IsDead(trap) && FiringBuilt(database, definition))
        {
            if ((definition.Flags & TurnsToTarget) != 0) Turn(world, database, sight, trap, definition, now);
            if (now >= trap.RechargeAt)
            {
                int target = definition.TriggerType switch
                {
                    SightSelector => trap.TriggerCount == 0 ? FindSightTarget(world, database, sight, trap, definition, now, wider: false) : 0,
                    TileSelector => TileTarget(world, database, trap, definition),
                    TriggerSelector => FirstOnTile(world, trap.X, trap.Y, trap.PlayerId)?.Id ?? 0,
                    _ => 0,
                };
                if (target == 0) trap.Charge = 0;
                else
                {
                    trap.Charge = TrapActivationRules.AdvanceCharge(trap.Charge, true);
                    if (TrapActivationRules.ChargeReached(definition, trap.Charge))
                        Attempt(context, trap, definition, target);
                }
            }
        }

        if (trap.Threat != 0)
            ShareThreat(world, sight, trap, trap.Threat,
                definition.TriggerType == SightSelector ? (definition.RangeTiles + 1) << 12 : ThreatRadius);

        if (definition.TriggerType == TriggerSelector && trap.TriggerCountdown != 0)
        {
            if (world.CreatureById(trap.TriggerCreatureId) is not { } struck || CreatureDeathRules.IsCorpse(struck))
                trap.TriggerCreatureId = 0;
            Propagate(context, sight, trap);
        }

        if (!IsDead(trap) && now > trap.RecoveryAt && definition.HealthGain != 0)
        {
            int gain = Math.Min(definition.HealthGain, definition.Health - trap.Health);
            if ((short)gain != 0) trap.Health += (short)gain; // 0x004A8E2F passes the signed low word
            trap.RecoveryAt += SimulationTiming.OriginalTicksPerGameSecond;
        }
    }

    /// <summary>The readiness <c>0x004A7D10</c> and what follows it: short of mana, the warning; ready,
    /// the firing at the target.</summary>
    private static void Attempt(SimulationContext context, TrapState trap, TrapDef definition, int target)
    {
        TrapReadiness readiness = Readiness(context.World, trap, definition, context.Tick.Value);
        if (readiness == TrapReadiness.InsufficientMana)
            NoMana(context, trap, definition);
        else if (readiness == TrapReadiness.Ready) Fire(context, trap, definition, target);
    }

    /// <summary>Readiness 2, short of mana: the no-mana effect (presentation) and <c>0x004C1A30</c> on
    /// the owner, which posts message 53 with Data1 1 once the player's clock <c>+0xAA</c> is due
    /// and sets it fifteen seconds on. Every site that answers 2 calls it.</summary>
    private static void NoMana(SimulationContext context, TrapState trap, TrapDef definition)
    {
        context.Emit(SystemName, "trap_no_mana", $"trap={trap.Id} player={trap.PlayerId} cost={definition.ManaCostToFire}");
        WorldState world = context.World;
        if (!world.HasPlayer(trap.PlayerId)) return;
        ref PlayerState player = ref world.Player(trap.PlayerId);
        long now = context.Tick.Value;
        if (now < player.TrapNoManaMessageAt) return;
        world.Messages.Post(world, trap.PlayerId, PlayerMessageKind.ManaShortfall, TrapNoManaSubtype, 0);
        player.TrapNoManaMessageAt = now + (15L * SimulationTiming.OriginalTicksPerGameSecond);
    }

    /// <summary>Message 53's Data1 a trap short of mana passes (<c>0x004C1A53</c>).</summary>
    public const int TrapNoManaSubtype = 1;

    public static TrapReadiness Readiness(WorldState world, TrapState trap, TrapDef definition, long now) =>
        TrapActivationRules.Readiness(definition, trap.PlayerId, trap.UnderConstruction, now, trap.RechargeAt,
            world.HasPlayer(trap.PlayerId) ? world.Player(trap.PlayerId).Mana : 0, beingDisarmed: false); // +0x115 is never set (Q455)

    /// <summary>The turn (definition flag <c>0x80</c>): toward the sight finder's target at range plus
    /// two (<c>0x004B2AE0</c>), or, with nobody, ten steps on (<c>0x004B2C10</c>), at most
    /// <see cref="TurnRate"/> a turn. The finder's answer is kept for the turn, so a turning sight
    /// trap fires at the wider range too (Q452).</summary>
    private static void Turn(WorldState world, GameDatabase database, ISightBlocker sight, TrapState trap, TrapDef definition, long now)
    {
        int target = FindSightTarget(world, database, sight, trap, definition, now, wider: true);
        int heading = trap.Heading;
        if (target != 0 && world.CreatureById(target) is { } creature)
            Bearing.TurnToward(ref heading, Bearing.Of(creature.PosX - trap.PosX, creature.PosY - trap.PosY), TurnRate);
        else Bearing.TurnToward(ref heading, (trap.Heading + SweepStep) & Bearing.Mask, TurnRate);
        trap.Heading = heading;
    }

    /// <summary>Selector 2 (<c>0x004A92C0</c>): refused near a Trigger; otherwise the tile's first
    /// creature, unless it is dying, flying or <c>IsHorny</c> — and then nobody, not the next. Trap 16
    /// also wants a worker.</summary>
    private static int TileTarget(WorldState world, GameDatabase database, TrapState trap, TrapDef definition)
    {
        if (trap.TriggerCount != 0) return 0;
        if (FirstOnTile(world, trap.X, trap.Y, trap.PlayerId) is not { } creature) return 0;
        if (CreatureDeathRules.IsDying(creature)) return 0;
        CreatureDef type = database.CreatureById(creature.CreatureTypeId);
        if (IsFlying(creature, type) || type.Flags.HasFlag(CreatureFlags.IsHorny)) return 0;
        if (trap.TrapId == JackInTheBoxId && !type.Flags.HasFlag(CreatureFlags.IsWorker)) return 0;
        return creature.Id;
    }

    /// <summary>The creature's flying slot, vtable <c>+0x88</c> (<c>0x0048ED40</c>): <c>CanFly</c>, except
    /// in states 217 and 218. Not built: a worker's answer from its state bit 8, the five animations
    /// that answer no, and the <c>0x00494700</c> arm; a worker never flies here.</summary>
    private static bool IsFlying(CreatureState creature, CreatureDef type) =>
        !type.Flags.HasFlag(CreatureFlags.IsWorker)
        && creature.Dk2StateId is not (217 or 218)
        && type.Flags.HasFlag(CreatureFlags.CanFly);

    /// <summary>The sight finder <c>0x004A9400</c>: once a turn, the strictly nearest attackable,
    /// visible creature of a hostile player inside the range (plus two when the turn asks), never an
    /// <c>IsHorny</c> one. Last turn's answer seeds it if it is still visible and attackable, with an
    /// inclusive boundary. Ours walks players by id and each one's creatures newest first, where DK2
    /// walks its player list and each owner list; only an exact tie can tell them apart.</summary>
    public static int FindSightTarget(WorldState world, GameDatabase database, ISightBlocker sight, TrapState trap,
        TrapDef definition, long now, bool wider)
    {
        if (now == trap.SightTurn) return trap.SightTargetId;
        int range = definition.RangeTiles + (wider ? 2 : 0);
        uint limit = unchecked((uint)(range * range * 0x1000));
        uint best = 0x7FFFFFFF;
        int found = 0;
        if (now == trap.SightTurn + 1 && world.CreatureById(trap.SightTargetId) is { } kept && !CreatureDeathRules.IsCorpse(kept))
        {
            found = trap.SightTargetId;
            if (!LineOfSight.Clear(sight, trap.PosX, trap.PosY, kept.PosX, kept.PosY) || !CombatRules.IsAttackable(kept)
                || database.CreatureById(kept.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny))
                found = 0;
            else
            {
                best = DistanceSquared(kept.PosX, kept.PosY, trap.PosX, trap.PosY);
                if (limit < best) found = 0;
            }
        }

        IReadOnlyList<CreatureState> creatures = world.Creatures;
        foreach (byte player in world.PlayerIds)
        {
            if (!CombatRules.IsHostile(world, trap.PlayerId, player)) continue;
            for (int i = creatures.Count - 1; i >= 0; i--)
            {
                CreatureState creature = creatures[i];
                if (creature.OwnerPlayerId != player || !CombatRules.IsAttackable(creature)) continue;
                if (database.CreatureById(creature.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsHorny)) continue;
                uint distance = DistanceSquared(creature.PosX, creature.PosY, trap.PosX, trap.PosY);
                if (distance >= limit || distance >= best) continue;
                if (!LineOfSight.Clear(sight, trap.PosX, trap.PosY, creature.PosX, creature.PosY)) continue;
                found = creature.Id;
                best = distance;
            }
        }
        trap.SightTurn = now;
        trap.SightTargetId = found;
        return found;
    }

    // ------------------------------------------------------------------ the firing

    /// <summary><c>0x004A7FD0</c>: at a target, its position (half its height, which a flat shot does
    /// not have); with none, one tile along the heading. A shot fired at a target is launched when
    /// its definition says so.</summary>
    public static ShotState? Fire(SimulationContext context, TrapState trap, TrapDef definition, int target)
    {
        CreatureState? creature = target == 0 ? null : context.World.CreatureById(target);
        if (creature is null)
        {
            int aimX = trap.PosX + (int)(((long)Sine(trap.Heading) * 0x1000) >> 16);
            int aimY = trap.PosY - (int)(((long)Cosine(trap.Heading) * 0x1000) >> 16);
            return Shoot(context, trap, definition, aimX, aimY, null);
        }
        ShotState? shot = Shoot(context, trap, definition, creature.PosX, creature.PosY, creature);
        if (shot != null && (context.Database.Shots[shot.ShotTypeId].Flags & ShotLaunches) != 0)
            ShotRules.Launch(shot, context.Database.Shots[shot.ShotTypeId], creature.PosX, creature.PosY);
        return shot;
    }

    /// <summary><c>0x004A80E0</c>: the shot from the trap's origin — the definition's offsets turned by
    /// the heading, height dropped — told its target, aimed, given the definition's data words and,
    /// for a tile trap with a target, its first collision forced. Then the cost (not for the Good
    /// player), the reveal, the charge spent, the threat and the recharge; a Trigger keeps the
    /// creature and starts its countdown. No shot, and nothing after it happens.</summary>
    private static ShotState? Shoot(SimulationContext context, TrapState trap, TrapDef definition, int aimX, int aimY, CreatureState? target)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (definition.ShotTypeId == 0 || !database.Shots.TryGetValue(definition.ShotTypeId, out ShotDef? shotDef)) return null;
        int sine = Sine(trap.Heading), cosine = Cosine(trap.Heading);
        int originX = unchecked(((definition.ShotOffsetX * cosine) >> 16) - ((definition.ShotOffsetY * sine) >> 16) + trap.PosX);
        int originY = unchecked(((definition.ShotOffsetY * cosine) >> 16) + ((definition.ShotOffsetX * sine) >> 16) + trap.PosY);

        if (target != null) FightRules.AnswerTrap(context, target, trap);
        ShotState shot = ShotRules.Spawn(world, shotDef, trap.PlayerId, originX, originY, definition.ShotData1, definition.ShotData2, 0,
            target is null ? FightTargetKind.None : FightTargetKind.Creature, target?.Id ?? CombatRules.NoTarget,
            target?.OwnerPlayerId ?? 0, -1, aimX, aimY);
        if (target != null && definition.TriggerType is not (SightSelector or TriggerSelector)) shot.ForcedCollision = true;

        if ((definition.Flags & TrapActivationRules.UsesMana) != 0 && trap.PlayerId != PlayerIds.Good && world.HasPlayer(trap.PlayerId))
        {
            ref PlayerState player = ref world.Player(trap.PlayerId);
            player.Mana = Math.Max(0, player.Mana - definition.ManaCostToFire);
            player.OneShotManaSpent += definition.ManaCostToFire;
        }
        if ((definition.Flags & RevealsOnFire) != 0) trap.Hidden = false;
        trap.Charge = 0;
        uint duration = SimulationTiming.LoaderFixedSecondsToTicks(definition.ThreatSecondsRaw);
        trap.ThreatDuration = unchecked((byte)duration);
        trap.Threat = duration == 0 ? 0 : definition.Threat;
        long now = context.Tick.Value;
        trap.RechargeAt = now + SimulationTiming.LoaderFixedSecondsToTicks(definition.RechargeSecondsRaw);
        if (definition.TriggerType == TriggerSelector)
        {
            trap.TriggerCreatureId = target?.Id ?? 0;
            // Fixed_Multiply(ticks a second << 12, one tile) over the speed: ticks to cross a tile.
            long tile = ((long)SimulationTiming.OriginalTicksPerGameSecond << 12) * 0x1000 >> 12;
            trap.TriggerCountdown = unchecked((byte)((uint)tile / (uint)database[VariableTypeId.TriggerTrapTriggerSpeedTilesPerSecond]));
        }
        context.Emit(SystemName, "trap_fired",
            $"trap={trap.Id} type={trap.TrapId} player={trap.PlayerId} shot={shot.Id} shot_type={shotDef.ShotId} target={target?.Id ?? 0} at={originX},{originY} recharge={trap.RechargeAt}");
        return shot;
    }

    // ------------------------------------------------------------------ the threat

    /// <summary><c>0x004B32A0</c>: count every creature of a hostile player within the radius and in
    /// sight, then give each one inside a radius a tile smaller (at least one) the threat over that
    /// count, into its <c>TempFear</c> through <c>0x0045C5D0</c>. Its owner discount is the one
    /// <see cref="FearRules"/> does not hold (Q375), and its <c>ThreatThing</c> is not built.</summary>
    public static void ShareThreat(WorldState world, ISightBlocker sight, TrapState trap, int amount, int radius)
    {
        IReadOnlyList<CreatureState> creatures = world.Creatures;
        int outer = radius * (radius >> 12);
        int count = 0;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (IsHostileOnMap(world, trap.PlayerId, creature)
                && (int)DistanceSquared(creature.PosX, creature.PosY, trap.PosX, trap.PosY) < outer
                && LineOfSight.Clear(sight, trap.PosX, trap.PosY, creature.PosX, creature.PosY))
                count++;
        }
        if (count == 0) return;
        int inner = Math.Max(1, (radius >> 12) - 1);
        int innerSquared = inner * inner * 0x1000;
        int share = amount / count;
        for (int i = 0; i < creatures.Count; i++)
        {
            CreatureState creature = creatures[i];
            if (IsHostileOnMap(world, trap.PlayerId, creature)
                && (int)DistanceSquared(creature.PosX, creature.PosY, trap.PosX, trap.PosY) < innerSquared
                && LineOfSight.Clear(sight, trap.PosX, trap.PosY, creature.PosX, creature.PosY))
                creature.TempFear = unchecked((ushort)(creature.TempFear + share));
        }
    }

    /// <summary>A creature a player list walk reaches: on the map, not yet a body (class 6 in DK2).
    /// One held in a hand is on its owner's list in DK2; ours leaves it out.</summary>
    private static bool IsHostileOnMap(WorldState world, byte owner, CreatureState creature) =>
        CombatRules.IsHostile(world, owner, creature.OwnerPlayerId) && !CreatureDeathRules.IsCorpse(creature)
        && CombatPitRules.IsOnTheMap(creature);

    // ------------------------------------------------------------------ the Trigger

    /// <summary><c>0x004A8E70</c>, each update while the countdown runs: the reach grows from a quarter
    /// tile to a tile as it falls, and every own trap within it is fired — at the Trigger's creature if
    /// that one is alive and the Trigger can see it (a Trigger needs no sight), otherwise one tile
    /// toward the Trigger, unlaunched. Each answers through its own readiness, so one recharging
    /// does nothing. A trap whose firing is not built is passed over.</summary>
    public static void Propagate(SimulationContext context, ISightBlocker sight, TrapState trigger)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        trigger.TriggerCountdown--;
        uint reach = unchecked((uint)trigger.TriggerCountdown * 0xFFFFF000u + 0x4000u);
        uint quarter = reach >> 2;
        uint limit = unchecked(quarter * quarter) >> 12;
        int half = (int)(reach >> 14);
        int x0 = Math.Max(0, trigger.X - half), x1 = Math.Min(world.Width, trigger.X + half + 1);
        int y0 = Math.Max(0, trigger.Y - half), y1 = Math.Min(world.Height, trigger.Y + half + 1);
        long now = context.Tick.Value;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                if (world.TrapAt(x, y, trigger.PlayerId) is not { } found) continue;
                if (DistanceSquared(found.PosX, found.PosY, trigger.PosX, trigger.PosY) > limit) continue;
                if (!database.Traps.TryGetValue(found.TrapId, out TrapDef? definition) || !FiringBuilt(database, definition)) continue;
                CreatureState? struck = world.CreatureById(trigger.TriggerCreatureId) is { } c && !CreatureDeathRules.IsCorpse(c) ? c : null;
                if (struck != null && (definition.TriggerType == TriggerSelector
                    || LineOfSight.Clear(sight, trigger.PosX, trigger.PosY, struck.PosX, struck.PosY)))
                {
                    TrapReadiness ready = Readiness(world, found, definition, now);
                    if (ready == TrapReadiness.InsufficientMana)
                        NoMana(context, found, definition);
                    else if (ready == TrapReadiness.Ready) Fire(context, found, definition, struck.Id);
                    continue;
                }
                int toward = Bearing.Of(trigger.PosX - found.PosX, trigger.PosY - found.PosY);
                TrapReadiness readiness = Readiness(world, found, definition, now);
                if (readiness == TrapReadiness.InsufficientMana)
                    NoMana(context, found, definition);
                else if (readiness == TrapReadiness.Ready)
                    Shoot(context, found, definition,
                        found.PosX + (int)(((long)Sine(toward) * 0x1000) >> 16),
                        found.PosY - (int)(((long)Cosine(toward) * 0x1000) >> 16), null);
            }
    }

    /// <summary>The traps' squared distance: each axis squared and shifted down twelve, summed as DK2's
    /// unsigned dword.</summary>
    public static uint DistanceSquared(int ax, int ay, int bx, int by)
    {
        long dx = Math.Abs((long)ax - bx), dy = Math.Abs((long)ay - by);
        return unchecked((uint)((dx * dx) >> 12) + (uint)((dy * dy) >> 12));
    }
}
