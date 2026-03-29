using Vortice.Direct3D9;

namespace DMAW_DND;

public sealed class D3D9Host : IDisposable
{
    readonly IDirect3D9 _d3d;
    IDirect3DDevice9? _device;
    PresentParameters _pp;
    bool _disposed;

    public D3D9Host(IntPtr hwnd, int width, int height)
    {
        _d3d = D3D9.Direct3DCreate9() ?? throw new InvalidOperationException("Direct3DCreate9 failed.");
        _pp = CreatePresentParameters(hwnd, width, height);
        _device = _d3d.CreateDevice(0, DeviceType.Hardware, hwnd,
            CreateFlags.HardwareVertexProcessing | CreateFlags.Multithreaded, _pp);
    }

    public IDirect3DDevice9 Device => _device ?? throw new ObjectDisposedException(nameof(D3D9Host));

    static PresentParameters CreatePresentParameters(IntPtr hwnd, int width, int height)
    {
        uint w = (uint)Math.Max(width, 1);
        uint h = (uint)Math.Max(height, 1);
        return new PresentParameters
        {
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
            BackBufferFormat = Format.Unknown,
            BackBufferWidth = w,
            BackBufferHeight = h,
            PresentationInterval = PresentInterval.Immediate,
            DeviceWindowHandle = hwnd,
            EnableAutoDepthStencil = true,
            AutoDepthStencilFormat = Format.D16,
        };
    }

    public void Resize(int width, int height)
    {
        if (_device == null || _disposed)
            return;
        uint w = (uint)Math.Max(width, 1);
        uint h = (uint)Math.Max(height, 1);
        if (_pp.BackBufferWidth == w && _pp.BackBufferHeight == h)
            return;
        _pp.BackBufferWidth = w;
        _pp.BackBufferHeight = h;
        _device.Reset(ref _pp);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _device?.Dispose();
        _device = null;
        _d3d.Dispose();
    }
}
