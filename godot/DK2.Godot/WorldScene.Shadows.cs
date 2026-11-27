// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Presentation;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace DK2.GodotAdapter;

/// <summary>Real torch shadows on DK2's per-pixel light (<c>--light-shadows</c> with <c>--light-perpixel</c>), a modern
/// addition DK2 does not have: the <see cref="ClassicTorchShadowSlots.MaximumTorches"/> torches nearest the focus each get
/// a distance map looking down from the torch (a <see cref="SubViewport"/> whose wide perspective camera only sees the
/// casters' layers; the world shaders write distances there). A wall's shadow multiplies only that torch's DK2 term; a
/// creature's or object's darkens the whole per-pixel light, as DK2's receiver quad darkens the frame (DK2_TORCH_SHADOWS
/// in Shaders/World.gdshaderinc and WorldLight.gdshaderinc, the lookup in WorldShadows.gdshaderinc). A map
/// texel holds the nearest caster's distance and its darkening: DK2's strength (<see cref="ClassicMeshShadow.Strength"/>)
/// with a creature's or object's fade from DK2's caster rule (<see cref="ClassicShadowSourceSelector"/>: the three nearest
/// torches within 2.5 tiles, fading from the fourth), and the terrain's over the 2.5-tile range. Off, nothing here exists
/// and every shader is the per-pixel one, byte for byte.</summary>
public partial class WorldScene
{
    /// <summary>The render layers (13 and 14) a shadow view's camera sees, one bit a view; casters carry all of them and
    /// layer 1. A camera without layer 1 is a shadow view (the shaders read <c>CAMERA_VISIBLE_LAYERS</c>). One view a
    /// torch, looking straight down over <see cref="ShadowFieldOfView"/>: a dual paraboloid misplaced the terrain's large
    /// triangles, and the floor and the creatures are below the torch; a receiver outside the view is unshadowed.</summary>
    // ShadowMapSize, ShadowRange and ShadowFieldOfView are written out in Shaders/WorldShadows.gdshaderinc
    // (ClassicVertexLightShaderTextTests holds the two to each other).
    private const int ShadowLayerShift = 12, ShadowViews = ClassicTorchShadowSlots.MaximumTorches, ShadowMapSize = 512;
    private const float ShadowFieldOfView = 150f; // DK2_SHADOW_TAN in WorldShadows.gdshaderinc: tan(75 degrees)
    private const uint ShadowCasterLayers = 1u | (((1u << ShadowViews) - 1u) << ShadowLayerShift);
    /// <summary>A map's reach from its torch in tiles: past DK2's 2.5 horizontally, where every strength is zero, with the
    /// height above; distances are stored over it.</summary>
    private const float ShadowRange = 4f; // DK2_SHADOW_RANGE in WorldShadows.gdshaderinc
    /// <summary>How far from the focus a torch may be and still get a map, in tiles.</summary>
    private const float ShadowReach = 14f;
    /// <summary>How much nearer a torch that has a map counts, in tiles, so the maps do not swap back and forth.</summary>
    private const float ShadowHysteresis = 1.5f;

    private static readonly StringName ShadowFadeParameter = "dk2_shadow_fade";
    private static readonly StringName[] ShadowMapGlobals =
        ["dk2_shadow_map0", "dk2_shadow_map1"];
    private static readonly StringName[] ShadowLightGlobals =
        ["dk2_shadow_light0", "dk2_shadow_light1"];

    private readonly Dictionary<int, NVector3> _shadowTorches = new();
    private int[] _shadowKeys = [];
    private NVector3[] _shadowPositions = [];
    private bool _shadowTorchesChanged;
    private readonly int[] _shadowChosen = new int[ClassicTorchShadowSlots.MaximumTorches];
    private readonly int[] _shadowNext = new int[ClassicTorchShadowSlots.MaximumTorches];
    private readonly int[] _shadowSlotKeys = [-1, -1];
    private readonly NVector3[] _shadowSlotPositions = new NVector3[ClassicTorchShadowSlots.MaximumTorches];
    private readonly ClassicShadowSourceSelector _shadowSelector = new();
    private SubViewport[]? _shadowViewports;
    private Camera3D[]? _shadowCameras;

    /// <summary>Real torch shadows (<c>--light-shadows</c>), drawn only with <see cref="PerPixelLight"/>. Set before
    /// <see cref="Open"/>.</summary>
    public bool TorchShadows { get; set; }

    private bool TorchShadowsActive
    {
        get
        {
            return TorchShadows && PerPixelLight;
        }
    }

    /// <summary>The shadow views and the globals the shaders read; nothing when the shadows are off.</summary>
    private void OpenShadows()
    {
        if (!TorchShadowsActive)
        {
            return;
        }

        _shadowViewports = new SubViewport[ShadowViews];
        _shadowCameras = new Camera3D[ShadowViews];
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Colors.White,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1f,
            TonemapWhite = 1f,
        };
        for (int view = 0; view < ShadowViews; view++)
        {
            var viewport = new SubViewport
            {
                Name = "TorchShadow" + view,
                Size = new Vector2I(ShadowMapSize, ShadowMapSize),
                // Float output, no sRGB encoding: the texels are distances (renderer_scene_render_rd.cpp, convert_to_srgb).
                UseHdr2D = true,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
                PositionalShadowAtlasSize = 0,
            };
            var camera = new Camera3D
            {
                Fov = ShadowFieldOfView,
                Near = 0.01f,
                Far = ShadowRange,
                CullMask = 1u << (ShadowLayerShift + view),
                Environment = environment,
            };
            viewport.AddChild(camera);
            AddChild(viewport);
            camera.Current = true;
            _shadowViewports[view] = viewport;
            _shadowCameras[view] = camera;
            Global(ShadowMapGlobals[view], RenderingServer.GlobalShaderParameterType.Sampler2D, viewport.GetTexture());
        }
        for (int slot = 0; slot < ShadowLightGlobals.Length; slot++)
        {
            _shadowSlotKeys[slot] = -1;
            Global(ShadowLightGlobals[slot], RenderingServer.GlobalShaderParameterType.Vec4, Vector4.Zero);
        }
    }

    /// <summary>A torch's light for the shadow slots (<see cref="SetTorchLight"/>), null when unlit or gone.</summary>
    private void SetShadowTorch(int floorTileIndex, ClassicPointLight? light)
    {
        if (light is ClassicPointLight lit)
        {
            _shadowTorches[floorTileIndex] = lit.Position;
        }
        else if (!_shadowTorches.Remove(floorTileIndex))
        {
            return;
        }

        _shadowTorchesChanged = true;
    }

    /// <summary>The frame's shadow slots and each caster's fades: the nearest torches to the focus get the views (placed
    /// and rendered only while held; a slot's global names its light as the tables hold it), and each creature, object
    /// and structure near one of them takes DK2's fade for that torch when it is among its three. Allocation-free once
    /// the torches are listed.</summary>
    private void PrepareShadows()
    {
        if (_shadowViewports == null || _shadowCameras == null)
        {
            return;
        }

        if (_shadowTorchesChanged)
        {
            _shadowTorchesChanged = false;
            if (_shadowKeys.Length != _shadowTorches.Count)
            {
                _shadowKeys = new int[_shadowTorches.Count];
                _shadowPositions = new NVector3[_shadowTorches.Count];
            }
            int i = 0;
            foreach (KeyValuePair<int, NVector3> torch in _shadowTorches)
            {
                _shadowKeys[i] = torch.Key;
                _shadowPositions[i++] = torch.Value;
            }
        }
        int count = ClassicTorchShadowSlots.Choose(ScriptedView?.PathPlaying == true ? _placed.Position : Focus, _shadowKeys, _shadowPositions, ShadowReach, _shadowSlotKeys,
            ShadowHysteresis, _shadowChosen);
        // A torch still chosen keeps its slot; the slots it frees take the newly chosen ones in order.
        for (int slot = 0; slot < ClassicTorchShadowSlots.MaximumTorches; slot++)
        {
            _shadowNext[slot] = -1;
            for (int i = 0; i < count && _shadowSlotKeys[slot] >= 0; i++)
            {
                if (_shadowChosen[i] >= 0 && _shadowKeys[_shadowChosen[i]] == _shadowSlotKeys[slot]) { _shadowNext[slot] = _shadowChosen[i]; _shadowChosen[i] = -1; }
            }
        }
        for (int i = 0, slot = 0; i < count; i++)
        {
            if (_shadowChosen[i] < 0)
            {
                continue;
            }

            while (_shadowNext[slot] >= 0)
            {
                slot++;
            }

            _shadowNext[slot] = _shadowChosen[i];
        }
        for (int slot = 0; slot < ClassicTorchShadowSlots.MaximumTorches; slot++)
        {
            int index = _shadowNext[slot];
            int key = index >= 0 ? _shadowKeys[index] : -1;
            if (key == _shadowSlotKeys[slot])
            {
                continue;
            }

            _shadowSlotKeys[slot] = key;
            if (key < 0)
            {
                RenderingServer.GlobalShaderParameterSet(ShadowLightGlobals[slot], Vector4.Zero);
                _shadowViewports[slot].RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
                continue;
            }
            NVector3 at = _shadowPositions[index];
            _shadowSlotPositions[slot] = at;
            // Godot's frame, as LightTables writes a source's position: Z negated.
            var point = new Vector3(at.X, at.Y, -at.Z);
            RenderingServer.GlobalShaderParameterSet(ShadowLightGlobals[slot], new Vector4(point.X, point.Y, point.Z, 1f));
            // Looking down (-Y), up along -Z: dk2_shadow_sample's projection.
            _shadowCameras[slot].GlobalTransform = new Transform3D(
                new Basis(new Vector3(1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0)), point);
            _shadowViewports[slot].RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
        }
        foreach (KeyValuePair<int, CreatureSlot> entry in _creatures)
        {
            ShadowFades(entry.Value.Slot);
        }

        foreach (KeyValuePair<int, ModelSlot> entry in _objects)
        {
            ShadowFades(entry.Value);
        }

        foreach (KeyValuePair<StructureId, ModelSlot> entry in _structures)
        {
            ShadowFades(entry.Value);
        }
    }

    /// <summary>A caster's fade per slot: zero unless that slot's torch is one of its DK2 sources; written to its
    /// instance only when it changed.</summary>
    private void ShadowFades(ModelSlot slot)
    {
        NVector3 centre = slot.ShadowCentre;
        const float range = ClassicMeshShadow.MaximumDistance * ClassicMeshShadow.MaximumDistance;
        bool near = false;
        for (int i = 0; i < ClassicTorchShadowSlots.MaximumTorches && !near; i++)
        {
            near = _shadowSlotKeys[i] >= 0 && NVector3.DistanceSquared(centre, _shadowSlotPositions[i]) < range;
        }

        Vector4 fades = default;
        if (near)
        {
            _shadowSelector.Begin(centre);
            for (int i = 0; i < _shadowKeys.Length; i++)
            {
                _shadowSelector.Offer(_shadowKeys[i], _shadowPositions[i]);
            }

            fades = new Vector4(Fade(0), Fade(1), 0f, 0f);
        }
        if (fades == slot.ShadowFades)
        {
            return;
        }

        slot.ShadowFades = fades;
        slot.Node.SetInstanceShaderParameter(ShadowFadeParameter, fades);

        float Fade(int i)
        {
            return _shadowSlotKeys[i] >= 0 ? _shadowSelector.FadeFor(_shadowSlotKeys[i]) : 0f;
        }
    }
}
