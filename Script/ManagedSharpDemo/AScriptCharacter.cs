using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Core;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.EnhancedInput;
using UnrealSharp.InputCore;
using UnrealSharp.SharpDemo;

namespace ManagedSharpDemo
{
    [UClass]
    public partial class AScriptCharacter : ACharacter
    {
        [UProperty(DefaultComponent = true, AttachmentComponent = nameof(RootComponent))]
        protected partial USpringArmComponent CameraBoom { get; set; }

        [UProperty(DefaultComponent = true, AttachmentComponent = nameof(CameraBoom), AttachmentSocket = "SpringEndpoint")]
        protected partial UCameraComponent FollowCamera { get; set; }

        [UProperty]
        partial UInputMappingContext DefaultMappingContext { get; set; }

        [UProperty]
        partial UInputAction JumpAction { get; set; }

        [UProperty]
        partial UInputAction MoveAction { get; set; }

        [UProperty]
        partial UInputAction LookAction { get; set; }

        public override void BeginPlay()
        {
            base.BeginPlay();

            APlayerController PlayerCtrl = Controller as APlayerController;

            UEnhancedInputLocalPlayerSubsystem Subsystem = USubsystemBlueprintLibrary.GetLocalPlayerSubsystem<UEnhancedInputLocalPlayerSubsystem>(PlayerCtrl);

            //input config — dynamically created (no binary asset dependency)
            {
                DefaultMappingContext = NewObject<UInputMappingContext>();
                JumpAction = NewObject<UInputAction>();
                MoveAction = NewObject<UInputAction>(); MoveAction.ValueType = EInputActionValueType.Axis2D;
                LookAction = NewObject<UInputAction>(); LookAction.ValueType = EInputActionValueType.Axis2D;

                var SwizzleToY = NewObject<UInputModifierSwizzleAxis>(); SwizzleToY.Order = EInputAxisSwizzle.YXZ;
                var Negate = NewObject<UInputModifierNegate>();

                USharpInputHelper.MapKey(DefaultMappingContext, JumpAction, Keys.SpaceBar);
                USharpInputHelper.MapKey(DefaultMappingContext, JumpAction, Keys.Gamepad_FaceButton_Bottom);

                USharpInputHelper.MapKeyWithModifiers(DefaultMappingContext, MoveAction, Keys.W, new UInputModifier[] { SwizzleToY });
                USharpInputHelper.MapKeyWithModifiers(DefaultMappingContext, MoveAction, Keys.S, new UInputModifier[] { SwizzleToY, Negate });
                USharpInputHelper.MapKeyWithModifiers(DefaultMappingContext, MoveAction, Keys.A, new UInputModifier[] { Negate });
                USharpInputHelper.MapKey(DefaultMappingContext, MoveAction, Keys.D);
                USharpInputHelper.MapKey(DefaultMappingContext, MoveAction, Keys.Gamepad_Left2D);
                USharpInputHelper.MapKey(DefaultMappingContext, MoveAction, new FKey("Touch 1"));

                USharpInputHelper.MapKey(DefaultMappingContext, LookAction, Keys.MouseX);
                USharpInputHelper.MapKeyWithModifiers(DefaultMappingContext, LookAction, Keys.MouseY, new UInputModifier[] { SwizzleToY, Negate });
                USharpInputHelper.MapKey(DefaultMappingContext, LookAction, Keys.Gamepad_Right2D);
                USharpInputHelper.MapKey(DefaultMappingContext, LookAction, new FKey("Touch 2"));
            }

            //avatar mesh
            {
                UObject LoadedAsset = null;

                LoadedAsset = StaticLoadObject<UObject>("/Game/ParagonShinbi/Characters/Heroes/Shinbi/Shinbi_AnimBlueprint.Shinbi_AnimBlueprint_C");
                Mesh.AnimClass = LoadedAsset as UAnimBlueprintGeneratedClass;

                LoadedAsset = StaticLoadObject<UObject>("/Game/ParagonShinbi/Characters/Heroes/Shinbi/Meshes/Shinbi.Shinbi");
                Mesh.SkeletalMesh = LoadedAsset as USkeletalMesh;

                //see these in blueprint
                FTransform RelativeTransform = Mesh.RelativeTransform;
                RelativeTransform.Location.Z = -99.0f;
                // Rotation is an FQuat — must go through FRotator to avoid breaking normalization
                FRotator Rot = RelativeTransform.Rotation.ToRotator();
                Rot.Yaw = -90;
                RelativeTransform.Rotation = Rot.ToQuaternion;

                Mesh.SetRelativeTransform(RelativeTransform, false, out FHitResult HitResult, true);

            }

            //setup camera
            {
                CapsuleComponent.SetCapsuleSize(42.0f, 96.0f);

                // Don't rotate when the controller rotates. Let that just affect the camera.
                UseControllerRotationPitch = false;
                UseControllerRotationYaw = false;
                UseControllerRotationRoll = false;

                // Configure character movement
                CharacterMovement.OrientRotationToMovement = true; // Character moves in the direction of input...	

                CameraBoom.TargetArmLength = 400;
                CameraBoom.UsePawnControlRotation = true;
                FollowCamera.UsePawnControlRotation = false;
            }

            //setup input
            if (Subsystem!= null && DefaultMappingContext != null)
            {
                Subsystem.AddMappingContext(DefaultMappingContext, 0);
            }

            if (InputComponent != null && InputComponent is UEnhancedInputComponent)
            {
                UEnhancedInputComponent EnhancedInputComponent = InputComponent as UEnhancedInputComponent;

                EnhancedInputComponent.BindAction(JumpAction, ETriggerEvent.Started, HandleJumpAction);
                EnhancedInputComponent.BindAction(JumpAction, ETriggerEvent.Completed, HandleStopJumpAction);
                EnhancedInputComponent.BindAction(MoveAction, ETriggerEvent.Triggered, HandleMoveAction);
                EnhancedInputComponent.BindAction(LookAction, ETriggerEvent.Triggered, HandleLookAction);
            }

        }


        [UFunction]
        protected void HandleJumpAction(FInputActionValue Value, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            Jump();
        }

        [UFunction]
        protected void HandleStopJumpAction(FInputActionValue Value, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            StopJumping();
        }

        [UFunction]
        protected void HandleMoveAction(FInputActionValue Value, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            if (Controller == null) return;
            var mv = Value.GetAxis2D();
            FRotator Rot = Controller.ControlRotation;
            var Fwd = MathLibrary.RotateAngleAxis(FVector.Forward, (float)Rot.Yaw, FVector.Up);
            var Rgt = MathLibrary.RotateAngleAxis(FVector.Right, (float)Rot.Yaw, FVector.Up);
            AddMovementInput(Fwd, (float)mv.Y);
            AddMovementInput(Rgt, (float)mv.X);
        }

        [UFunction]
        protected void HandleLookAction(FInputActionValue Value, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            var lk = Value.GetAxis2D();
            AddControllerYawInput((float)lk.X);
            AddControllerPitchInput((float)lk.Y);
        }

    }
}
