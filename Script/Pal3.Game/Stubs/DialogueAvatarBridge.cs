// Bridge between the ported dialogue UI (stub Sprite/Image objects) and the native
// HUD dialogue bar: the avatar portrait is a real UE texture, so hand it to the C++
// overlay which draws the text box (portrait + wrapped text) at the bottom.
using Engine.Core.Abstraction;

namespace Pal3.Game.Dev
{
    public static class DialogueAvatarBridge
    {
        public static void Show(ISprite? sprite, bool rightAligned)
        {
            try
            {
                if (sprite?.Texture is Pal3.Game.Data.UnityTexture2D { UeTexture: not null } texture)
                {
                    // Crop to the opaque part of the (padded) portrait texture.
                    if (!texture.TryGetAlphaBounds(out float u0, out float v0, out float u1, out float v1))
                    {
                        u0 = 0f; v0 = 0f; u1 = 1f; v1 = 1f;
                    }
                    UnrealSharp.SharpDemo.UPal3DebugOverlay.SetDialogueAvatar(
                        texture.UeTexture, rightAligned, u0, v0, u1, v1);
                    return;
                }
            }
            catch (System.Exception)
            {
                // Diagnostics-only path: never break the dialogue flow.
            }

            Clear();
        }

        public static void Clear()
        {
            try
            {
                UnrealSharp.SharpDemo.UPal3DebugOverlay.ClearDialogueAvatar();
            }
            catch (System.Exception)
            {
            }
        }
    }
}
