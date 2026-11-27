// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Art;
using DK2.Art.Kmf;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Commands;
using DK2.TestSupport;
namespace DK2.Presentation.Tests;
public sealed class LightningStagingTests
{
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);
    [Fact]
    public void LaunchFlagIsExactAndOffByDefault()
    {
        Assert.False(LaunchArguments.StageLightning([], "--"));
        Assert.False(LaunchArguments.StageLightning(["--stage-lightning-extra"], "--"));
        Assert.True(LaunchArguments.StageLightning(["--stage-lightning"], "--"));
        Assert.True(LaunchArguments.StageLightning(["-stage-lightning"], "-"));
    }
    [InstallFact]
    public void OrdinarySpecialsAndFightProduceCreatureLightningAndElectrification()
    {
        GameSimulation simulation = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1").CreateSimulation(1, true);
        simulation.Tick(InputFrame.Empty);
        var commands = new List<PlayerCommand>();
        LightningStaging stage = Assert.IsType<LightningStaging>(LightningStaging.Prepare(simulation, Viewer, commands));
        Assert.Equal(new TilePos(14, 29), stage.Focus);
        Assert.Equal(new TilePos(14, 31), stage.TargetTile);
        Assert.Null(simulation.Database.RoomByTerrain(simulation.World.Tile(stage.TargetTile).TerrainId));
        CreatureState caster = simulation.World.CreatureById(stage.Caster.Value)!;
        Assert.Equal(1, caster.Level);
        Assert.Equal(4, commands.Count);
        Assert.All(commands, command => Assert.IsType<ActivateSpecialCommand>(command));
        Assert.Equal(new byte[] { 117, 117, 117, 122 }, commands.Cast<ActivateSpecialCommand>()
            .Select(command => simulation.World.ObjectById(command.ObjectId)!.ObjectTypeId));
        var events = new LightningEvents();
        simulation.ObserveWith(events);
        bool bolt = false;
        for (int i = 0; i < 40; i++)
        {
            simulation.Tick(i == 0 ? new InputFrame(commands.ToArray()) : InputFrame.Empty);
            bolt |= simulation.World.Shots.Any(shot => shot.CasterId == stage.Caster.Value
                && simulation.Database.Shots[shot.ShotTypeId].MeshResource.Type == 7);
        }
        Assert.Equal(4, caster.Level);
        Assert.True(bolt);
        Assert.Contains(stage.Target, events.Electrified);
    }
    [InstallFact]
    public void StagedTargetsInstalledStunnedArtSuppliesCracklePairs()
    {
        GameSimulation simulation = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1").CreateSimulation(1, true);
        simulation.Tick(InputFrame.Empty);
        LightningStaging stage = Assert.IsType<LightningStaging>(LightningStaging.Prepare(simulation, Viewer, []));
        CreatureState target = simulation.World.CreatureById(stage.Target.Value)!;
        CreatureDef definition = simulation.Database.CreatureById(target.CreatureTypeId);
        using var art = InstallArtProvider.Open(Dk2InstallGate.Path, simulation.Database);
        Assert.True(art.TryResolve(AssetRef.ForCreature(definition.Name, AssetKind.Animation, "stunned"), out ResolvedAsset asset));
        Assert.True(art.TryLoadModel(asset, out KmfModel? model));
        var mesh = RenderMesh.FromModel(model);
        LightningControlPoint[] controls = [.. mesh.TaggedPoints.Select(point =>
            LightningControlPoint.FromRenderWorld(point.Id, mesh.Positions[point.VertexIndex]))];
        Assert.Equal(17, controls.Count(point => point.Id < 2000));
        Assert.Contains(controls, point => point.Id == 1006);
        Assert.Contains(controls, point => point.Id == 1008);
        Assert.NotEmpty(new LightningCracklePairs(new Dk2Lcg(1)).Choose(controls));
    }
    [InstallFact]
    public void ThunderboltWaitsForTheCreatureCastThenUsesTheOrdinaryPaidCommand()
    {
        GameSimulation simulation = CampaignLevelData.Load(Dk2InstallGate.Path, "Level1").CreateSimulation(1, true);
        simulation.Tick(InputFrame.Empty);
        var commands = new List<PlayerCommand>();
        LightningStaging stage = Assert.IsType<LightningStaging>(LightningStaging.Prepare(simulation, Viewer, commands));
        bool cast = false;
        for (int i = 0; i < 80 && !cast; i++)
        {
            bool posted = stage.QueueThunderboltWhenReady(simulation, commands);
            if (posted)
            {
                Assert.True(simulation.World.CurrentTick.Value >= 31);
                Assert.Contains(commands, command => command is CastSpellOnCreatureCommand { SpellId: KeeperSpellRules.Thunderbolt });
                CastSpellOnCreatureCommand command = Assert.Single(commands.OfType<CastSpellOnCreatureCommand>());
                Assert.Equal(stage.TargetTile, simulation.World.CreatureById(command.CreatureId)!.Tile);
                Assert.NotEqual(stage.Focus, stage.TargetTile);
                Assert.True(simulation.World.Player(Viewer).Mana >= simulation.Database.KeeperSpellById(KeeperSpellRules.Thunderbolt)!.ManaCost);
            }
            simulation.Tick(commands.Count == 0 ? InputFrame.Empty : new InputFrame(commands.ToArray()));
            commands.Clear();
            cast = posted && simulation.World.Shots.Any(shot => shot.ShotTypeId == 21 && shot.CasterId == 0);
        }
        Assert.True(cast);
        Assert.False(stage.QueueThunderboltWhenReady(simulation, commands));
    }
    private sealed class LightningEvents : ISimulationEventSink
    {
        public HashSet<CreatureId> Electrified { get; } = [];
        public void Emit(in SimEvent simEvent)
        {
            if (simEvent.ElectrifiedCreature is CreatureId creature)
            {
                Electrified.Add(creature);
            }
        }
    }
}
