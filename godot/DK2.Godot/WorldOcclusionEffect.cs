// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Runtime.InteropServices;
using Godot;
namespace DK2.GodotAdapter;
/// <summary>Opaque world AO followed by a normal and depth aware spatial filter, before transparent effects. Without MSAA
/// the filter darkens the resolved colour in place. Under MSAA Godot resolves the multisampled colour again after the
/// transparent pass, which would drop that write, so the filter writes the factor to its own image and a full-screen
/// draw multiplies it into every sample of the multisampled colour (audit item 17).</summary>
public partial class WorldOcclusionEffect : CompositorEffect
{
    private RenderingDevice? _rd;
    private Rid _rawShader, _filterShader, _rawPipeline, _filterPipeline, _sampler;
    private Rid _texture, _rawSet, _filterSet, _depth, _color;
    // The MSAA path: the factor filter, its image, the multiply draw and its framebuffer over the multisampled colour.
    private Rid _factorShader, _factorPipeline, _factorTexture, _factorSet, _applyShader, _applyPipeline, _applySet,
        _applyFramebuffer, _msaaColor;
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
        if (_released)
        {
            return;
        }

        long getterBefore = GC.GetAllocatedBytesForCurrentThread();
        var buffers = renderData.GetRenderSceneBuffers() as RenderSceneBuffersRD;
        if (_frames >= 20)
        {
            _bufferBytes += GC.GetAllocatedBytesForCurrentThread() - getterBefore;
        }

        if (buffers == null)
        {
            return;
        }

        Vector2I size = buffers.GetInternalSize();
        if (size.X == 0 || size.Y == 0)
        {
            return;
        }

        _rd ??= RenderingServer.GetRenderingDevice();
        if (!_rawPipeline.IsValid)
        {
            Initialize();
        }

        if (!_rawPipeline.IsValid || !_filterPipeline.IsValid)
        {
            return;
        }

        bool msaa = buffers.GetMsaa3D() != RenderingServer.ViewportMsaa.Disabled;
        // Each camera has its own effect. Rebind only if the renderer reallocates its buffers.
        Rid depth = buffers.GetDepthLayer(0), color = buffers.GetColorLayer(0);
        Rid msaaColor = msaa ? buffers.GetColorLayer(0, true) : default;
        if (_size != size || _depth != depth || _color != color || _msaaColor != msaaColor)
        {
            Bind(size, depth, color, msaaColor, buffers.GetTextureSamples());
        }

        if (msaa && !_applyPipeline.IsValid)
        {
            return;
        }

        getterBefore = GC.GetAllocatedBytesForCurrentThread();
        // This includes depth/Y correction and jitter, matching the spatial shader inverse projection.
        Projection projection = renderData.GetRenderSceneData().GetViewProjection(0);
        if (_frames >= 20)
        {
            _sceneBytes += GC.GetAllocatedBytesForCurrentThread() - getterBefore;
        }

        Projection inverse = projection.Inverse();
        Span<float> values = MemoryMarshal.Cast<byte, float>(_push.AsSpan());
        for (int column = 0; column < 4; column++)
        {
            for (int row = 0; row < 4; row++)
            {
                values[column * 4 + row] = inverse[column][row];
            }
        }

        values[16] = size.X; values[17] = size.Y;
        values[18] = projection.X.X; values[19] = projection.Y.Y;
        long list = _rd!.ComputeListBegin();
        Dispatch(list, _rawPipeline, _rawSet, size);
        _rd.ComputeListAddBarrier(list);
        if (msaa)
        {
            Dispatch(list, _factorPipeline, _factorSet, size);
        }
        else
        {
            Dispatch(list, _filterPipeline, _filterSet, size);
        }

        _rd.ComputeListEnd();
        if (!msaa)
        {
            return;
        }

        long draw = _rd.DrawListBegin(_applyFramebuffer, RenderingDevice.DrawFlags.DefaultAll,
            [], 0f, 0u, null, 0u);
        _rd.DrawListBindRenderPipeline(draw, _applyPipeline);
        _rd.DrawListBindUniformSet(draw, _applySet, 0);
        _rd.DrawListDraw(draw, false, 1, 3);
        _rd.DrawListEnd();
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
    private static string Defined(string source, string define) => source.Replace("#version 450", "#version 450\n#define " + define);
    private void Initialize()
    {
        _rawShader = Compile(_rawSource);
        _filterShader = Compile(Denoise ? _filterSource : Defined(_filterSource, "AO_UNFILTERED"));
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
    /// <summary>The MSAA path's shaders, compiled the first time a camera renders with MSAA.</summary>
    private void InitializeMsaa()
    {
        string filter = Defined(_filterSource, "AO_FACTOR");
        _factorShader = Compile(Denoise ? filter : Defined(filter, "AO_UNFILTERED"));
        using var source = new RDShaderSource { SourceVertex = ApplyVertex, SourceFragment = ApplyFragment };
        using RDShaderSpirV spirv = _rd!.ShaderCompileSpirVFromSource(source);
        if (spirv.CompileErrorVertex.Length != 0 || spirv.CompileErrorFragment.Length != 0)
        {
            GD.PushError(spirv.CompileErrorVertex, spirv.CompileErrorFragment);
        }
        else
        {
            _applyShader = _rd.ShaderCreateFromSpirV(spirv);
        }

        if (_factorShader.IsValid)
        {
            _factorPipeline = _rd.ComputePipelineCreate(_factorShader);
        }
    }
    private RDUniform Uniform(int binding, Rid texture, bool sampled)
    {
        var uniform = new RDUniform { Binding = binding, UniformType = sampled
            ? RenderingDevice.UniformType.SamplerWithTexture : RenderingDevice.UniformType.Image };
        if (sampled)
        {
            uniform.AddId(_sampler);
        }

        uniform.AddId(texture);
        return uniform;
    }
    private Rid UniformSet(Rid shader, params RDUniform[] uniforms)
    {
        var set = new Godot.Collections.Array<RDUniform>(uniforms);
        using var storage = (Godot.Collections.Array)set;
        Rid rid = _rd!.UniformSetCreate(set, shader, 0);
        foreach (RDUniform uniform in uniforms)
        {
            uniform.Dispose();
        }

        return rid;
    }
    private Rid HalfTexture(Vector2I size)
    {
        using var format = new RDTextureFormat { Width = (uint)size.X, Height = (uint)size.Y,
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit | RenderingDevice.TextureUsageBits.SamplingBit };
        using var view = new RDTextureView();
        return _rd!.TextureCreate(format, view);
    }
    private void Bind(Vector2I size, Rid depth, Rid color, Rid msaaColor, RenderingDevice.TextureSamples samples)
    {
        FreeSet(ref _rawSet); FreeSet(ref _filterSet); Free(ref _texture);
        FreeMsaaTargets();
        _size = size; _depth = depth; _color = color; _msaaColor = msaaColor;
        _texture = HalfTexture(size);
        _rawSet = UniformSet(_rawShader, Uniform(0, depth, true), Uniform(1, _texture, false));
        if (!msaaColor.IsValid)
        {
            _filterSet = UniformSet(_filterShader, Uniform(0, depth, true), Uniform(1, _texture, true), Uniform(2, color, false));
            return;
        }
        // Under MSAA the resolved colour is not a storage image: the factor goes to its own.
        if (!_applyShader.IsValid)
        {
            InitializeMsaa();
        }

        if (!_applyShader.IsValid || !_factorPipeline.IsValid)
        {
            return;
        }

        _factorTexture = HalfTexture(size);
        _factorSet = UniformSet(_factorShader, Uniform(0, depth, true), Uniform(1, _texture, true), Uniform(2, _factorTexture, false));
        _applySet = UniformSet(_applyShader, Uniform(0, _factorTexture, true));
        var targets = new Godot.Collections.Array<Rid> { msaaColor };
        using (var targetStorage = (Godot.Collections.Array)targets)
        {
            _applyFramebuffer = _rd!.FramebufferCreate(targets);
        }
        // Colour = factor x destination; the destination's alpha is kept.
        using var attachment = new RDPipelineColorBlendStateAttachment { EnableBlend = true,
            SrcColorBlendFactor = RenderingDevice.BlendFactor.Zero, DstColorBlendFactor = RenderingDevice.BlendFactor.SrcColor,
            SrcAlphaBlendFactor = RenderingDevice.BlendFactor.Zero, DstAlphaBlendFactor = RenderingDevice.BlendFactor.One };
        var attachments = new Godot.Collections.Array<RDPipelineColorBlendStateAttachment> { attachment };
        using var attachmentStorage = (Godot.Collections.Array)attachments;
        using var blend = new RDPipelineColorBlendState { Attachments = attachments };
        using var raster = new RDPipelineRasterizationState();
        using var multisample = new RDPipelineMultisampleState { SampleCount = samples };
        using var depthState = new RDPipelineDepthStencilState();
        _applyPipeline = _rd.RenderPipelineCreate(_applyShader, _rd.FramebufferGetFormat(_applyFramebuffer),
            RenderingDevice.InvalidFormatId, RenderingDevice.RenderPrimitive.Triangles, raster, multisample, depthState, blend);
    }
    /// <summary>A full-screen triangle; the fragment reads the factor at its own pixel.</summary>
    private const string ApplyVertex = "#version 450\nvoid main() {\n"
        + " vec2 p = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);\n gl_Position = vec4(p * 2. - 1., 0., 1.);\n}\n";
    private const string ApplyFragment = "#version 450\nlayout(set=0,binding=0) uniform sampler2D factor_texture;\n"
        + "layout(location=0) out vec4 frag_color;\n"
        + "void main() { frag_color = vec4(texelFetch(factor_texture, ivec2(gl_FragCoord.xy), 0).rrr, 1.); }\n";
    /// <summary>Called when the owning camera leaves the tree; all RD resources are freed on the render thread.</summary>
    public void Release()
    {
        if (_released)
        {
            return;
        }

        _released = true;
        GD.Print("World occlusion callback: ", _measured, " warmed frames allocated ", _bytes, " B; buffers ", _bufferBytes, "; scene ", _sceneBytes);
        RenderingServer.CallOnRenderThread(Callable.From(FreeResources));
    }
    private void FreeMsaaTargets()
    {
        Free(ref _applyPipeline); FreeSet(ref _applySet); FreeSet(ref _factorSet);
        if (_applyFramebuffer.IsValid && _rd!.FramebufferIsValid(_applyFramebuffer))
        {
            _rd.FreeRid(_applyFramebuffer);
        }

        _applyFramebuffer = default;
        Free(ref _factorTexture);
    }
    private void FreeResources()
    {
        if (_rd == null)
        {
            return;
        }

        FreeSet(ref _rawSet); FreeSet(ref _filterSet); Free(ref _texture);
        FreeMsaaTargets();
        Free(ref _factorPipeline); Free(ref _factorShader); Free(ref _applyShader);
        Free(ref _rawPipeline); Free(ref _filterPipeline); Free(ref _rawShader); Free(ref _filterShader); Free(ref _sampler);
    }
    private void FreeSet(ref Rid rid)
    {
        if (rid.IsValid && _rd!.UniformSetIsValid(rid))
        {
            _rd.FreeRid(rid);
        }

        rid = default;
    }
    private void Free(ref Rid rid)
    {
        if (rid.IsValid)
        {
            _rd!.FreeRid(rid);
        }

        rid = default;
    }
}
