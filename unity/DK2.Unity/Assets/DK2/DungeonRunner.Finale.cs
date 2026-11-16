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
using DK2.Data.Media;
using DK2.Presentation;
using DK2.Simulation;
using UnityEngine;
using NumericsVector3 = System.Numerics.Vector3;

namespace DK2.Unity
{
    /// <summary>The campaign win's view side (campaign-win-sequence.md §4-§6): the win screen, the
    /// camera following the Reaper and the finale's zoom. Escape to carry on playing is
    /// <see cref="CampaignResultFlow"/>'s.</summary>
    public sealed partial class DungeonRunner
    {
        private WinScreen _winScreen;
        private TabletArt _tabletArt;

        // Editor previews (DungeonRunner.Preview.cs); without UNITY_EDITOR these calls compile away.
        partial void UpdateWinScreenPreview();
        partial void DrawWinScreenPreview();

        /// <summary>Tablet.kmf for the win screen: its mesh and draw list (<see cref="TabletDrawList"/>), and one
        /// material per texture, made on first use.</summary>
        private sealed class TabletArt
        {
            public TabletDrawList Draw;
            public NumericsVector3[] Points => Draw.Mesh.Points;
            public readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
            public Material Sparkle;
            public bool SparkleLoaded;
        }

        private void PrepareWinScreen(GameDatabase database)
        {
            _winScreen = null;
            if (database.Level == null) return;
            string table = Path.Combine(_textFolder, "Speech.str");
            Dk2StringTable speech = File.Exists(table) ? Dk2StringTable.Open(table) : null;
            _tabletArt ??= LoadTabletArt();
            _winScreen = new WinScreen(WinScreen.RewardsOf(database.Level),
                id => speech != null && speech.TryResolve(id, out string text) ? text : null, _tabletArt?.Points);
            _resultFlow.WinScreen = _winScreen;
            _winScreen.SpeechRequested += id => _soundPlayer?.QueueSpeech(SoundCategory.Mentor, new SoundId(id + 1));
            _scriptedView.WinScreenRequested += () =>
            {
                _soundPlayer?.StopSpeech((long)(Time.unscaledTimeAsDouble * 1000));
                _winScreen.Start(Time.unscaledTimeAsDouble * 1000d);
            };
            _scriptedView.ZoomRequested += (zoom, _) => _classicCamera?.SetZoom(new Fp32(zoom));
        }

        /// <summary>Game action 17: the camera keeps the followed creature at its focus.</summary>
        private void FollowScriptedCreature()
        {
            if (_scriptedView.FollowedCreatureId == 0 || _glide.Active) return;
            if (World.CreatureById(_scriptedView.FollowedCreatureId) is not CreatureState creature) return;
            _cameraFocus = ToUnity(CameraFocus.Over(new Fp32(creature.PosX), new Fp32(creature.PosY), _cameraFocus.y));
        }

        /// <summary>The win screen's tablet (0x0042F040) and, with speech disabled, its
        /// Speech.str text near the bottom (DK2 shows no text).</summary>
        private void DrawWinScreen()
        {
            if (_winScreen == null || !_winScreen.Started || _winScreen.Current is not RewardItem item) return;
            if (_tabletArt != null && Event.current.type == EventType.Repaint)
                DrawTablet(_tabletArt, _winScreen.Tablet, item.Id, Screen.width * RewardTablet.CentreX, Screen.height * RewardTablet.CentreY);
            if ((_soundPlayer == null || !_soundPlayer.SpeechEnabled) && _winScreen.Text is string text)
                DrawSubtitleText(SubtitleLayout.WinScreenBox(Screen.width, Screen.height), text);
        }

        private TabletArt LoadTabletArt()
        {
            if (_installArt == null || !_installArt.TryResolveEntry("Tablet", out ResolvedAsset asset)
                || !_installArt.TryLoadModel(asset, out KmfModel model)) return null;
            return new TabletArt { Draw = new TabletDrawList(new TabletMesh(model)) };
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

        /// <summary>Blits the tablet's draw list (<see cref="TabletDrawList"/>): each batch with its texture, then the
        /// sparkles alpha-blended (the sprite's blend is unread).</summary>
        private void DrawTablet(TabletArt art, RewardTablet tablet, byte itemId, float originX, float originY)
        {
            TabletDrawList draw = art.Draw;
            draw.Build(tablet, itemId, originX, originY, Screen.height);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
            System.ReadOnlySpan<TabletVertex> vertices = draw.Vertices;
            foreach (TabletBatch batch in draw.Batches)
            {
                Material material = TabletMaterial(art, batch.Texture);
                if (material == null) continue;
                material.SetPass(0);
                GL.Begin(GL.TRIANGLES);
                GL.Color(Color.white);
                for (int i = batch.First; i < batch.First + batch.Count; i++)
                {
                    TabletVertex vertex = vertices[i];
                    GL.TexCoord2(vertex.U, vertex.V);
                    GL.Vertex3(vertex.X, vertex.Y, 0);
                }
                GL.End();
            }
            if (!art.SparkleLoaded)
            {
                art.Sparkle = TabletMaterial(art, "TabletSparkle");
                art.SparkleLoaded = true;
            }
            System.ReadOnlySpan<TabletSparkleQuad> sparkles = draw.Sparkles;
            if (art.Sparkle != null && sparkles.Length > 0)
            {
                art.Sparkle.SetPass(0);
                GL.Begin(GL.QUADS);
                GL.Color(Color.white);
                foreach (TabletSparkleQuad s in sparkles)
                {
                    GL.TexCoord2(0, 0); GL.Vertex3(s.X - s.Half, s.Y - s.Half, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(s.X + s.Half, s.Y - s.Half, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(s.X + s.Half, s.Y + s.Half, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(s.X - s.Half, s.Y + s.Half, 0);
                }
                GL.End();
            }
            GL.PopMatrix();
        }
    }
}
