// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Effect
{
    using System;
    using Data;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.Renderer;
    using UnrealSharp.Core;
    using UnrealSharp.CoreUObject;
    using UnrealSharp.Engine;
    using Vector3 = UnityEngine.Vector3;

    public sealed class RotatingSpriteEffect : TickableGameEntityScript, IDisposable
    {
        private IGameEntity _root;
        private USceneComponent? _rootComponent;
        private AnimatedBillboardRenderer _spriteRenderer;

        private float _rotationSpeed;
        private float _rotationDegrees;

        protected override void OnDisableGameEntity()
        {
            Dispose();
        }

        public void Init(GameResourceProvider resourceProvider,
            string textureName,
            Vector3 scale,
            float rotationSpeed)
        {
            _rotationSpeed = rotationSpeed;
            _rotationDegrees = 0f;

            ITexture2D texture = resourceProvider.GetEffectTexture(textureName, out var hasAlphaChannel);
            if (texture == null) return;

            _root = GameEntityFactory.Create($"RotatingSpriteEffect_{textureName}",
                GameEntity, worldPositionStays: false);

            _rootComponent = (_root.NativeObject as AActor)?.RootComponent;
            if (_rootComponent is { } root && root.IsValid())
            {
                // Offset the plane by 2 cm so it stays above the floor surface.
                root.SetRelativeTransform(new FTransform(new FRotator(0, 0, 0),
                    new FVector(0, 0, 2), new FVector(scale.z, scale.x, scale.y)), false, out _, false);
            }

            ISprite sprite = texture.CreateSprite(0f, 0f, texture.Width, texture.Height, 0.5f, 0.5f);
            _spriteRenderer = _root.AddComponent<AnimatedBillboardRenderer>();
            _spriteRenderer.ShowGroundSprite(sprite, useAdditiveBlend: !hasAlphaChannel);
        }

        protected override void OnUpdateGameEntity(float deltaTime)
        {
            if (_rootComponent is { } root && root.IsValid())
            {
                _rotationDegrees = (_rotationDegrees + _rotationSpeed * deltaTime) % 360f;
                root.SetRelativeRotation(new FRotator(0, _rotationDegrees, 0), false, out _, false);
            }
        }

        public void Dispose()
        {
            if (_spriteRenderer != null)
            {
                _spriteRenderer.Destroy();
                _spriteRenderer = null;
            }

            _rootComponent = null;
            if (_root != null)
            {
                _root.Destroy();
                _root = null;
            }
        }
    }
}
