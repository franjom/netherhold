// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.AI;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;

namespace DK2.Harness;

/// <summary>Builds a scenario's input: scripted commands, injected work and computer keepers.</summary>
internal sealed class ScenarioInputBuilder
{
    private readonly GameDatabase _database;
    private readonly string? _installDir;

    public ScenarioInputBuilder(GameDatabase database, string? installDir)
    {
        _database = database;
        _installDir = installDir;
    }

    public List<KeeperAI> CreateComputerKeepers(
        Scenario scenario, string? campaignLevel, List<string> unsupported)
    {
        var result = new List<KeeperAI>();
        if (scenario.ComputerKeepers.Count == 0) return result;
        if (campaignLevel is null || _installDir is null)
        {
            unsupported.Add("computer_keepers needs a campaign map and the DK2 installation");
            return result;
        }

        List<PlayerDef>? players = GameDatabaseLoader.LoadCampaignPlayers(_installDir, campaignLevel);
        foreach (byte id in scenario.ComputerKeepers.Distinct())
        {
            PlayerDef? player = players?.FirstOrDefault(p => p.PlayerId == id);
            if (player is null || !player.IsComputerKeeper)
            {
                unsupported.Add($"computer_keepers: player {id} has no computer PLAYERS record");
                continue;
            }
            result.Add(new KeeperAI(id, player.Ai, new SimRandom(scenario.Seed ^ id),
                playerActivationOrder: players!.Select(p => new PlayerId(p.PlayerId))));
        }
        return result;
    }

    /// <summary>Turns the scenario's scheduled commands into one input frame per tick.</summary>
    public Dictionary<int, InputFrame> BuildInput(Scenario scenario, List<string> unsupported)
    {
        var byTick = new Dictionary<int, List<PlayerCommand>>();

        foreach (ScenarioCommand c in scenario.Commands)
        {
            if (c.AtTick < 0 || c.AtTick >= scenario.DurationTicks)
            {
                unsupported.Add(
                    $"commands entry at_tick={c.AtTick} falls outside the run (0..{scenario.DurationTicks - 1})");
                continue;
            }

            if (!byTick.TryGetValue(c.AtTick, out List<PlayerCommand>? list))
                byTick[c.AtTick] = list = [];

            if (c.Tag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Tag), true));
            if (c.Untag.Count > 0) list.Add(new TagTilesCommand(c.Player, ToTiles(c.Untag), false));

            bool built = c.Build is not null && TryBuild(c, list, unsupported);
            if (c.Sell.Count > 0) list.Add(new SellRoomCommand(c.Player, ToTiles(c.Sell)));
            if (c.CreateImp) list.Add(new CreateImpCommand(c.Player));

            if (c.PickUp > 0) list.Add(new PickUpCreatureCommand(c.Player, c.PickUp));
            if (c.PickUpObject > 0)
                list.Add(new PickUpObjectCommand(c.Player, c.PickUpObject));
            if (c.Slap > 0) list.Add(new SlapCreatureCommand(c.Player, c.Slap));

            bool dropped = c.Drop.Count > 0;
            if (dropped)
            {
                if (c.Drop.Count != 2)
                {
                    unsupported.Add(
                        $"commands entry at_tick={c.AtTick} has a drop that is not [x, y]");
                    dropped = false;
                }
                else
                {
                    list.Add(new DropCommand(
                        c.Player, new TilePos(c.Drop[0], c.Drop[1]), c.DropOnto));
                }
            }

            bool called = c.CallToArms.Count > 0;
            if (called)
            {
                if (c.CallToArms.Count != 2)
                {
                    unsupported.Add($"commands entry at_tick={c.AtTick} has a call_to_arms that is not [x, y]");
                    called = false;
                }
                else
                {
                    list.Add(new CastSpellAtPlaceCommand(c.Player, CallToArmsRules.SpellId,
                        CreatureState.CentreOf(c.CallToArms[0]), CreatureState.CentreOf(c.CallToArms[1])));
                }
            }
            if (c.RemoveCallToArms) list.Add(new RemoveCallToArmsCommand(c.Player));

            if (c.Tag.Count == 0 && c.Untag.Count == 0 && !built
                && c.Sell.Count == 0 && !c.CreateImp && !called && !c.RemoveCallToArms
                && c.PickUp == 0 && c.PickUpObject == 0 && c.Slap == 0 && !dropped)
                unsupported.Add($"commands entry at_tick={c.AtTick} names nothing this build can do");
        }

        return byTick.ToDictionary(kv => kv.Key, kv => new InputFrame(kv.Value));
    }

    private bool TryBuild(ScenarioCommand c, List<PlayerCommand> into, List<string> unsupported)
    {
        ScenarioBuild build = c.Build!;
        RoomDef? room = _database.RoomByName(build.Type);
        if (room is null)
        {
            unsupported.Add(
                $"commands entry at_tick={c.AtTick} builds '{build.Type}', which is not a room in the database");
            return false;
        }

        List<TilePos> tiles = ToTiles(build.Tiles);
        if (tiles.Count == 0)
        {
            unsupported.Add($"commands entry at_tick={c.AtTick} builds '{build.Type}' on no tiles");
            return false;
        }

        into.Add(new BuildRoomCommand(c.Player, (RoomTypeId)room.RoomId, tiles));
        return true;
    }

    private static List<TilePos> ToTiles(List<List<int>> pairs) =>
        pairs.Where(p => p.Count == 2).Select(p => new TilePos(p[0], p[1])).ToList();

    /// <summary>Validates the scenario's work entries, dropping and reporting bad ones.</summary>
    public static List<ScenarioWork> ResolveWork(
        Scenario scenario, WorldState world, List<string> unsupported)
    {
        var usable = new List<ScenarioWork>();

        foreach (ScenarioWork w in scenario.Work)
        {
            if (!Enum.TryParse(w.Kind, ignoreCase: true, out TileWorkKind _))
            {
                unsupported.Add(
                    $"work kind '{w.Kind}' is not one of {string.Join(", ", Enum.GetNames<TileWorkKind>())}");
                continue;
            }

            if (w.At.Count != 2 || !world.Contains(w.At[0], w.At[1]))
            {
                unsupported.Add($"work entry at [{string.Join(",", w.At)}] is not a tile on this map");
                continue;
            }

            if (w.Repeat < 1)
            {
                unsupported.Add($"work entry at_tick={w.AtTick} has repeat={w.Repeat}");
                continue;
            }

            usable.Add(w);
        }

        return usable;
    }

    public static void InjectWork(WorldState world, List<ScenarioWork> work, int tick)
    {
        foreach (ScenarioWork w in work)
        {
            int last = w.UntilTick < w.AtTick ? w.AtTick : w.UntilTick;
            if (tick < w.AtTick || tick > last) continue;

            var kind = Enum.Parse<TileWorkKind>(w.Kind, ignoreCase: true);
            for (int i = 0; i < w.Repeat; i++)
                world.QueueWork(w.At[0], w.At[1], kind, w.Player);
        }
    }
}
