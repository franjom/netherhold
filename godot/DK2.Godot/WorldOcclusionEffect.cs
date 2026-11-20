// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
using System.Runtime.InteropServices;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Opaque world AO followed by a normal and depth aware spatial filter, before transparent effects.</summary>
public partial class WorldOcclusionEffect : CompositorEffect
{
    private RenderingDevice? _rd;
    private Rid _rawShader, _filterShader, _rawPipeline, _filterPipeline, _sampler;
    private Rid _texture, _rawSet, _filterSet, _depth, _color;
    private Vector2I _size;
    private readonly byte[] _push = new byte[80];
    private readonly string _rawSource, _filterSource;
    private volatile bool _released;
    private long _frames, _bytes, _measured, _bufferBytes, _sceneBytes;
    internal bool Denoise { get; set; } = true;
    public WorldOcclusionEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostOpaque;
        AccessResolvedColor = true;
        AccessResolvedDepth = true;
        _rawSource = Godot.FileAccess.GetFileAsString("res://Shaders/WorldOcclusionRaw.glsl").Replace("#[compute]", "");
        _filterSource = Godot.FileAccess.GetFileAsString("res://Shaders/WorldOcclusionFilter.glsl").Replace("#[compute]", "");
    }
    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Render(renderData);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (++_frames > 20) { _bytes += allocated; _measured++; }
    }
    private void Render(RenderData renderData)
    {
        if (_released) return;
        long getterBefore = GC.GetAllocatedBytesForCurrentThread();
        var buffers = renderData.GetRenderSceneBuffers() as RenderSceneBuffersRD;
        if (_frames >= 20) _bufferBytes += GC.GetAllocatedBytesForCurrentThread() - getterBefore;
        if (buffers == null) return;
        Vector2I size = buffers.GetInternalSize();
        if (size.X == 0 || size.Y == 0) return;
        _rd ??= RenderingServer.GetRenderingDevice();
        if (!_rawPipeline.IsValid) Initialize();
        if (!_rawPipeline.IsValid || !_filterPipeline.IsValid) return;
        // Each camera has its own effect. Rebind only if the renderer reallocates its buffers.
        Rid depth = buffers.GetDepthLayer(0), color = buffers.GetColorLayer(0);
        if (_size != size || _depth != depth || _color != color) Bind(size, depth, color);
        getterBefore = GC.GetAllocatedBytesForCurrentThread();
        // This includes depth/Y correction and jitter, matching the spatial shader inverse projection.
        Projection projection = renderData.GetRenderSceneData().GetViewProjection(0);
        if (_frames >= 20) _sceneBytes += GC.GetAllocatedBytesForCurrentThread() - getterBefore;
        Projection inverse = projection.Inverse();
        Span<float> values = MemoryMarshal.Cast<byte, float>(_push.AsSpan());
        for (int column = 0; column < 4; column++)
            for (int row = 0; row < 4; row++) values[column * 4 + row] = inverse[column][row];
        values[16] = size.X; values[17] = size.Y;
        values[18] = projection.X.X; values[19] = projection.Y.Y;
        long list = _rd!.ComputeListBegin();
        Dispatch(list, _rawPipeline, _rawSet, size);
        _rd.ComputeListAddBarrier(list);
        Dispatch(list, _filterPipeline, _filterSet, size);
        _rd.ComputeListEnd();
    }
    private void Dispatch(long list, Rid pipeline, Rid set, Vector2I size)
    {
        _rd!.ComputeListBindComputePipeline(list, pipeline);
        _rd.ComputeListBindUniformSet(list, set, 0);
        _rd.ComputeListSetPushConstant(list, _push, 80);
        _rd.ComputeListDispatch(list, (uint)((size.X + 7) / 8), (uint)((size.Y + 7) / 8), 1);
    }
    private Rid Compile(string code)
    {
        using var source = new RDShaderSource { SourceCompute = code };
        using RDShaderSpirV spirv = _rd!.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorCompute.Length != 0) { GD.PushError(spirv.CompileErrorCompute); return default; }
        return _rd.ShaderCreateFromSpirV(spirv);
    }
    private void Initialize()
    {
        _rawShader = Compile(_rawSource);
        _filterShader = Compile(Denoise ? _filterSource : _filterSource.Replace("#version 450", "#version 450\n#define AO_UNFILTERED"));
        if (!_rawShader.IsValid || !_filterShader.IsValid)
        {
            Enabled = false; _released = true; FreeResources();
            return;
        }
        _rawPipeline = _rd!.ComputePipelineCreate(_rawShader);
        _filterPipeline = _rd.ComputePipelineCreate(_filterShader);
        using var state = new RDSamplerState { MagFilter = RenderingDevice.SamplerFilter.Nearest,
            MinFilter = RenderingDevice.SamplerFilter.Nearest, RepeatU = RenderingDevice.SamplerRepeatMode.ClampToEdge,
            RepeatV = RenderingDevice.SamplerRepeatMode.ClampToEdge };
        _sampler = _rd.SamplerCreate(state);
    }
    private RDUniform Uniform(int binding, Rid texture, bool sampled)
    {
        var uniform = new RDUniform { Binding = binding, UniformType = sampled
            ? RenderingDevice.UniformType.SamplerWithTexture : RenderingDevice.UniformType.Image };
        if (sampled) uniform.AddId(_sampler);
        uniform.AddId(texture);
        return uniform;
    }
    private void Bind(Vector2I size, Rid depth, Rid color)
    {
        FreeSet(ref _rawSet); FreeSet(ref _filterSet); Free(ref _texture);
        _size = size; _depth = depth; _color = color;
        using var format = new RDTextureFormat { Width = (uint)size.X, Height = (uint)size.Y,
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit };
        using var view = new RDTextureView();
        _texture = _rd!.TextureCreate(format, view);
        using RDUniform rawDepth = Uniform(0, depth, true), rawOutput = Uniform(1, _texture, false);
        var raw = new Godot.Collections.Array<RDUniform> { rawDepth, rawOutput };
        using var rawStorage = (Godot.Collections.Array)raw;
        _rawSet = _rd.UniformSetCreate(raw, _rawShader, 0);
        using RDUniform filterDepth = Uniform(0, depth, true), filterInput = Uniform(1, _texture, true), filterOutput = Uniform(2, color, false);
        var filter = new Godot.Collections.Array<RDUniform> { filterDepth, filterInput, filterOutput };
        using var filterStorage = (Godot.Collections.Array)filter;
        _filterSet = _rd.UniformSetCreate(filter, _filterShader, 0);
    }
    /// <summary>Called when the owning camera leaves the tree; all RD resources are freed on the render thread.</summary>
    public void Release()
    {
        if (_released) return;
        _released = true;
        GD.Print("World occlusion callback: ", _measured, " warmed frames allocated ", _bytes, " B; buffers ", _bufferBytes, "; scene ", _sceneBytes);
        RenderingServer.CallOnRenderThread(Callable.From(FreeResources));
    }
    private void FreeResources()
    {
        if (_rd == null) return;
        FreeSet(ref _rawSet); FreeSet(ref _filterSet); Free(ref _texture);
        Free(ref _rawPipeline); Free(ref _filterPipeline); Free(ref _rawShader); Free(ref _filterShader); Free(ref _sampler);
    }
    private void FreeSet(ref Rid rid)
    {
        if (rid.IsValid && _rd!.UniformSetIsValid(rid)) _rd.FreeRid(rid);
        rid = default;
    }
    private void Free(ref Rid rid)
    {
        if (rid.IsValid) _rd!.FreeRid(rid);
        rid = default;
    }
}
