using Engine.Core.Abstraction;
using Pal3.Game.Data;
using UnrealSharp.Engine;

namespace Pal3.Game.Dev
{
    public static class DialoguePresentation
    {
        public sealed record Portrait(UTexture2D Texture, bool RightAligned, float U0, float V0, float U1, float V1);

        public static string Text { get; private set; } = "";
        public static Portrait? Avatar { get; private set; }
        public static IReadOnlyList<string> Choices { get; private set; } = Array.Empty<string>();
        public static string ChoicePrompt { get; private set; } = "";
        public static Portrait? ChoiceAvatar { get; private set; }
        private static string _lastPage = "";
        private static Portrait? _lastPageAvatar;

        public static void ShowChoices(string[] choices)
        {
            ChoicePrompt = _lastPage;
            ChoiceAvatar = _lastPageAvatar;
            _lastPage = "";
            _lastPageAvatar = null;
            Choices = Array.AsReadOnly(choices);
        }

        public static void ClearChoices()
        {
            Choices = Array.Empty<string>();
            ChoicePrompt = "";
            ChoiceAvatar = null;
            _lastPage = "";
            _lastPageAvatar = null;
        }

        public static void ShowText(string text)
        {
            // UMG text blocks display plain text; strip the upstream TMP color tags.
            Text = System.Text.RegularExpressions.Regex.Replace(text, "</?color(?:=[^>]+)?>", "");
            _lastPage = Text;
            _lastPageAvatar = Avatar;
        }

        public static void ShowAvatar(ISprite? sprite, bool rightAligned)
        {
            Avatar = null;
            if (sprite?.Texture is not UnityTexture2D { UeTexture: not null } texture) return;
            if (!texture.TryGetAlphaBounds(out float u0, out float v0, out float u1, out float v1))
            {
                u0 = 0f;
                v0 = 0f;
                u1 = 1f;
                v1 = 1f;
            }
            Avatar = new Portrait(texture.UeTexture, rightAligned, u0, v0, u1, v1);
        }

        public static void Clear()
        {
            Text = "";
            Avatar = null;
        }
    }
}
