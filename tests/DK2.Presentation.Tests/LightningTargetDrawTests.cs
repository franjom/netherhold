// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.Simulation.Combat;
namespace DK2.Presentation.Tests;
public sealed class LightningTargetDrawTests
{
    [Fact]
    public void CameraRefreshKeepsCrackleAndDrawsLaterShotsWithTheSameRandomStream()
    {
        const uint seed = 42;
        var f = new Fixture();
        FpPosition3 target = f.Target(FightTargetKind.Creature);
        LightningControlPoint[] controls = [.. Enumerable.Range(0, 25).Select(i =>
            new LightningControlPoint((uint)(i * 3), new Vector3(1 + i * .08f, 2 + i % 3 * .04f, .8f + i * .025f)))];
        Matrix4x4 cameraA = Matrix4x4.CreateRotationX(.4f) * Matrix4x4.CreateTranslation(2, 3, -4);
        Matrix4x4 cameraB = Matrix4x4.CreateRotationY(.8f) * Matrix4x4.CreateTranslation(-3, 5, -2);
        Assert.True(Matrix4x4.Invert(cameraA, out Matrix4x4 viewA));
        Assert.True(Matrix4x4.Invert(cameraB, out Matrix4x4 viewB));
        var originalBolts = new LightningBolts(seed);
        var originalPairs = new LightningCracklePairs(new Dk2Lcg(seed));
        var expectedVertices = new List<Vector3>();
        var expectedUvs = new List<Vector2>();
        var expectedIndices = new List<int>();
        void Append(Vector3 start, Vector3 end, LightningAmplitude amplitude, Matrix4x4 view, Matrix4x4 camera)
        {
            Vector3 Render(Vector3 p)
            {
                return new Vector3(p.X, p.Z, -p.Y);
            }

            foreach (LightningPath path in originalBolts.Build(Transform(Render(start), view),
                Transform(Render(end), view), amplitude))
            {
                var ribbon = LightningRibbon.Build(path.Nodes);
                int offset = expectedVertices.Count;
                foreach (Vector3 vertex in ribbon.Vertices)
                {
                    expectedVertices.Add(Transform(vertex, camera));
                }

                expectedUvs.AddRange(ribbon.Uvs);
                foreach (int index in ribbon.Triangles)
                {
                    expectedIndices.Add(offset + index);
                }
            }
        }
        foreach (LightningSegment pair in originalPairs.Choose(controls))
        {
            Append(pair.Start, pair.End, LightningAmplitude.Creature, viewA, cameraA);
        }

        Assert.NotEmpty(expectedVertices);
        LightningDrawList draws = f.Draws(seed);
        draws.BeginFrame(viewA, cameraA);
        draws.DrawCrackle(controls);
        Vector3[] prefix = [.. draws.Vertices];
        Vector2[] prefixUvs = [.. draws.Uvs];
        int[] prefixIndices = [.. draws.Triangles];
        draws.UpdateCameraMatrices(viewB, cameraB);
        Assert.Equal(prefix, draws.Vertices);
        Assert.Equal(prefixUvs, draws.Uvs);
        Assert.Equal(prefixIndices, draws.Triangles);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Vector3 Tiles(FpPosition3 p)
        {
            return new Vector3(p.X.Raw / 4096f, p.Y.Raw / 4096f, p.Z.Raw / 4096f);
        }

        Append(Tiles(f.CasterStart), Tiles(target), LightningAmplitude.Shot, viewB, cameraB);
        Assert.True(draws.Vertices.Count > prefix.Length);
        Assert.Equal(expectedVertices.Count, draws.Vertices.Count);
        for (int i = 0; i < expectedVertices.Count; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(expectedVertices[i].X), BitConverter.SingleToInt32Bits(draws.Vertices[i].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(expectedVertices[i].Y), BitConverter.SingleToInt32Bits(draws.Vertices[i].Y));
            Assert.Equal(BitConverter.SingleToInt32Bits(expectedVertices[i].Z), BitConverter.SingleToInt32Bits(draws.Vertices[i].Z));
        }
        Assert.Equal(expectedUvs, draws.Uvs);
        Assert.Equal(expectedIndices, draws.Triangles);
    }

    private static Vector3 Transform(Vector3 p, Matrix4x4 m)
    {
        return new Vector3(
        m.M11 * p.X + m.M21 * p.Y + m.M31 * p.Z + m.M41,
        m.M12 * p.X + m.M22 * p.Y + m.M32 * p.Z + m.M42,
        m.M13 * p.X + m.M23 * p.Y + m.M33 * p.Z + m.M43);
    }

    [Theory]
    [InlineData(FightTargetKind.Creature)]
    [InlineData(FightTargetKind.Body)]
    [InlineData(FightTargetKind.Door)]
    [InlineData(FightTargetKind.Trap)]
    [InlineData(FightTargetKind.Heart)]
    [InlineData(FightTargetKind.Shot)]
    public void EveryTargetUsesUnitysEndpointAndCasterInterpolation(FightTargetKind kind)
    {
        var f = new Fixture();
        FpPosition3 end = f.Target(kind);
        if (kind == FightTargetKind.Body)
        {
            f.Victim.Dk2StateId = CreatureStateMachine.NoState;
        }

        f.Caster.LastPosX = f.Caster.PosX - 3073;
        f.Caster.LastPosY = f.Caster.PosY + 1537;
        foreach (float fraction in new[] { -.5f, 0, .25f, .75f, 1, 2 })
        {
            for (uint seed = 0; seed < 32; seed++)
        {
                LightningDrawList draws = f.Draws(seed);
            draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
            draws.DrawShots(f.World, f.Database, f.View, fraction);
            float clamped = Math.Clamp(fraction, 0, 1);
            var start = new FpPosition3(
                new Fp32((int)(f.Caster.LastPosX + (f.Caster.PosX - f.Caster.LastPosX) * clamped)),
                new Fp32((int)(f.Caster.LastPosY + (f.Caster.PosY - f.Caster.LastPosY) * clamped)), f.Shot.ViewPosition.Z);
            EqualOriginal(draws, seed, start, end);
        }
        }
    }
    [Theory]
    [InlineData(FightTargetKind.Creature)]
    [InlineData(FightTargetKind.Body)]
    [InlineData(FightTargetKind.Door)]
    [InlineData(FightTargetKind.Trap)]
    [InlineData(FightTargetKind.Heart)]
    [InlineData(FightTargetKind.Shot)]
    [InlineData(FightTargetKind.None)]
    [InlineData(FightTargetKind.Object)]
    public void MissingAndUnsupportedTargetsPermanentlyBreakTheLink(FightTargetKind kind)
    {
        var f = new Fixture();
        LightningDrawList draws = f.Draws(1);
        f.Shot.TargetKind = kind;
        f.Shot.TargetId = -1;
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
        f.Target(FightTargetKind.Creature);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
    }
    [Fact]
    public void ACorpseCannotBeACreatureTargetOrSupplyTheCasterPoint()
    {
        var f = new Fixture();
        FpPosition3 end = f.Target(FightTargetKind.Creature);
        f.Victim.Dk2StateId = CreatureStateMachine.NoState;
        LightningDrawList draws = f.Draws(3);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, .5f);
        Assert.Empty(draws.Vertices);
        f.Victim.Dk2StateId = 1;
        f.Caster.Dk2StateId = CreatureStateMachine.NoState;
        draws = f.Draws(3);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, .5f);
        EqualOriginal(draws, 3, f.Shot.ViewPosition, end);
    }
    [Fact]
    public void ShotInterpolationKeepsTheShotHeightAndTruncatesRawCoordinates()
    {
        var f = new Fixture();
        FpPosition3 end = f.Target(FightTargetKind.Creature);
        f.Shot.CasterId = 0;
        var previous = new FpPosition3(new Fp32(f.Shot.PosX), new Fp32(f.Shot.PosY), new Fp32(6789));
        var system = new LaunchForInterpolation(f.Shot, previous.Z);
        new GameSimulation(f.Database, f.World, 1, [system]).Tick();
        f.Shot.PosX += 2073;
        f.Shot.PosY -= 1347;
        foreach (float fraction in new[] { -.1f, 0, .333f, 1, 1.5f })
        {
            LightningDrawList draws = f.Draws(12);
            draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
            draws.DrawShots(f.World, f.Database, f.View, fraction);
            float clamped = Math.Clamp(fraction, 0, 1);
            var start = new FpPosition3(
                new Fp32((int)(previous.X.Raw + (f.Shot.PosX - previous.X.Raw) * clamped)),
                new Fp32((int)(previous.Y.Raw + (f.Shot.PosY - previous.Y.Raw) * clamped)), previous.Z);
            EqualOriginal(draws, 12, start, end);
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenShotsAndMissingCameraConsumeNoBoltRandomNumbers(bool hidden)
    {
        var f = new Fixture(fog: hidden);
        FpPosition3 end = f.Target(FightTargetKind.Creature);
        LightningDrawList draws = f.Draws(31);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity, hasCamera: hidden);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
        f.World.Tile(f.Shot.PosX >> 12, f.Shot.PosY >> 12).SetExploredBy(PressFixture.Keeper);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        EqualOriginal(draws, 31, f.CasterStart, end);
    }
    [Fact]
    public void RemovedShotLinksAreResetBeforeTheirStorageIsReused()
    {
        var f = new Fixture();
        LightningDrawList draws = f.Draws(71);
        f.Shot.TargetKind = FightTargetKind.None;
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
        f.World.RemoveShot(f.Shot.Id);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
        ShotState replacement = f.World.SpawnShot(1, PressFixture.Keeper, f.Shot.PosX, f.Shot.PosY);
        replacement.Id = f.Shot.Id;
        replacement.TargetKind = FightTargetKind.Creature;
        replacement.TargetId = f.Victim.Id;
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        EqualOriginal(draws, 71, replacement.ViewPosition, f.VictimEnd);
        f.World.RemoveShot(replacement.Id);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
        Assert.Empty(draws.Uvs);
        Assert.Empty(draws.Triangles);
    }
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    public void ProceduralKindsEndAtAbsoluteOneTileAndTripleBuildsThreeBolts(byte kind, int bolts)
    {
        var f = new Fixture();
        var at = new FpPosition3(new Fp32(12345), new Fp32(23456), new Fp32(14567));
        f.Effects.Request(new ViewEffectRequest(new EffectId(kind), PressFixture.Keeper, at));
        LightningDrawList draws = f.Draws(42);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawEffects();
        EqualOriginal(draws, 42, at, at with { Z = new Fp32(Fp32.One) }, bolts);
    }
    [Theory]
    [InlineData(2, 3)]
    [InlineData(3, 1)]
    public void TripleAndFixedShotsRetainTheOriginalBoltCountAndFixedEndpoint(byte kind, int bolts)
    {
        var f = new Fixture();
        FpPosition3 end = f.Target(FightTargetKind.Creature);
        f.Shot.ShotTypeId = kind;
        LightningDrawList draws = f.Draws(21);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        EqualOriginal(draws, 21, kind == 3 ? f.Shot.ViewPosition : f.CasterStart,
            kind == 3 ? f.Shot.ViewPosition with { Z = new Fp32(Fp32.One) } : end, bolts);
    }
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(99)]
    public void NonProceduralMeshesOtherProceduralKindsAndMissingDefinitionsDoNotDraw(byte kind)
    {
        var f = new Fixture();
        f.Target(FightTargetKind.Creature);
        f.Shot.ShotTypeId = kind;
        LightningDrawList draws = f.Draws(11);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
    }
    private sealed class LaunchForInterpolation : ISimulationSystem
    {
        private readonly ShotState _shot;
        private readonly Fp32 _height;

        public LaunchForInterpolation(ShotState shot, Fp32 height)
        {
            _shot = shot;
            _height = height;
        }

        public string Name
        {
            get
            {
                return "set interpolation origin";
            }
        }

        public void Execute(SimulationContext context)
        {
            ShotRules.RequestTrail(context, _shot, default, _height);
        }
    }

    [Fact]
    public void EveryHeartPillarIdMatchesTheExistingResolverIncludingChangedSlabOrder()
    {
        var f = new Fixture();
        RoomInstance room = Assert.Single(RoomIndex.Build(f.World, f.Database));
        for (int order = 0; order < 3; order++)
        {
            for (int i = 0; i < room.TileIndices.Count; i++)
            {
                f.World.TileAtIndex(room.TileIndices[i]).SlabOrder = order switch
                {
                    0 => 0,
                    1 => room.TileIndices.Count - i,
                    _ => i % 4,
                };
            }

            int found = 0;
            for (int id = -1; id <= f.World.TileCount * 3; id++)
            {
                f.Shot.TargetKind = FightTargetKind.Heart;
                f.Shot.TargetId = id;
                f.Shot.TargetPlayerId = PressFixture.Keeper.Value;
                LightningDrawList draws = f.Draws(19);
                draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
                draws.DrawShots(f.World, f.Database, f.View, 1);
                bool valid = HeartPillarRules.TryResolve(f.World, f.Database, PressFixture.Keeper,
                    new HeartPillarId(id), out HeartPillarTarget pillar);
                if (!valid)
                {
                    Assert.Empty(draws.Vertices);
                }
                else
                {
                    found++;
                    var point = new FpPosition3(pillar.X, pillar.Y, default);
                    EqualOriginal(draws, 19, f.CasterStart, point with { Z = f.View.GroundHeightAt(f.World, point) });
                }
            }
            Assert.Equal(3, found);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void StaleHeartPillarsMatchTheExistingResolversRejections(int fault)
    {
        var f = new Fixture();
        f.Target(FightTargetKind.Heart);
        int tile = f.Shot.TargetId / 3;
        switch (fault)
        {
            case 0: f.World.Player(PressFixture.Keeper).HeartRuined = true; break;
            case 1: f.World.Player(PressFixture.Keeper).HeartHealth = 0; break;
            case 2: f.Shot.TargetPlayerId = 7; break;
            case 3: f.World.TileAtIndex(tile).TerrainId = 0; break;
            case 4: f.World.TileAtIndex(tile).OwnerPlayerId = PlayerIds.Neutral; break;
            case 5:
                for (int i = 0; i < f.World.TileCount; i++)
                {
                    if (i != tile)
                    {
                        f.World.TileAtIndex(i).TerrainId = 0;
                    }
                }

                break;
        }
        Assert.False(HeartPillarRules.TryResolve(f.World, f.Database, new PlayerId(f.Shot.TargetPlayerId),
            new HeartPillarId(f.Shot.TargetId), out _));
        LightningDrawList draws = f.Draws(7);
        draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
        draws.DrawShots(f.World, f.Database, f.View, 1);
        Assert.Empty(draws.Vertices);
    }

    [Fact]
    public void AHeartTargetAllocatesNothingPerFrameAfterWarmup()
    {
        var f = new Fixture();
        f.Target(FightTargetKind.Heart);
        LightningDrawList draws = f.Draws(83);
        void Frame()
        {
            draws.BeginFrame(Matrix4x4.Identity, Matrix4x4.Identity);
            draws.DrawShots(f.World, f.Database, f.View, .5f);
        }
        for (int i = 0; i < 4096; i++)
        {
            Frame();
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 256; i++)
        {
            Frame();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.NotEmpty(draws.Vertices);
        Assert.Equal(0, allocated);
    }

    private static void EqualOriginal(LightningDrawList actual, uint seed, FpPosition3 start, FpPosition3 end, int boltCount = 1)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        var bolts = new LightningBolts(seed);
        Vector3 Render(FpPosition3 p)
        {
            return new Vector3(p.X.Raw / 4096f, p.Z.Raw / 4096f, -p.Y.Raw / 4096f);
        }

        for (int b = 0; b < boltCount; b++)
        {
            foreach (LightningPath path in bolts.Build(Render(start), Render(end)))
        {
            var ribbon = LightningRibbon.Build(path.Nodes);
            int offset = vertices.Count;
            vertices.AddRange(ribbon.Vertices); uvs.AddRange(ribbon.Uvs);
            foreach (int index in ribbon.Triangles)
                {
                    triangles.Add(offset + index);
                }
            }
        }

        Assert.NotEmpty(vertices);
        Assert.Equal(vertices.Count, actual.Vertices.Count);
        for (int i = 0; i < vertices.Count; i++)
        {
            Assert.Equal(BitConverter.SingleToInt32Bits(vertices[i].X), BitConverter.SingleToInt32Bits(actual.Vertices[i].X));
            Assert.Equal(BitConverter.SingleToInt32Bits(vertices[i].Y), BitConverter.SingleToInt32Bits(actual.Vertices[i].Y));
            Assert.Equal(BitConverter.SingleToInt32Bits(vertices[i].Z), BitConverter.SingleToInt32Bits(actual.Vertices[i].Z));
        }
        Assert.Equal(uvs, actual.Uvs);
        Assert.Equal(triangles, actual.Triangles);
    }
    private sealed class Fixture
    {
        public GameDatabase Database { get; }
        public WorldState World { get; }
        public WorldView View { get; }
        public RoomConstructionParticles Effects { get; }
        public CreatureState Caster { get; }
        public CreatureState Victim { get; }
        public ShotState Shot { get; }
        public FpPosition3 CasterStart
        {
            get
            {
                return new FpPosition3(new Fp32(Caster.PosX), new Fp32(Caster.PosY), Shot.ViewPosition.Z);
            }
        }

        public FpPosition3 VictimEnd
        {
            get
            {
                return new FpPosition3(new Fp32(Victim.PosX), new Fp32(Victim.PosY),
                        new Fp32(View.GroundHeightAt(World, new FpPosition3(new Fp32(Victim.PosX), new Fp32(Victim.PosY), default)).Raw + Victim.PosZ));
            }
        }

        public Fixture(bool fog = false)
        {
            GameDatabase basic = PressFixture.Database();
            TerrainDef floor = basic.Terrain[0];
            var heart = new RoomDef
            {
                Name = "Heart", RoomId = (byte)RoomTypeId.DungeonHeart, TerrainId = 1,
                Flags = RoomFlags.PlaceableOnLand, Construction = TileConstruction.ThreeByThree,
                Cost = 0, ReturnPercentage = 0, CreatedCreatureId = 0, ObjectIds = [], HealthGain = 0,
                CeilingHeight = default, RecommendedSizeX = 5, RecommendedSizeY = 5,
                OrderInEditor = 0, NameStringId = 0, TooltipStringId = 0,
            };
            ProceduralArtKind[] kinds = [ProceduralArtKind.Lightning, ProceduralArtKind.TripleLightning,
                ProceduralArtKind.FixedLightning, ProceduralArtKind.Lightning, ProceduralArtKind.IceRing];
            KwdArtResource Art(int i)
            {
                byte[] data = new byte[12]; data[0] = (byte)kinds[i];
                return new KwdArtResource("bolt", 0, data, (byte)(i == 3 ? 5 : 7), 0, 0, 0);
            }
            var light = new KwdLight(0, 0, 0, 0, 0, 0, 0, 0, 0);
            Database = new GameDatabase([floor, floor with { TerrainId = 1, Flags = TerrainFlags.Room | TerrainFlags.Ownable }],
                [heart], basic.Creatures.Values, basic.Variables,
                doors: [PressFixture.Door(1)], traps: [PressFixture.Trap(1, 0)],
                shots: Enumerable.Range(0, 5).Select(i => new ShotDef([])
                    { Name = "bolt", ShotId = (byte)(i + 1), MeshResource = Art(i), Light = light, SoundCategory = "" }),
                effects: Enumerable.Range(0, 3).Select(i => new EffectDef([])
                    { Name = "bolt", EffectId = (ushort)(i + 1), ArtResource = Art(i), Light = light, GenerateIds = [], MinHp = 20, MaxHp = 20 }));
            MapTile[] tiles = [.. Enumerable.Repeat(new MapTile(0, 0, 0, 0), 81)];
            for (int y = 1; y <= 5; y++)
            {
                for (int x = 1; x <= 5; x++)
                {
                    tiles[y * 9 + x] = new MapTile(1, PressFixture.Keeper.Value, 0, 0);
                }
            }

            World = WorldState.FromMap(Database, new LevelMap(9, 9, tiles),
                [PlayerIds.Good, PlayerIds.Neutral, PressFixture.Keeper.Value], fog);
            View = new WorldView(Database, PressFixture.Keeper);
            Caster = World.SpawnCreature(Database, 1, PressFixture.Keeper, new TilePos(7, 6));
            Victim = World.SpawnCreature(Database, 1, PressFixture.Keeper, new TilePos(6, 6));
            Victim.PosZ = 1536;
            Shot = World.SpawnShot(1, PressFixture.Keeper, 7201, 29781);
            Shot.CasterId = Caster.Id;
            Effects = new RoomConstructionParticles(Database, 1, () => default, floorHeight: _ => default);
            ThingProjection.Place(World, Database,
            [new DoorThing(new byte[20]) { X = 6, Y = 2, DoorId = 1, PlayerId = PressFixture.Keeper.Value, Unknown_0011 = new byte[3] },
             new TrapThing(new byte[16]) { X = 7, Y = 2, TrapId = 1, PlayerId = PressFixture.Keeper.Value, NumberOfShots = 1 }]);
        }
        public LightningDrawList Draws(uint seed)
        {
            return new LightningDrawList(Effects, seed);
        }

        public FpPosition3 Target(FightTargetKind kind)
        {
            Shot.TargetKind = kind;
            switch (kind)
            {
                case FightTargetKind.Creature:
                case FightTargetKind.Body:
                    Shot.TargetId = Victim.Id;
                    return VictimEnd;
                case FightTargetKind.Door:
                    Shot.TargetId = Assert.Single(World.Doors).Id;
                    return Ground(new FpPosition3(new Fp32(CreatureState.CentreOf(6)), new Fp32(CreatureState.CentreOf(2)), default));
                case FightTargetKind.Trap:
                    Shot.TargetId = Assert.Single(World.Traps).Id;
                    return Ground(new FpPosition3(new Fp32(CreatureState.CentreOf(7)), new Fp32(CreatureState.CentreOf(2)), default));
                case FightTargetKind.Heart:
                    HeartPillarTarget pillar = HeartPillarRules.In(World, Assert.Single(RoomIndex.Build(World, Database)))[1];
                    Shot.TargetId = HeartPillarId.For(World, pillar).Value;
                    Shot.TargetPlayerId = PressFixture.Keeper.Value;
                    return Ground(new FpPosition3(pillar.X, pillar.Y, default));
                case FightTargetKind.Shot:
                    ShotState other = World.SpawnShot(4, PressFixture.Keeper, 23123, 26345);
                    Shot.TargetId = other.Id;
                    return other.ViewPosition;
                default: throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }
        private FpPosition3 Ground(FpPosition3 p)
        {
            return p with { Z = View.GroundHeightAt(World, p) };
        }
    }
}
