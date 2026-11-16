// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Data
{
    using System;
    using Core.FileSystem;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.DataLoader;

    public abstract class TextureProviderBase
    {
        protected ITexture2D GetTexture(ICpkFileSystem fileSystem,
            string texturePath,
            ITextureLoader textureLoader,
            out bool hasAlphaChannel)
        {
            byte[] data;

            try
            {
                data = fileSystem.ReadAllBytes(texturePath);
            }
            catch (Exception)
            {
                hasAlphaChannel = false;
                return null;
            }

            textureLoader.Load(data, out hasAlphaChannel);
            var texture = textureLoader.ToTexture();
            if (texture is UnityTexture2D native)
            {
                native.PixelRows = textureLoader switch
                {
                    TgaTextureLoader => TexturePixelRows.TopDown,
                    BmpTextureLoader => TexturePixelRows.BottomUp,
                    DxtTextureLoader => TexturePixelRows.DdsFileOrder,
                    _ => TexturePixelRows.Unspecified
                };
            }
            return texture;
        }
    }
}
