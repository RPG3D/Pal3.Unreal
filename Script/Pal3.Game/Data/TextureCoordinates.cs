using Engine.Core.Abstraction;
using Engine.Core.Implementation;

namespace Pal3.Game.Data;

internal enum TexturePixelRows
{
    Unspecified,
    TopDown,
    BottomUp,
    DdsFileOrder
}

internal static class TextureCoordinates
{
    internal static float ConvertV(float v, bool flip) => flip ? 1f - v : v;

    // MV3 and MSH UVs count from the bottom. Original PAL3 DDS variants retain the model's row order.
    internal static bool FlipCharacterV(ITexture2D? texture, bool fallback) => GetRows(texture) switch
    {
        TexturePixelRows.TopDown => true,
        TexturePixelRows.BottomUp or TexturePixelRows.DdsFileOrder => false,
        _ => fallback
    };

    // Original GameBox sprite effects use bottom-up DDS rows. Other DDS consumers keep their own UV convention.
    internal static bool HasBottomUpEffectRows(ITexture2D? texture) =>
        GetRows(texture) is TexturePixelRows.BottomUp or TexturePixelRows.DdsFileOrder;

    private static TexturePixelRows GetRows(ITexture2D? texture) =>
        texture is UnityTexture2D native ? native.PixelRows : TexturePixelRows.Unspecified;
}
