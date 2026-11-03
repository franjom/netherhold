// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Systems;

/// <summary>The dungeon heart's own frame and the verdict it can bring, three of DK2's
/// routines in the order their turn runs them (docs/research/win-and-defeat.md, ADR-0162):
/// the heart room's update <c>0x004D9A60</c> regenerates the heart object every fifth turn
/// and, finding no object, ruins the room (<c>0x004E9130</c>, <c>+0x9A</c> bit 0); the
/// player frame's tail <c>0x004BB2A0</c> reads <c>KilledByPlayerId</c> and ends the game
/// — lost to heroes or neutrals, conquered by a keeper — then awards the win to the one
/// keeper left playing. The verdict is one-shot: a <c>WIN_GAME</c> already performed stands.
/// <para>The tail then runs the fallen keeper's consequences every turn (<see cref="DefeatRules"/>,
/// DK2's <c>0x004DAA10</c>). Each verdict goes through <see cref="VerdictRules.End"/>, which in
/// skirmish and multiplayer carries it to allies (Q359).</para></summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/win-and-defeat.md")]
public sealed class DungeonHeartSystem : ISimulationSystem
{
    private readonly bool _frames;
    private readonly HeartFrameStep _frame = new();

    /// <param name="frames">Whether this system also runs the room frame, the Heart handler's
    /// <c>0x004D9A60</c> own part. A host that walks the rooms in DK2's order has <see cref="HeartFrameStep"/>
    /// do it there, at the Heart's turn, and passes false: this system then runs the player frame's tail
    /// alone, at the tick's end.</param>
    public DungeonHeartSystem(bool frames = true) => _frames = frames;

    public string Name => "dungeon heart";

    public void Execute(SimulationContext context)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        if (_frames) RoomTickSystem.RunAlone(context, _frame);

        // The frame's tail is the player update's, which walks the active list (0x004C2310); a
        // verdict carries to allies, so which fallen keeper is judged first can decide who wins.
        foreach (PlayerId id in world.ActivePlayerOrder)
        {
            if (!PlayerIds.IsKeeper(id.Value)) continue;
            ref PlayerState player = ref world.Player(id);

            // A stamped killer is the whole condition, and the verdict depends only on which side
            // struck the blow.
            if (player.KilledByPlayerId == 0) continue;
            byte fallen = player.PlayerId.Value;
            if (DefeatRules.IsDeathTurn(context.Tick, player.HeartDiedTick)) DefeatRules.OnDeathTurn(context, fallen);
            if (DefeatRules.CreaturesGo(context.Tick, player.HeartDiedTick)) DefeatRules.ScatterCreatures(context, fallen);

            player = ref world.Player(fallen);
            byte verdict = PlayerIds.IsKeeper(player.KilledByPlayerId)
                ? PlayerStatus.Conquered
                : PlayerStatus.Lost;
            byte killer = player.KilledByPlayerId;
            VerdictRules.End(world, database, player.PlayerId, verdict, (who, status) => Report(context, who, status, fallen, killer));

            // The tail's award follows each fallen keeper's verdict, every turn (0x004BB39A).
            if (world.GameMode != GameMode.Campaign) AwardLastKeeperStanding(context);
        }
    }

    /// <summary>The fallen keeper's own verdict names its killer; one an alliance carried does not.</summary>
    private void Report(SimulationContext context, PlayerId id, byte status, byte fallen, byte killer) =>
        context.Emit(Name, status switch
        {
            PlayerStatus.Conquered => "conquered",
            PlayerStatus.Won => "won",
            _ => "lost",
        }, id == fallen ? $"player={id} by={killer}" : $"player={id} by alliance");

    /// <summary>DK2 counts the keepers still playing each turn a fallen keeper's tail runs, and only
    /// outside the campaign (<c>0x004BB39A</c>: mode 1 skips it); exactly one left means that one has
    /// won. Not mirrored: the count also skips <c>+0x76</c> bit 19 and the award bit 22, both UNKNOWN.</summary>
    private void AwardLastKeeperStanding(SimulationContext context)
    {
        WorldState world = context.World;
        int standing = 0;
        byte survivor = 0;
        for (int i = 0; i < world.PlayerCount; i++)
        {
            ref PlayerState player = ref world.PlayerAtIndex(i);
            if (!PlayerIds.IsKeeper(player.PlayerId.Value) || PlayerStatus.HasEnded(player.Status)) continue;
            standing++;
            survivor = player.PlayerId.Value;
        }

        if (standing == 1)
            VerdictRules.End(world, context.Database, survivor, PlayerStatus.Won,
                (id, status) => context.Emit(Name, status == PlayerStatus.Won ? "won" : "lost",
                    id == survivor ? $"player={id} last keeper standing" : $"player={id} by alliance"));
    }
}

/// <summary>The Dungeon Heart handler's own part (<c>DungeonHeart_Update</c>, <c>0x004D9A60</c>), after the store
/// sweep and the responders' dispatch it calls first (<c>0x004D9B11</c>): the opening sequence, the regeneration
/// and the ruin when the Heart object is gone. The handler draws nothing of its own; the destroyed Heart's
/// <c>Heart_OnDestroyed</c> draws (<c>0x004DA560</c>, from the thing pass) are not in ours, and the message post at
/// <c>0x004D9AFD</c> (0x27 at half health, 0x28 at a tenth) has no counterpart. <see cref="DungeonHeartSystem"/> runs it itself
/// when it runs without a room walk.</summary>
[Dk2Behavior(Confidence.Confirmed, "docs/research/win-and-defeat.md")]
public sealed class HeartFrameStep : IRoomTickStep
{
    private const string SystemName = "dungeon heart";

    public string Name => SystemName;

    public void Execute(SimulationContext context) => RoomTickSystem.RunAlone(context, this);

    /// <summary>A Heart room of a keeper that has a heart tile in it: the keeper's frame runs once, whatever
    /// the room's shape.</summary>
    public bool Applies(SimulationContext context, RoomInstance room)
    {
        if (room.Definition.TypeId != RoomTypeId.DungeonHeart || !PlayerIds.IsKeeper(room.OwnerPlayerId)
            || !context.World.HasPlayer(room.OwnerPlayerId))
            return false;
        int tile = ImpSupplyRules.HeartTileOf(context.World, context.Database, new PlayerId(room.OwnerPlayerId));
        return tile >= 0 && room.Contains(tile);
    }

    public void Tick(SimulationContext context, RoomInstance room)
    {
        WorldState world = context.World;
        GameDatabase database = context.Database;
        ref PlayerState player = ref world.Player(room.OwnerPlayerId);
        // Object_Update 0x0049BD20 requests the object's authored death effect before
        // deletion, independently of the room opening and the stamped killer.
        // The central Heart pit has floor zero (0x004545C0), preserving its creation Z0.
        if (!player.HeartRuined && player.HeartHealth <= 0 && player.HeartDiedTick == context.Tick
            && database.Objects.TryGetValue(13, out ObjectDef? heart))
        {
            (ushort x, ushort y) = ManaRemovalLifecycle.HeartTile(world, database, player.PlayerId);
            context.RequestEffect(Name, ViewEffectRequest.AtTileCentre(new EffectId(heart.DeathEffectId),
                player.PlayerId, new TilePos(x, y), new Fp32(0)));
        }
        if (!player.HeartRuined) HeartIntroRules.RoomFrame(context, player.PlayerId);
        // The ordinary frame waits for the opening sequence (0x004D9BA5): no regeneration
        // and no ruin until nine seconds after its start.
        if (HeartIntroRules.SequenceOver(HeartIntroRules.Turn(context.Tick), database))
            RoomFrame(context, ref player, database[VariableTypeId.DungeonHeartObjectHealth],
                database[VariableTypeId.DungeonHeartHealthRegenerationPerSecond]);
    }

    /// <summary>Every <c>var19 + 1</c> turns (<see cref="PlayerState.HeartRegenCountdown"/>,
    /// <c>0x004D9A60</c>) the heart heals by the regeneration rate, never past its
    /// maximum and never from zero — a dead heart does not come back. A full heart is not
    /// touched at all (DK2 skips the call when the capped amount is zero), so its last source
    /// stays whoever struck it. The room whose heart object is gone becomes a ruin on this
    /// frame once the opening interval has ended.</summary>
    private static void RoomFrame(
        SimulationContext context, ref PlayerState player, int max, int regen)
    {
        if (player.HeartHealth <= 0)
        {
            if (player.HeartRuined) return;
            player.HeartRuined = true;
            context.Emit(SystemName, "heart_ruined", $"player={player.PlayerId}");
            return;
        }

        if (player.HeartRegenCountdown != 0)
        {
            player.HeartRegenCountdown--;
            return;
        }
        player.HeartRegenCountdown = (byte)SimulationTiming.TicksPerSecond;
        int amount = Math.Min(regen, max - player.HeartHealth);
        if (amount <= 0) return;
        context.World.RegenerateHeart(player.PlayerId, amount);
    }
}
