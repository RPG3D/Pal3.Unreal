using System;
using System.Buffers;
using System.IO;
using Engine.Core.Abstraction;
using Engine.DataLoader;

namespace Pal3.Game.Data;

internal sealed class UeTextureLoaderFactory(ITextureFactory textureFactory) : ITextureLoaderFactory
{
    private readonly UploadFactory _ddsUpload = new(textureFactory, TexturePixelRows.DdsFileOrder);
    private readonly UploadFactory _bmpUpload = new(textureFactory, TexturePixelRows.BottomUp);

    public ITextureLoader GetTextureLoader(string fileExtension)
    {
        string extension = fileExtension.StartsWith('.') ? fileExtension : "." + fileExtension;
        return extension.ToLowerInvariant() switch
        {
            ".dds" => new DxtTextureLoader(_ddsUpload),
            ".bmp" or ".bm" => new BmpTextureLoader(_bmpUpload),
            ".tga" => new TgaUploadLoader(textureFactory),
            _ => throw new ArgumentException($"Texture format not supported: {extension}", nameof(fileExtension))
        };
    }

    private sealed class TgaUploadLoader : ITextureLoader
    {
        private readonly UploadFactory _upload;
        private readonly TgaTextureLoader _decoder;

        public TgaUploadLoader(ITextureFactory textureFactory)
        {
            _upload = new UploadFactory(textureFactory, TexturePixelRows.TopDown);
            _decoder = new TgaTextureLoader(_upload);
        }

        public void Load(byte[] data, out bool hasAlphaChannel)
        {
            if (data.Length < 18) throw new InvalidDataException("Invalid TGA header");
            _decoder.Load(data, out hasAlphaChannel);
            // The upstream decoder preserves file order. Normalize bottom-origin
            // TGA rows before uploading, including the dialogue portraits.
            _upload.ReverseRows = (data[17] & 0x20) == 0;
        }

        public ITexture2D ToTexture() => _decoder.ToTexture();
    }

    private sealed class UploadFactory(ITextureFactory target, TexturePixelRows rows) : ITextureFactory
    {
        public bool ReverseRows { get; set; }

        public ITexture2D CreateTexture(int width, int height, byte[] rgba32DataBuffer)
        {
            if (ReverseRows) FlipRows(rgba32DataBuffer, width, height);
            ITexture2D texture = target.CreateTexture(width, height, rgba32DataBuffer);
            if (texture is UnityTexture2D ueTexture) ueTexture.PixelRows = rows;
            return texture;
        }

        public ITexture2D CreateWhiteTexture() => target.CreateWhiteTexture();

        private static void FlipRows(byte[] rgba, int width, int height)
        {
            int rowBytes = checked(width * 4);
            byte[] temporaryRow = ArrayPool<byte>.Shared.Rent(rowBytes);
            try
            {
                for (int y = 0; y < height / 2; y++)
                {
                    Span<byte> top = rgba.AsSpan(y * rowBytes, rowBytes);
                    Span<byte> bottom = rgba.AsSpan((height - y - 1) * rowBytes, rowBytes);
                    top.CopyTo(temporaryRow);
                    bottom.CopyTo(top);
                    temporaryRow.AsSpan(0, rowBytes).CopyTo(bottom);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(temporaryRow);
            }
        }
    }
}
