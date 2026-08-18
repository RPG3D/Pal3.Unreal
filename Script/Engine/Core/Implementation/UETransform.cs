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
        public bool IsNativeObjectDisposed => !_actor.IsValid();

        public UETransform(AActor actor) { _actor = actor; _component = actor.RootComponent ?? throw new InvalidOperationException("No root component"); }
        public UETransform(USceneComponent c) { _component = c; _actor = c.Owner ?? throw new InvalidOperationException("No owner"); }

        public UnityEngine.Vector3 Position { get => _actor.ActorLocation; set => _actor.SetActorLocation((FVector)value); }
        public UnityEngine.Vector3 LocalPosition { get => _component.RelativeLocation; set => _component.RelativeLocation = (FVector)value; }
        public UnityEngine.Quaternion Rotation { get => (UnityEngine.Quaternion)_actor.ActorRotation.ToQuaternion; set => _actor.SetActorRotation(new FRotator((FQuat)value)); }
        public UnityEngine.Quaternion LocalRotation { get => (UnityEngine.Quaternion)_component.RelativeRotation.ToQuaternion; set => _component.RelativeRotation = new FRotator((FQuat)value); }
        public UnityEngine.Vector3 LocalScale { get => _component.RelativeScale3D; set => _component.RelativeScale3D = (FVector)value; }
        public UnityEngine.Vector3 Forward { get => _actor.ActorForwardVector; set { } }
        public UnityEngine.Vector3 Right { get => _actor.ActorRightVector; set { } }
        public UnityEngine.Vector3 Up { get => _actor.ActorUpVector; set { } }
        public UnityEngine.Vector3 EulerAngles { get { var r = _actor.ActorRotation; return new UnityEngine.Vector3((float)r.Roll, (float)r.Pitch, (float)r.Yaw); } set => _actor.SetActorRotation(new FRotator(value.y, value.z, value.x)); }

        public void Translate(UnityEngine.Vector3 t) => _actor.AddActorWorldOffset((FVector)t, false, out _, false);
        public void GetPositionAndRotation(out UnityEngine.Vector3 p, out UnityEngine.Quaternion r) { p = _actor.ActorLocation; r = (UnityEngine.Quaternion)_actor.ActorRotation.ToQuaternion; }
        public void SetPositionAndRotation(UnityEngine.Vector3 p, UnityEngine.Quaternion r) { _actor.SetActorLocationAndRotation((FVector)p, new FRotator((FQuat)r), false, out _, false); }
        public void GetLocalPositionAndRotation(out UnityEngine.Vector3 p, out UnityEngine.Quaternion r) { p = _component.RelativeLocation; r = (UnityEngine.Quaternion)_component.RelativeRotation.ToQuaternion; }
        public void SetLocalPositionAndRotation(UnityEngine.Vector3 p, UnityEngine.Quaternion r) { _component.SetRelativeLocationAndRotation((FVector)p, new FRotator((FQuat)r), false, out _, false); }
        public void LookAt(UnityEngine.Vector3 t) { FVector d = (FVector)t - _actor.ActorLocation; _actor.SetActorRotation(new FRotator(d)); }
        public void RotateAround(UnityEngine.Vector3 center, UnityEngine.Vector3 axis, float angle) { FVector pt = _actor.ActorLocation; FVector c = (FVector)center; FQuat q = FQuat.CreateFromAxisAngle(FVector.Normalize((FVector)axis), angle * UnityEngine.Mathf.Deg2Rad); _actor.SetActorLocation(c + q.RotateVector(pt - c)); }
        public void Destroy() => _actor.DestroyActor();
        bool IManagedObject.Equals(IManagedObject? o) => o != null && o.NativeObject is AActor a && a == _actor;
    }
}
