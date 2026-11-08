// ---------------------------------------------------------------------------------------------
//  Copyright (c) 2021-2025, Jiaqi (0x7c13) Liu. All rights reserved.
//  See LICENSE file in the project root for license information.
// ---------------------------------------------------------------------------------------------

namespace Pal3.Game.Effect
{
    using System;
    using Core.Contract.Constants;
    using Core.Contract.Enums;
    using Core.DataReader.Cpk;
    using Core.DataReader.Pol;
    using Core.Utilities;
    using Data;
    using Engine.Core.Abstraction;
    using Engine.Core.Implementation;
    using Engine.Extensions;
    using Engine.Renderer;
    using Rendering.Material;
    using Rendering.Renderer;

    using Vector3 = UnityEngine.Vector3;
    using UnrealSharp.Core;
    using UnrealSharp.CoreUObject;
    using UnrealSharp.Engine;
    using UnrealSharp.ProceduralMeshComponent;
    using UnrealSharp.SharpDemo;

    public sealed class FireEffect : GameEntityScript, IEffect
    {
        public IGameEntity EffectGameEntity { get; private set; }
        public FireEffectType FireEffectType { get; private set; }

        private (ITexture2D texture, bool hasAlphaChannel)[] _effectTextures = Array.Empty<(ITexture2D texture, bool hasAlphaChannel)>();
        private PolyModelRenderer _sceneObjectRenderer;
        private UMaterialInterface _billboardMaterial;
        private int _frame = -1;
        private float _elapsed;

        protected override void OnDisableGameEntity()
        {
            Dispose();
        }

        public void Init(GameResourceProvider resourceProvider, uint effectParameter)
        {
            FireEffectType = (FireEffectType)effectParameter;
            (string TexturePathFormat, string ModelPath, float Size, float _) info = EffectConstants.FireEffectInfo[FireEffectType];
            IMaterialManager materialManager = resourceProvider.GetMaterialManager();

            if (!string.IsNullOrEmpty(info.ModelPath))
            {
                PolFile polFile = resourceProvider.GetGameResourceFile<PolFile>(info.ModelPath);
                ITextureResourceProvider textureProvider = resourceProvider.CreateTextureResourceProvider(
                    CoreUtility.GetDirectoryName(info.ModelPath, CpkConstants.DirectorySeparatorChar));
                _sceneObjectRenderer = GameEntity.AddComponent<PolyModelRenderer>();
                _sceneObjectRenderer.Render(polFile,
                    textureProvider,
                    materialManager,
                    isStaticObject: false);
            }

            if (!string.IsNullOrEmpty(info.TexturePathFormat))
            {
                _effectTextures = resourceProvider.GetEffectTextures(
                    GraphicsEffectType.Fire, info.TexturePathFormat);

                float yPosition = 0f;
                if (_sceneObjectRenderer != null)
                {
                    yPosition = _sceneObjectRenderer.GetMeshBounds().max.y;
                }

                EffectGameEntity = GameEntityFactory.Create($"Effect_{GraphicsEffectType.Fire.ToString()}_{FireEffectType.ToString()}",
                    GameEntity, worldPositionStays: false);
                EffectGameEntity.Transform.LocalScale = new Vector3(info.Size, info.Size, info.Size);
                EffectGameEntity.Transform.LocalPosition = new Vector3(0f, yPosition, 0f);

                if (EffectGameEntity is UEGameEntity entity && _effectTextures.Length != 0 &&
                    _effectTextures[0].texture is { } firstTexture)
                {
                    _billboardMaterial = UPal3AssetHelper.CreatePal3MaterialBlend(_effectTextures[0].hasAlphaChannel ? 2 : 1);
                    Pal3TransientAssets.Track(this, _billboardMaterial);
                    var quad = entity.Actor.AddComponentByClass<UProceduralMeshComponent>(false, new FTransform());
                    double halfWidth = firstTexture.Width / 2.0;
                    double height = firstTexture.Height;
                    quad.CreateMeshSection(0,
                        new FVector[] { new(0, -halfWidth, 0), new(0, halfWidth, 0), new(0, halfWidth, height), new(0, -halfWidth, height) },
                        new[] { 0, 1, 2, 0, 2, 3 }, Array.Empty<FVector>(),
                        new FVector2D[] { new(0, 1), new(1, 1), new(1, 0), new(0, 0) },
                        Array.Empty<FVector2D>(), Array.Empty<FVector2D>(), Array.Empty<FVector2D>(),
                        Array.Empty<FLinearColor>(), Array.Empty<FProcMeshTangent>(), false);
                    quad.SetMaterial(0, _billboardMaterial);
                    // Additive textures contain black in the transparent area. Keep
                    // the flame luminous even while the room is tinted for night.
                    UPal3AssetHelper.SetVectorParameter(_billboardMaterial, new FName("TintColor"), new FLinearColor(1, 1, 1, 1));
                    OnUpdateGameEntity(0);
                }
            }
        }

        protected override void OnUpdateGameEntity(float deltaTime)
        {
            if (_billboardMaterial == null || EffectGameEntity.IsNullOrDisposed()) return;
            if (Pal3Boot.CameraActor is { } camera)
            {
                Vector3 target = UnityUeConvert.ToUnityPosition(camera.ActorLocation);
                target.y = EffectGameEntity.Transform.Position.y;
                EffectGameEntity.Transform.LookAt(target);
            }

            _elapsed += deltaTime;
            int frame = (int)(_elapsed * EffectConstants.AnimatedFireEffectFrameRate) % _effectTextures.Length;
            if (frame == _frame) return;
            _frame = frame;
            if (_effectTextures[frame].texture is UnityTexture2D { UeTexture: { } texture })
                UPal3AssetHelper.BindTexture(_billboardMaterial, new FName("Pal3Tex"), texture);
        }

        public void Dispose()
        {
            if (_sceneObjectRenderer != null)
            {
                _sceneObjectRenderer.Dispose();
                _sceneObjectRenderer.Destroy();
                _sceneObjectRenderer = null;
            }

            if (EffectGameEntity != null)
            {
                EffectGameEntity.Destroy();
                EffectGameEntity = null;
            }

            if (_billboardMaterial != null)
            {
                Pal3TransientAssets.Release(_billboardMaterial);
                _billboardMaterial = null;
            }
        }
    }
}
