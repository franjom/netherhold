// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Godot;
namespace DK2.GodotAdapter;
/// <summary>Owns a camera's AO resources across tree exits and re-entry, including full-map reparenting.</summary>
public partial class WorldOcclusionHost : Node
{
    internal bool Denoise { get; set; } = true;
    private Camera3D? _camera;
    private WorldOcclusionEffect? _effect;
    private Compositor? _compositor;
    public override void _EnterTree()
    {
        _camera = GetParent<Camera3D>();
        _effect = new WorldOcclusionEffect { Denoise = Denoise };
        _compositor = new Compositor { CompositorEffects = new Godot.Collections.Array<CompositorEffect> { _effect } };
        _camera.Compositor = _compositor;
    }
    public override void _ExitTree()
    {
        if (_camera != null && _camera.Compositor == _compositor)
        {
            _camera.Compositor = null;
        }

        _effect?.Release();
        _compositor?.Dispose();
        _effect?.Dispose();
        _effect = null;
        _compositor = null;
        _camera = null;
    }
}
