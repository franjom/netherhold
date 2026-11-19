// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Uploads the possession-only world draw rules; their inputs and gates belong to the session.</summary>
public partial class WorldScene
{
    private static readonly StringName PossessionFadeEnabledGlobal = "dk2_possession_fade_enabled",
        PossessionViewDistanceGlobal = "dk2_possession_view_distance";
    private bool _possessionFadeEnabled;
    private float _possessionFadeDistance = -1f;
    private void OpenPossessionRendering()
    {
        Global(PossessionFadeEnabledGlobal, RenderingServer.GlobalShaderParameterType.Bool, false);
        Global(PossessionViewDistanceGlobal, RenderingServer.GlobalShaderParameterType.Float, 0f);
    }
    private void UpdatePossessionRendering(in ClassicCameraPose placed)
    {
        bool enabled = Possession is { SettledPossessionCamera: true };
        float distance = enabled ? Possession!.PossessionViewDistance : 0f;
        if (enabled != _possessionFadeEnabled || _fadeFar != placed.FarClipDistance)
        {
            _fadeFar = placed.FarClipDistance;
            // The ordinary spherical fill fade yields to PossessionFade's projection-scaled diamond.
            RenderingServer.GlobalShaderParameterSet(FadeGlobal, enabled ? Vector3.One
                : new Vector3(FadeStart, FadeEnd, Math.Max(_fadeFar, 1e-3f)));
        }
        if (enabled != _possessionFadeEnabled)
        {
            _possessionFadeEnabled = enabled;
            RenderingServer.GlobalShaderParameterSet(PossessionFadeEnabledGlobal, enabled);
        }
        if (distance != _possessionFadeDistance)
        {
            _possessionFadeDistance = distance;
            RenderingServer.GlobalShaderParameterSet(PossessionViewDistanceGlobal, distance);
        }
    }
}
