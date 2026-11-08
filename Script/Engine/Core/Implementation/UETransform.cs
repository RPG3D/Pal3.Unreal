// ITransform over a real UE actor/scene component. Every Unity-space value that
// crosses this boundary goes through UnityUeConvert: the game code keeps speaking
// Unity coordinates (meters, X-right/Y-up/Z-forward) exactly like upstream
// PAL3.Unity, while the UE actors hold UE coordinates (cm, X-forward/Y-right/Z-up).
//
// LIFETIME: game code destroys entities while controllers keep ticking (Unity
// tolerates that: destroyed objects compare == null and every access is a no-op).
// UE has no such protection — calling into a destroyed UObject is an access
// violation, so every member below is guarded by IsValid().
using System;
using System.Runtime.CompilerServices;
using Engine.Core.Abstraction;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;

namespace Engine.Core.Implementation
{
    public class UETransform : ITransform
    {
        readonly AActor _actor;
        readonly USceneComponent _component;

        public object NativeObject => _actor;
        public bool IsNativeObjectDisposed => !IsAlive;

        bool IsAlive => _actor != null && _actor.IsValid();

        // Diagnostic switch: with PAL3_FREEZE_CAMERA=1 the port stops driving the
        // camera actor, so an external tool (ue_remote.py) can place it and the view
        // stays put. Used to separate "conversion is wrong" from "camera animation is
        // wrong" without rebuilding gameplay code.
        static readonly bool s_FreezeCamera =
            System.Environment.GetEnvironmentVariable("PAL3_FREEZE_CAMERA") == "1";

        bool IsFrozenCamera =>
            s_FreezeCamera
            && UEGameEntityFactory.CameraActor != null
            && _actor == UEGameEntityFactory.CameraActor;
        bool IsComponentAlive => _component != null && _component.IsValid();

        public UETransform(AActor actor) { _actor = actor; _component = actor.RootComponent ?? throw new InvalidOperationException("No root component"); }
        public UETransform(USceneComponent c) { _component = c; _actor = c.Owner ?? throw new InvalidOperationException("No owner"); }

        public UnityEngine.Vector3 Position
        {
            get => IsAlive ? UnityUeConvert.ToUnityPosition(_actor.ActorLocation) : default;
            set { if (IsAlive && !IsFrozenCamera) _actor.SetActorLocation(UnityUeConvert.ToUePosition(value)); }
        }

        public UnityEngine.Vector3 LocalPosition
        {
            get => IsComponentAlive ? UnityUeConvert.ToUnityPosition(_component.RelativeLocation) : default;
            set { if (IsComponentAlive) _component.RelativeLocation = UnityUeConvert.ToUePosition(value); }
        }

        public UnityEngine.Quaternion Rotation
        {
            get => IsAlive ? UnityUeConvert.ToUnityQuaternion(_actor.ActorRotation.ToQuaternion) : UnityEngine.Quaternion.identity;
            set
            {
                if (!IsAlive) return;

                ApplyWorldRotation(value);
            }
        }

        public UnityEngine.Quaternion LocalRotation
        {
            get => IsComponentAlive ? UnityUeConvert.ToUnityQuaternion(_component.RelativeRotation.ToQuaternion) : UnityEngine.Quaternion.identity;
            set { if (IsComponentAlive) ApplyLocalRotation(value); }
        }

        public UnityEngine.Vector3 LocalScale
        {
            get => IsComponentAlive
                ? new UnityEngine.Vector3((float)_component.RelativeScale3D.X, (float)_component.RelativeScale3D.Y, (float)_component.RelativeScale3D.Z)
                : UnityEngine.Vector3.one;
            set { if (IsComponentAlive) _component.RelativeScale3D = new FVector(value.x, value.y, value.z); }
        }

        public UnityEngine.Vector3 Forward
        {
            get => IsAlive ? UnityUeConvert.ToUnityDirection(_actor.ActorForwardVector) : UnityEngine.Vector3.forward;
            set
            {
                float lengthSquared = value.sqrMagnitude;
                if (IsAlive && !IsFrozenCamera && float.IsFinite(lengthSquared) && lengthSquared > 1e-10f)
                {
                    _actor.SetActorRotation(new FRotator(UnityUeConvert.ToUeDirection(value)));
                }
            }
        }

        public UnityEngine.Vector3 Right
        {
            get => IsAlive ? UnityUeConvert.ToUnityDirection(_actor.ActorRightVector) : UnityEngine.Vector3.right;
            set { }
        }

        public UnityEngine.Vector3 Up
        {
            get => IsAlive ? UnityUeConvert.ToUnityDirection(_actor.ActorUpVector) : UnityEngine.Vector3.up;
            set { }
        }

        public UnityEngine.Vector3 EulerAngles
        {
            get => IsAlive ? UnityUeConvert.ToUnityEuler(_actor.ActorRotation) : default;
            set { if (IsAlive) _actor.SetActorRotation(UnityUeConvert.ToUeRotator(value)); }
        }

        public void Translate(UnityEngine.Vector3 t)
        {
            if (IsAlive) _actor.AddActorWorldOffset(UnityUeConvert.ToUePosition(t), false, out _, false);
        }

        public void GetPositionAndRotation(out UnityEngine.Vector3 p, out UnityEngine.Quaternion r)
        {
            p = Position;
            r = Rotation;
        }

        public void SetPositionAndRotation(UnityEngine.Vector3 p, UnityEngine.Quaternion r)
        {
            if (IsAlive)
            {
                // Out-of-bounds write probe: any world placement farther than ~15 m
                // from the origin is almost certainly a conversion bug (orbit/follow
                // paths feeding wrong directions into cutscene keyframes).
                if (p.x * p.x + p.y * p.y + p.z * p.z > 225f)
                {
                    UnrealSharp.Log.UnrealLogger.Log("Pal3",
                        $"[PosProbe] {_actor.Name} <- ({p.x:F1},{p.y:F1},{p.z:F1}) rot=({r.eulerAngles.x:F1},{r.eulerAngles.y:F1},{r.eulerAngles.z:F1})");
                }
                _actor.SetActorLocation(UnityUeConvert.ToUePosition(p));
                ApplyWorldRotation(r);
            }
        }

        public void GetLocalPositionAndRotation(out UnityEngine.Vector3 p, out UnityEngine.Quaternion r)
        {
            p = LocalPosition;
            r = LocalRotation;
        }

        public void SetLocalPositionAndRotation(UnityEngine.Vector3 p, UnityEngine.Quaternion r)
        {
            if (IsComponentAlive)
            {
                _component.RelativeLocation = UnityUeConvert.ToUePosition(p);
                ApplyLocalRotation(r);
            }
        }

        // Unity LookAt aims +Z (forward) at the target; UE's FRotator(direction) aims
        // +X (forward) — the same semantic under the U.Z→E.X axis mapping.
        public void LookAt(UnityEngine.Vector3 t)
        {
            if (!IsAlive) return;
            FVector d = UnityUeConvert.ToUePosition(t) - _actor.ActorLocation;
            _actor.SetActorRotation(new FRotator(d));
        }

        public void RotateAround(UnityEngine.Vector3 centerPoint, UnityEngine.Vector3 axis, float angle)
        {
            if (!IsAlive) return;
            FVector pt = _actor.ActorLocation;
            FVector c = UnityUeConvert.ToUePosition(centerPoint);
            FQuat q = FQuat.CreateFromAxisAngle(FVector.Normalize(UnityUeConvert.ToUeDirection(axis)), angle * UnityEngine.Mathf.Deg2Rad);
            _actor.SetActorLocation(c + q.RotateVector(pt - c));
        }

        // Rotation mapping. The shim Quaternion stores UE FQuat components with
        // Unity euler semantics (see UnityEngine/Quaternion.cs header): the UE
        // rotation it represents is flip(q) = (-x, -y, z, w), which is the exact
        // algebraic form of the previously verified euler path
        //   FRotator(-e.x, e.y, -e.z), e = q.eulerAngles
        // (that path was validated against the Unity build: Unity camera
        // euler=(19.48,146.76,0) → UE rot=(P-19.5,Y146.8) matched PCM ground
        // truth). The flip form is preferred: no euler round trip, roll exact.
        private void ApplyWorldRotation(UnityEngine.Quaternion q)
        {
            if (!IsAlive || IsFrozenCamera)
            {
                return;
            }
            _actor.SetActorRotation(new FRotator(UnityUeConvert.ToUeQuaternion(q)));
        }

        private void ApplyLocalRotation(UnityEngine.Quaternion q)
        {
            if (!IsComponentAlive)
            {
                return;
            }
            _component.RelativeRotation = new FRotator(UnityUeConvert.ToUeQuaternion(q));
        }

        public void Destroy()
        {
            if (IsAlive) _actor.DestroyActor();
        }

        bool IManagedObject.Equals(IManagedObject? o) => o != null && o.NativeObject is AActor a && a == _actor;
    }
}
