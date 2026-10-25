// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Presentation;
using DK2.Simulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace DK2.Unity.Editor
{
    public static class SpellSlotStateProbe
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !File.Exists(".tag-overlay-probe"))
                throw new InvalidOperationException("Requires the isolated probe project.");
            try
            {
                const string install = "E:/GogGames/Dungeon Keeper 2";
                var db = GameDatabaseLoader.LoadForCampaignLevel(install, "level2");
                var world = WorldState.FromMap(db, GameDatabaseLoader.LoadCampaignMap(install, "level2"), new byte[] { 0, 1, 2, 3 });
                new GameSimulation(db, world, 1).Run(1000);
                using (var assets = new InstallClassicBottomPanelAssets(install, db))
                {
                    var view = new BottomPanelView(db, new PlayerId(3), assets.Name, assets.Order);
                    view.Capture(world);
                    using (var panel = new ClassicBottomPanel(null, assets, view))
                    {
                        panel.SelectCategory(BottomPanelCategory.Spells);
                        var icons = Field<Image[]>(panel, "_icons");
                        foreach (int state in new[] { 2, 4, 3 })
                        foreach (int mana in new[] { 0, 3000, 6000 })
                        {
                            world.Player(new PlayerId(3)).Mana = mana;
                            world.SetAvailability(3, AvailabilityKind.Spell, 4, state);
                            world.SetAvailability(3, AvailabilityKind.Spell, 6, state);
                            view.Capture(world);
                            panel.FlashTarget = null;
                            panel.Draw(new Vector2Int(1024, 768));
                            var slots = Field<BottomPanelChoice[]>(panel, "_slots");
                            foreach (byte id in new byte[] { 4, 6 })
                            {
                                int index = Array.FindIndex(slots, c => c != null && c.Kind == BottomPanelChoiceKind.Spell && c.Key.Value == id);
                                if (index < 0) throw new Exception("Missing spell slot " + id);
                                byte expected = state == 3 && mana < (id == 4 ? 1500 : 6000) ? (byte)100 : (byte)255;
                                Color32 tint = icons[index].tintColor;
                                if (tint.r != expected || tint.g != expected || tint.b != expected)
                                    throw new Exception("Spell " + id + " state " + state + " mana " + mana + " tint " + tint + " expected " + expected);
                                if (view.Select(slots[index].Key) != (state == 3)) throw new Exception("Wrong spell selectability");
                            }
                            if (state == 3) continue;
                            panel.FlashTarget = new HudFlashTarget(0x0605);
                            panel.Draw(new Vector2Int(1024, 768));
                            if (Field<Image[]>(panel, "_sparkles").Any(i => i.style.display.value != DisplayStyle.None))
                                throw new Exception("Unknown spell flashes");
                        }
                        world.SetAvailability(3, AvailabilityKind.Spell, 6, 2);
                        db.Variables.Research.Clear();
                        db.Variables.Research.Add(new ResearchEntry(4, 6, 400));
                        world.Player(new PlayerId(3)).ResearchTarget = 1;
                        world.Player(new PlayerId(3)).ResearchPoints = 100;
                        view.Capture(world); panel.Draw(new Vector2Int(1024, 768));
                        var choices = Field<BottomPanelChoice[]>(panel, "_slots");
                        int studied = Array.FindIndex(choices, c => c != null && c.Kind == BottomPanelChoiceKind.Spell && c.Key.Value == 6);
                        var sectors = Field<Array>(panel, "_research");
                        if (((VisualElement)sectors.GetValue(studied)).style.display.value != DisplayStyle.Flex)
                            throw new Exception("Unknown spell lost its research overlay");
                    }
                }
                Debug.Log("Spell slot state verification passed: 18 tint/selection cases, unknown flash suppression and research overlay.");
                EditorApplication.Exit(0);
            }
            catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
        }

        private static T Field<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);
    }
}
