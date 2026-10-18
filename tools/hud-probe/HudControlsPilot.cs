// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;

namespace DK2.Unity.HudProbe
{
    public sealed partial class HudToolkitPilot
    {
        private ClassicBottomPanel _controlsBottom;
        private ClassicOptionsPanel _controlsOptions;
        private ClassicFullMap _controlsFullMap;
        private ClassicCreatureQueryPanel _controlsQuery;
        private ClassicCreatureOverview _controlsOverview;
        private DungeonMinimap _controlsMapSource;
        private RenderTexture _bottomTarget, _optionsTarget, _fullMapTarget, _queryTarget, _overviewTarget;
        private readonly CreatureQuerySnapshot _querySample = new CreatureQuerySnapshot(new CreatureQueryTarget(1),
            "Delver the Imp. Blood Group: ARh+. Current Wage: 0. Gold Held: 100.", default);

        private void InitializeControlsPilot()
        {
            string install = Environment.GetEnvironmentVariable("DK2_DIR");
            GameDatabase database = GameDatabaseLoader.LoadForCampaignLevel(install, "Level1");
            var map = GameDatabaseLoader.LoadCampaignMap(install, "Level1");
            WorldState world = WorldState.FromMap(database, map, new[] { PlayerIds.FirstKeeper }, fogOfWarEnabled: true);
            var simulation = new GameSimulation(database, world, 1);
            foreach (string type in new[] { "Imp", "Goblin" })
            {
                CreatureDef definition = database.CreatureByName(type);
                world.SpawnCreature(database, definition.CreatureId, PlayerIds.FirstKeeper, new TilePos(10, 10));
            }
            var assets = new InstallClassicBottomPanelAssets(install, database);
            var model = new BottomPanelView(database, PlayerIds.FirstKeeper, assets.Name, assets.Order);
            model.Capture(world);
            _controlsBottom = new ClassicBottomPanel(null, assets, model);
            _controlsBottom.SelectCategory(BottomPanelCategory.Rooms);
            _controlsBottom.SetVisibility(new HudVisibility(true, true));
            var overviewAssets = new InstallCreatureOverviewAssets(install, database, assets);
            var overview = new CreatureOverview(database, PlayerIds.FirstKeeper, overviewAssets.Name, overviewAssets.Order);
            overview.Capture(world);
            _controlsOverview = new ClassicCreatureOverview(null, overviewAssets, overview);
            _controlsOptions = new ClassicOptionsPanel(null, new InstallClassicOptionsAssets(install));
            _controlsOptions.SetVisible(true);
            _controlsMapSource = new DungeonMinimap(null, new InstallMinimapAssets(install), world.Width, world.Height);
            _controlsMapSource.Capture(world, new WorldView(database, PlayerIds.FirstKeeper), simulation.Navigation);
            _controlsMapSource.Draw(null);
            _controlsMapSource.SetVisibility(new HudVisibility(false, true));
            _controlsFullMap = new ClassicFullMap(null, _controlsMapSource);
            _controlsFullMap.SetVisible(true);
            _controlsQuery = new ClassicCreatureQueryPanel(null, new InstallClassicOptionsAssets(install));
            foreach (HandCursorKind kind in Enum.GetValues(typeof(HandCursorKind)))
                if (kind != HandCursorKind.Default) _handCursor.Update(kind, true, 0);
            _handCursor.Update(HandCursorKind.Dig, true, 0);
        }

        private void ConfigureControlsPilot(int width, int height)
        {
            RenderTexture Target()
            {
                var target = Own(new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)); target.Create(); return target;
            }
            _bottomTarget = Target(); _optionsTarget = Target(); _fullMapTarget = Target(); _queryTarget = Target();
            _controlsBottom.Panel.targetTexture = _bottomTarget;
            _controlsOptions.Panel.targetTexture = _optionsTarget;
            _controlsFullMap.Panel.targetTexture = _fullMapTarget;
            _controlsQuery.Panel.targetTexture = _queryTarget;
            _overviewTarget = Target(); _controlsOverview.Panel.targetTexture = _overviewTarget;
            foreach (var panel in new[] { _controlsBottom.Panel, _controlsOptions.Panel, _controlsFullMap.Panel, _controlsQuery.Panel, _controlsOverview.Panel })
            { panel.clearColor = true; panel.colorClearValue = Color.clear; }
        }

        private void DrawControlsPilot()
        {
            var viewport = new Vector2Int(_bottomTarget.width, _bottomTarget.height);
            _controlsBottom.Draw(viewport);
            _controlsOptions.Draw(new Vector3(10, viewport.y - 10, 0), viewport);
            _controlsFullMap.Draw(viewport);
            _controlsQuery.Draw(_querySample, new HudVisibility(true, true), false, viewport);
            _controlsOverview.Draw(new HudVisibility(true, true), true, Vector3.zero, viewport);
        }

        private void AssertControlsPilot()
        {
            void Check(RenderTexture target, string name, int minimum)
            {
                Texture2D image = Read(target);
                try
                {
                    int visible = image.GetPixels32().Count(p => p.a > 128 && (p.r > 25 || p.g > 25 || p.b > 25));
                    if (visible < minimum) throw new InvalidOperationException(name + " production HUD did not render: " + visible);
                    string file = name + "-" + target.width + "x" + target.height + ".png";
                    File.WriteAllBytes(Path.Combine(_output, file), image.EncodeToPNG()); _captures.Add(file);
                }
                finally { Destroy(image); }
            }
            Check(_bottomTarget, "bottom-panel", 1000);
            Check(_optionsTarget, "options-panel", 100);
            Check(_fullMapTarget, "full-map", 1000);
            Check(_queryTarget, "creature-query", 100);
            Check(_overviewTarget, "creature-overview", 100);
            for(int mode=0;mode<4;mode++)
            {
                float scale=_overviewTarget.height/1920f;
                _controlsOverview.TryPress(new Vector3(710*scale,_overviewTarget.height-(1630+64*mode)*scale,0),true,false,false,false,out _);
                if(_controlsOverview.Mode!=(CreatureOverviewMode)mode) throw new InvalidOperationException("Creature overview mode input failed.");
            }
        }

        private void DisposeControlsPilot()
        {
            _controlsBottom?.Dispose(); _controlsOptions?.Dispose(); _controlsFullMap?.Dispose();
            _controlsOverview?.Dispose(); _controlsQuery?.Dispose(); _controlsMapSource?.Dispose();
        }
    }
}
