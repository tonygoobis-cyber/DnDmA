using Vortice.Direct3D9;

namespace DMAW_DND;

public static class D3D9TextureRegistry
{
    static readonly object Gate = new();
    static readonly Dictionary<nint, IDirect3DTexture9> Map = new();

    public static void Register(IDirect3DTexture9 texture)
    {
        lock (Gate)
            Map[texture.NativePointer] = texture;
    }

    public static bool TryGet(nint ptr, out IDirect3DTexture9? texture)
    {
        lock (Gate)
            return Map.TryGetValue(ptr, out texture);
    }

    public static void Unregister(nint ptr)
    {
        lock (Gate)
        {
            if (Map.Remove(ptr, out var t))
                t.Dispose();
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            foreach (var t in Map.Values)
                t.Dispose();
            Map.Clear();
        }
    }
}
