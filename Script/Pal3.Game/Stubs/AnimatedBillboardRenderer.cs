using System;
using System.Collections;
using Engine.Core.Abstraction;
using Engine.Core.Implementation;
using Engine.Coroutine;
using Pal3.Game.Data;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.ProceduralMeshComponent;
using UnrealSharp.SharpDemo;
using UnityEngine;

namespace Engine.Renderer
{
    // UnrealSharp portability. Implements the upstream billboard API using
    // a UE quad and the existing game-thread coroutine scheduler.
    public sealed class AnimatedBillboardRenderer : GameEntityScript
    {
        private readonly record struct FrameGeometry(
            float Width, float Height, float PivotX, float PivotY,
            float Left, float Top, float Right, float Bottom);

        private static readonly FName TextureParameter = new("Pal3Tex");
        private ISprite[] _sprites = Array.Empty<ISprite>();
        private object? _frameWait;
        private IMaterial? _sourceMaterial;
        private UMaterialInterface? _material;
        private bool _ownsMaterial;
        private UProceduralMeshComponent? _quad;
        private APlayerCameraManager? _camera;
        private UTexture2D? _texture;
        private FrameGeometry? _geometry;
        private bool _visible;
        private int _playback;

        public void Init(ISprite[] sprites, float fps, IMaterial? material = null)
        {
            Destroy();
            _sprites = sprites;
            _sourceMaterial = material;
            if (fps > 0f && float.IsFinite(fps))
                _frameWait = CoroutineYieldInstruction.WaitForSeconds(1f / fps);
        }

        public void StartAnimation(int loopCount = -1) => StartCoroutine(PlayAnimationAsync(loopCount));

        public IEnumerator PlayAnimationAsync(int loopCount) => PlayFrames(loopCount, ++_playback);

        private IEnumerator PlayFrames(int loopCount, int playback)
        {
            if (_frameWait == null || _sprites.Length == 0 || loopCount == 0 || loopCount < -1)
                yield break;

            for (int loops = 0; loopCount == -1 || loops < loopCount; loops++)
            {
                foreach (ISprite sprite in _sprites)
                {
                    if (playback != _playback || GameEntity.IsNativeObjectDisposed)
                        yield break;

                    ShowFrame(sprite);
                    yield return _frameWait;
                }
            }
        }

        public void StopAnimation()
        {
            _playback++;
            _visible = false;
            if (_quad.IsValid()) _quad!.SetVisibility(false);
        }

        protected override void OnLateUpdateGameEntity(float deltaTime)
        {
            if (!_visible || GameEntity.IsNativeObjectDisposed) return;
            if (!_camera.IsValid()) _camera = UGameplayStatics.GetPlayerCameraManager(0);
            if (!_camera.IsValid()) return;

            Vector3 position = Transform.Position;
            Vector3 target = UnityUeConvert.ToUnityPosition(_camera!.CameraLocation);
            target.y = position.y;
            if (target != position) Transform.LookAt(target);
        }

        private void ShowFrame(ISprite? sprite)
        {
            if (sprite?.Texture is not { Width: > 0, Height: > 0 } source ||
                source.NativeObject is not UTexture2D texture || !texture.IsValid())
            {
                return;
            }

            if (!_quad.IsValid() && !CreateQuad()) return;

            var unitySprite = sprite as UnitySprite;
            Rect region = unitySprite?.TextureRect ?? new Rect(0, 0, source.Width, source.Height);
            Vector2 pivot = unitySprite?.Pivot ?? new Vector2(0.5f, 0.5f);
            if (region.width <= 0f || region.height <= 0f) return;

            var geometry = new FrameGeometry(region.width, region.height, pivot.x, pivot.y,
                region.xMin / source.Width, 1f - region.yMax / source.Height,
                region.xMax / source.Width, 1f - region.yMin / source.Height);
            if (_geometry != geometry)
            {
                UpdateQuad(geometry);
                _geometry = geometry;
            }

            if (_texture != texture)
            {
                UPal3AssetHelper.BindTexture(_material, TextureParameter, texture);
                _texture = texture;
            }
            if (!_visible)
            {
                _visible = true;
                _quad!.SetVisibility(true);
            }
        }

        private bool CreateQuad()
        {
            if (GameEntity.NativeObject is not AActor actor || !actor.IsValid()) return false;

            // PAL3's sprite shader treats black as transparent. Sprites with
            // an alpha channel use the regular translucent material.
            bool isAdditive = _sourceMaterial?.ShaderName == "Pal3/Sprite";
            if (!isAdditive && _sourceMaterial?.NativeObject is UMaterialInterface provided && provided.IsValid())
            {
                _material = provided;
            }
            else
            {
                _material = UPal3AssetHelper.CreatePal3MaterialBlend(isAdditive ? 1 : 2);
                if (!_material.IsValid()) return false;
                _ownsMaterial = true;
                Pal3TransientAssets.Track(this, _material);
                UPal3AssetHelper.SetVectorParameter(_material, new FName("TintColor"), new FLinearColor(1, 1, 1, 1));
            }

            _quad = actor.AddComponentByClass<UProceduralMeshComponent>(false, new FTransform());
            if (!_quad.IsValid())
            {
                if (_ownsMaterial) Pal3TransientAssets.Release(_material);
                _ownsMaterial = false;
                _material = null;
                return false;
            }
            _quad!.SetMaterial(0, _material);
            _geometry = null;
            _texture = null;
            _visible = false;
            return true;
        }

        private void UpdateQuad(FrameGeometry frame)
        {
            double left = -frame.PivotX * frame.Width;
            double bottom = -frame.PivotY * frame.Height;
            double right = left + frame.Width;
            double top = bottom + frame.Height;
            UPal3DebugOverlay.CreateProceduralSection(_quad, 0,
                new FVector[] { new(0, left, bottom), new(0, right, bottom), new(0, right, top), new(0, left, top) },
                new[] { 0, 1, 2, 0, 2, 3 }, Array.Empty<FVector>(),
                new FVector2D[] { new(frame.Left, frame.Bottom), new(frame.Right, frame.Bottom),
                    new(frame.Right, frame.Top), new(frame.Left, frame.Top) },
                Array.Empty<FVector2D>(), false);
        }

        public new void Destroy()
        {
            StopAnimation();
            if (_quad.IsValid() && GameEntity.NativeObject is AActor actor && actor.IsValid())
                _quad!.DestroyComponent(actor);
            if (_ownsMaterial) Pal3TransientAssets.Release(_material);
            _ownsMaterial = false;
            _quad = null;
            _camera = null;
            _texture = null;
            _material = null;
            _geometry = null;
            _frameWait = null;
            _sourceMaterial = null;
            _sprites = Array.Empty<ISprite>();
        }

        protected override void OnDisableGameEntity() => Destroy();
    }
}
