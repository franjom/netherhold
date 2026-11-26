// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using Godot;
namespace DK2.GodotAdapter;
public partial class WorldScene
{
    /// <summary>Modern ambient occlusion applied to opaque world surfaces before transparent effects.</summary>
    public bool PostOcclusion { get; set; }
    private void OpenPostEffects() => AttachOcclusion(_camera!);
    /// <summary>Each world camera owns its AO buffers; shadow cameras do not call this.</summary>
    public void AttachOcclusion(Camera3D camera)
    {
        if (!PostOcclusion)
        {
            return;
        }

        camera.AddChild(new WorldOcclusionHost { Name = "WorldOcclusion" });
    }
}
