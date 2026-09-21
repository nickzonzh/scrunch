using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Noot_Proto.NootFX;

internal sealed class D3DRenderer : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private ID3D11Device _device = null!;
    private ID3D11DeviceContext _context = null!;
    private IDCompositionDevice _composition = null!;
    private IDCompositionVisual _visual = null!;
    private IDXGIFactory2 _factory = null!;
    private ID3D11VertexShader _vs = null!;
    private ID3D11PixelShader _ps = null!;
    private ID3D11VertexShader _shadowVs = null!;
    private ID3D11PixelShader _shadowPs = null!;
    private ID3D11Buffer _settings = null!, _indices = null!;
    private ID3D11ShaderResourceView _bakeView = null!, _uvView = null!;
    private ID3D11RasterizerState _raster = null!;
    private ID3D11SamplerState _sampler = null!;
    private ID3D11BlendState _blend = null!;
    private ID3D11DepthStencilState _depthState = null!, _noDepth = null!;
    private IDXGISwapChain1? _swap;
    private ID3D11RenderTargetView? _target;
    private ID3D11Texture2D? _depth, _note;
    private ID3D11DepthStencilView? _depthView;
    private ID3D11ShaderResourceView? _noteView;
    private int _width, _height;
    private uint _indexCount;
    private int _vertexCount, _frames;
    public string Adapter { get; private set; } = "";
    public FxOverlay Overlay { get; private set; } = null!;
    private T Keep<T>(T resource) where T : IDisposable { _resources.Add(resource); return resource; }

    // Use a factory so a failure halfway through initialization still disposes
    // all native objects. No finalizer or GC timing is needed for correctness.
    public static D3DRenderer Create()
    {
        var renderer = new D3DRenderer();
        try { renderer.Initialize(); return renderer; }
        catch { renderer.Dispose(); throw; }
    }
    private void Initialize()
    {
        D3D11.D3D11CreateDevice((IDXGIAdapter?)null, DriverType.Hardware, DeviceCreationFlags.BgraSupport,
            new[] { FeatureLevel.Level_11_0 }, out _device, out _context).CheckError();
        Keep(_device); Keep(_context);
        using var dxgi = _device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgi.GetAdapter();
        Adapter = adapter.Description.Description;
        _factory = Keep(adapter.GetParent<IDXGIFactory2>());
        Overlay = Keep(new FxOverlay());
        _composition = Keep(DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgi));
        _composition.CreateTargetForHwnd(Overlay.Handle, true, out var compTarget).CheckError(); Keep(compTarget);
        _composition.CreateVisual(out _visual).CheckError(); Keep(_visual);
        compTarget.SetRoot(_visual).CheckError();
        var bake = new PaperBake(Path.Combine(AppContext.BaseDirectory, "Assets", "NootFX", "crumple.nfx"));
        _indexCount = (uint)bake.Indices.Length; _vertexCount = bake.VertexCount; _frames = bake.FrameCount;
        var samples = Keep(_device.CreateBuffer(bake.Samples, BindFlags.ShaderResource, ResourceUsage.Immutable,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, structureByteStride: 16));
        var uvs = Keep(_device.CreateBuffer(bake.UVs, BindFlags.ShaderResource, ResourceUsage.Immutable,
            CpuAccessFlags.None, ResourceOptionFlags.BufferStructured, structureByteStride: 8));
        _bakeView = Keep(_device.CreateShaderResourceView(samples)); _uvView = Keep(_device.CreateShaderResourceView(uvs));
        _indices = Keep(_device.CreateBuffer(bake.Indices, BindFlags.IndexBuffer, ResourceUsage.Immutable));
        _settings = Keep(_device.CreateBuffer(64, BindFlags.ConstantBuffer));
        string hlsl = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "NootFX", "Paper.hlsl"));
        var vs = Compiler.Compile(hlsl, "VS", "Paper.hlsl", "vs_5_0"); var ps = Compiler.Compile(hlsl, "PS", "Paper.hlsl", "ps_5_0");
        _vs = Keep(_device.CreateVertexShader(vs.Span)); _ps = Keep(_device.CreatePixelShader(ps.Span));
        _shadowVs = Keep(_device.CreateVertexShader(Compiler.Compile(hlsl, "VSShadow", "Paper.hlsl", "vs_5_0").Span));
        _shadowPs = Keep(_device.CreatePixelShader(Compiler.Compile(hlsl, "PSShadow", "Paper.hlsl", "ps_5_0").Span));
        _raster = Keep(_device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid) { DepthClipEnable = true }));
        _sampler = Keep(_device.CreateSamplerState(SamplerDescription.LinearClamp));
        _blend = Keep(_device.CreateBlendState(BlendDescription.AlphaBlend));
        _depthState = Keep(_device.CreateDepthStencilState(DepthStencilDescription.Default));
        _noDepth = Keep(_device.CreateDepthStencilState(DepthStencilDescription.None));
    }

    public unsafe void Prepare(NoteSnapshot snapshot, int padding)
    {
        int width = snapshot.Width + padding * 2, height = snapshot.Height + padding * 2;
        if (width != _width || height != _height || _swap == null)
        {
            ReleaseSurface();
            _width = width; _height = height;
            _swap = _factory.CreateSwapChainForComposition(_device, new SwapChainDescription1
            {
                Width = (uint)width, Height = (uint)height, Format = Format.B8G8R8A8_UNorm,
                BufferCount = 2, BufferUsage = Usage.RenderTargetOutput, SampleDescription = new SampleDescription(1, 0),
                SwapEffect = SwapEffect.FlipSequential, Scaling = Scaling.Stretch, AlphaMode = AlphaMode.Premultiplied
            });
            using var buffer = _swap.GetBuffer<ID3D11Texture2D>(0);
            _target = _device.CreateRenderTargetView(buffer);
            _depth = _device.CreateTexture2D(new Texture2DDescription(Format.D32_Float, (uint)width, (uint)height, 1, 1, BindFlags.DepthStencil));
            _depthView = _device.CreateDepthStencilView(_depth);
            _visual.SetContent(_swap).CheckError(); _composition.Commit().CheckError();
        }
        ReleaseNote();
        fixed (byte* pixels = snapshot.Pixels)
        {
            var desc = new Texture2DDescription(Format.B8G8R8A8_UNorm, (uint)snapshot.Width, (uint)snapshot.Height, 1, 1, BindFlags.ShaderResource, ResourceUsage.Immutable);
            _note = _device.CreateTexture2D(desc, new SubresourceData((IntPtr)pixels, (uint)snapshot.Width * 4));
        }
        _noteView = _device.CreateShaderResourceView(_note);
        Overlay.Position(snapshot.ScreenX - padding, snapshot.ScreenY - padding, width, height);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Settings { public Vector4 Viewport, Playback, Colour, Origin; }
    public void Render(NoteSnapshot snapshot, float deformation, float discard, float opacity)
    {
        _context.ClearRenderTargetView(_target!, new Color4(0, 0, 0, 0));
        _context.ClearDepthStencilView(_depthView!, DepthStencilClearFlags.Depth, 1, 0);
        _context.OMSetRenderTargets(_target!, _depthView!);
        _context.RSSetViewport(new Viewport(0, 0, _width, _height));
        _context.RSSetState(_raster); _context.OMSetBlendState(_blend);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetIndexBuffer(_indices, Format.R32_UInt, 0);
        _context.VSSetShader(_vs); _context.PSSetShader(_ps);
        _context.VSSetShaderResource(0, _bakeView); _context.VSSetShaderResource(1, _uvView);
        _context.PSSetShaderResource(2, _noteView!); _context.PSSetSampler(0, _sampler);
        _context.VSSetConstantBuffer(0, _settings); _context.PSSetConstantBuffer(0, _settings);
        var settings = new Settings
        {
            Viewport = new Vector4(_width, _height, snapshot.Width, snapshot.Height),
            Playback = new Vector4(Math.Min(deformation * (_frames - 1), _frames - 1.0001f), _vertexCount, discard, opacity),
            Colour = snapshot.Colour,
            Origin = new Vector4(_width / 2f, _height / 2f, deformation, 1)
        };
        _context.OMSetDepthStencilState(_noDepth);
        _context.VSSetShader(_shadowVs); _context.PSSetShader(_shadowPs);
        _context.UpdateSubresource(in settings, _settings); _context.Draw(6, 0);
        settings.Origin.W = 0;
        _context.VSSetShader(_vs); _context.PSSetShader(_ps);
        _context.OMSetDepthStencilState(_depthState);
        _context.UpdateSubresource(in settings, _settings); _context.DrawIndexed(_indexCount, 0, 0);
        _swap!.Present(1, PresentFlags.None).CheckError();
    }
    public void EndEffect()
    {
        Overlay?.Hide(); _context?.ClearState(); ReleaseNote();
    }
    private void ReleaseNote() { _noteView?.Dispose(); _noteView = null; _note?.Dispose(); _note = null; }
    private void ReleaseSurface()
    {
        _context?.ClearState();
        _target?.Dispose(); _target = null; _depthView?.Dispose(); _depthView = null; _depth?.Dispose(); _depth = null;
        _swap?.Dispose(); _swap = null;
    }
    public void Dispose()
    {
        EndEffect(); ReleaseSurface();
        for (int i = _resources.Count - 1; i >= 0; i--) _resources[i].Dispose();
        _resources.Clear();
    }
}
