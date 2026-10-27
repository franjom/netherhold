// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Collections.Generic;
using System.IO;
using DK2.Art.Kmf;
using DK2.Art.Textures;
using DK2.Assets;
using DK2.Core;
using DK2.Data.Model;
using DK2.Data.Text;
using DK2.Presentation;
using DK2.Simulation;
using DK2.Simulation.Commands;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    /// <summary>The campaign win's view side (campaign-win-sequence.md §4-§6): the win screen, the
    /// camera following the Reaper, the finale's zoom, and Escape to carry on playing.</summary>
    public sealed partial class DungeonRunner
    {
        private WinScreen _winScreen;
        private bool _continueRequested;
        private TabletArt _tabletArt;

        /// <summary>Tablet.kmf for the win screen: its geometry points, triangles, and one material per
        /// texture, made on first use.</summary>
        private sealed class TabletArt
        {
            public KmfModel Model;
            public NumericsVector3[] Points;
            /// <summary>The screen winding (sign of the y-down cross product) of the front at rest.</summary>
            public int FrontWinding;
            public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
            public Material Sparkle;
            public bool SparkleLoaded;
        }

        private void PrepareWinScreen(GameDatabase database)
        {
            _winScreen = null;
            _continueRequested = false;
            if (database.Level == null) return;
            string table = Path.Combine(_textFolder, "Speech.str");
            Dk2StringTable speech = File.Exists(table) ? Dk2StringTable.Open(table) : null;
            _tabletArt ??= LoadTabletArt();
            _winScreen = new WinScreen(WinScreen.RewardsOf(database.Level),
                id => speech != null && speech.TryResolve(id, out string text) ? text : null, _tabletArt?.Points);
            _scriptedView.WinScreenRequested += () => _winScreen.Start(Time.unscaledTimeAsDouble * 1000d);
            _scriptedView.ZoomRequested += (zoom, _) => _classicCamera?.SetZoom(new Fp32(zoom));
        }

        /// <summary>Game action 17: the camera keeps the followed creature at its focus.</summary>
        private void FollowScriptedCreature()
        {
            if (_scriptedView.FollowedCreatureId == 0 || _glideMilliseconds > 0) return;
            if (_simulation.World.CreatureById(_scriptedView.FollowedCreatureId) is not CreatureState creature) return;
            _cameraFocus = new Vector3(creature.PosX / (float)Fp32.One, _cameraFocus.y, -creature.PosY / (float)Fp32.One);
        }

        /// <summary>The won campaign result's keys (<c>0x004054F0</c>) and the win screen's update
        /// (<c>0x0042C390</c>). Space leaves unless a continue is pending; Escape continues at once when
        /// the screen is done, else reveals the map and waits for it; on a secret level Escape does
        /// nothing. Returns true when the frame's keys were taken.</summary>
        private bool UpdateWinKeys()
        {
            var player = new PlayerId((byte)viewingPlayerId);
            double now = Time.unscaledTimeAsDouble * 1000d;
            bool secret = _campaignProfile.SecretLevelNumber != 0;
            if (_winScreen != null && _winScreen.Update(now, _scriptedView.PathPlaying, _continueRequested))
            {
                Continue(player);
                return true;
            }
            if (secret || _continueRequested || !Input.GetKeyDown(KeyCode.Escape)) return false;
            _pendingCommands.Add(new RevealMapCommand(player));
            if (_winScreen != null && _winScreen.Started && _winScreen.Done) Continue(player);
            else _continueRequested = true;
            return true;
        }

        private void Continue(PlayerId player)
        {
            _pendingCommands.Add(new ContinueAfterResultCommand(player));
            _winScreen?.Close();
            _continueRequested = false;
        }

        /// <summary>The win screen's tablet (0x0042F040) and, standing in for the spoken line until F8, its
        /// Speech.str text near the bottom (DK2 shows no text).</summary>
        private void DrawWinScreen()
        {
            if (_winScreen == null || !_winScreen.Started || _winScreen.Current is not RewardItem item) return;
            if (_tabletArt != null && Event.current.type == EventType.Repaint) DrawTablet(_tabletArt, _winScreen.Tablet, item.Id);
            if (_winScreen.Text is string text)
            {
                var style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Screen.height / 32, wordWrap = true };
                GUI.Label(new Rect(Screen.width * 0.1f, Screen.height * 0.72f, Screen.width * 0.8f, Screen.height * 0.08f), text, style);
            }
        }

        private TabletArt LoadTabletArt()
        {
            if (_installArt == null || !_installArt.TryResolveEntry("Tablet", out ResolvedAsset asset)
                || !_installArt.TryLoadModel(asset, out KmfModel model)) return null;
            // The sparkles' points are the mesh's control points (0x00577DC0 reads the CTRL table):
            // Tablet.kmf's sixteen ring its edge.
            var points = new List<NumericsVector3>(model.ControlPoints.Count);
            foreach (KmfControlPoint control in model.ControlPoints)
                if (control.Vertex < model.Geometry.Count) points.Add(model.Geometry[(int)control.Vertex]);
            var art = new TabletArt { Model = model, Points = points.ToArray() };
            // The front is the material with the item frames; at rest it faces the viewer.
            foreach (KmfPrimitive primitive in model.Mesh.Primitives)
            {
                IReadOnlyList<byte> indices = primitive.IndicesByDetailLevel[0];
                if (model.Materials[(int)primitive.MaterialIndex].TextureNames.Count < 2 || indices.Count < 3) continue;
                NumericsVector3 a = primitive.Vertices[indices[0]].Position, b = primitive.Vertices[indices[1]].Position,
                    c = primitive.Vertices[indices[2]].Position;
                art.FrontWinding = System.Math.Sign((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
                break;
            }
            return art;
        }

        private Material TabletMaterial(TabletArt art, string texture)
        {
            if (art.Materials.TryGetValue(texture, out Material material)) return material;
            try
            {
                RgbaImage image = _installArt.LoadTexture(texture);
                var pixels = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, mipChain: false)
                { name = texture, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
                pixels.SetPixelData(image.Pixels, mipLevel: 0);
                pixels.Apply(updateMipmaps: false);
                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
                material = new Material(shader) { name = "Tablet_" + texture, mainTexture = pixels };
            }
            catch (FileNotFoundException)
            {
                Debug.LogWarning($"DK2: engine texture '{texture}' not found; the win screen leaves it out.");
                material = null;
            }
            art.Materials[texture] = material;
            return material;
        }

        /// <summary>The tablet in screen pixels (0x0042F040): turned and scaled about (½ width, 5/12
        /// height), DK2's pixels taken as 768-high reference units; faces wound away from the viewer are
        /// left out. Then its sparkles, each a TabletSparkle square as wide as its frames left (LIKELY),
        /// alpha-blended (the sprite's blend is unread).</summary>
        private void DrawTablet(TabletArt art, RewardTablet tablet, byte itemId)
        {
            float unit = Screen.height / 768f;
            float cx = Screen.width * RewardTablet.CentreX, cy = Screen.height * RewardTablet.CentreY;
            System.Numerics.Matrix4x4 turn = tablet.Turn();
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            foreach (KmfPrimitive primitive in art.Model.Mesh.Primitives)
            {
                IReadOnlyList<string> textures = art.Model.Materials[(int)primitive.MaterialIndex].TextureNames;
                if (textures.Count == 0) continue;
                Material material = TabletMaterial(art, textures[RewardTablet.TextureFrame(textures.Count, itemId)]);
                if (material == null) continue;
                material.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                GL.Color(Color.white);
                IReadOnlyList<byte> indices = primitive.IndicesByDetailLevel[0];
                for (int i = 0; i + 2 < indices.Count; i += 3)
                {
                    KmfVertex a = primitive.Vertices[indices[i]], b = primitive.Vertices[indices[i + 1]], c = primitive.Vertices[indices[i + 2]];
                    NumericsVector3 pa = tablet.Project(a.Position, turn), pb = tablet.Project(b.Position, turn), pc = tablet.Project(c.Position, turn);
                    if (System.Math.Sign((pb.X - pa.X) * (pc.Y - pa.Y) - (pb.Y - pa.Y) * (pc.X - pa.X)) != art.FrontWinding) continue;
                    TabletVertex(a, pa, cx, cy, unit);
                    TabletVertex(b, pb, cx, cy, unit);
                    TabletVertex(c, pc, cx, cy, unit);
                }
                GL.End();
            }
            if (!art.SparkleLoaded)
            {
                art.Sparkle = TabletMaterial(art, "TabletSparkle");
                art.SparkleLoaded = true;
            }
            System.ReadOnlySpan<RewardTablet.Sparkle> sparkles = tablet.Sparkles;
            if (art.Sparkle != null && sparkles.Length > 0)
            {
                art.Sparkle.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                foreach (RewardTablet.Sparkle s in sparkles)
                {
                    float x = cx + s.X * unit, y = cy + s.Y * unit, half = s.Life * unit / 2;
                    GL.TexCoord2(0, 0); GL.Vertex3(x - half, y - half, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(x + half, y - half, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(x + half, y + half, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(x - half, y + half, 0);
                }
                GL.End();
            }
            GL.PopMatrix();
        }

        private static void TabletVertex(KmfVertex vertex, NumericsVector3 at, float cx, float cy, float unit)
        {
            GL.TexCoord2(vertex.TextureCoordinate.X, vertex.TextureCoordinate.Y);
            GL.Vertex3(cx + at.X * unit, cy + at.Y * unit, 0);
        }
    }
}
