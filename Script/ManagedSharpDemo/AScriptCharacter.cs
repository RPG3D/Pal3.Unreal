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
                // Create Input Mapping Context
                DefaultMappingContext = NewObject<UInputMappingContext>();

                // Create Input Actions (ValueType defaults to Axis2D in UE)
                JumpAction = NewObject<UInputAction>();
                MoveAction = NewObject<UInputAction>();
                LookAction = NewObject<UInputAction>();

                // Map keys: keyboard + gamepad + touch
                // Jump
                DefaultMappingContext.MapKey(JumpAction, Keys.SpaceBar);
                DefaultMappingContext.MapKey(JumpAction, Keys.Gamepad_FaceButton_Bottom);

                // Move — per-key modifiers matching IMC asset
                {
                    UInputModifierSwizzleAxis SwizzleToY = NewObject<UInputModifierSwizzleAxis>();
                    SwizzleToY.Order = EInputAxisSwizzle.YXZ;

                    UInputModifierNegate NegX = NewObject<UInputModifierNegate>();
                    UInputModifierNegate NegY = NewObject<UInputModifierNegate>();

                    // W: Y=+1 (swizzle X→Y)
                    DefaultMappingContext.MapKey(MoveAction, Keys.W).Modifiers.Add(SwizzleToY);
                    // S: Y=-1 (swizzle X→Y + negate)
                    DefaultMappingContext.MapKey(MoveAction, Keys.S).Modifiers.Add(SwizzleToY);
                    DefaultMappingContext.MapKey(MoveAction, Keys.S).Modifiers.Add(NegY);
                    // A: X=-1 (negate X)
                    DefaultMappingContext.MapKey(MoveAction, Keys.A).Modifiers.Add(NegX);
                    // D: X=+1 (no modifier)
                    DefaultMappingContext.MapKey(MoveAction, Keys.D);
                }
                // Gamepad: 2D axis already provides correct X/Y
                DefaultMappingContext.MapKey(MoveAction, Keys.Gamepad_Left2D);
                DefaultMappingContext.MapKey(MoveAction, new FKey("Touch 1"));

                // Look
                DefaultMappingContext.MapKey(LookAction, Keys.MouseX);
                DefaultMappingContext.MapKey(LookAction, Keys.MouseY);
                DefaultMappingContext.MapKey(LookAction, Keys.Gamepad_Right2D);
                DefaultMappingContext.MapKey(LookAction, new FKey("Touch 2"));
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
        protected void HandleMoveAction(FInputActionValue InValue, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            FVector2D MovementVector = InValue.GetAxis2D();

            if (Controller != null)
            {
                //find out which way is forward
                FRotator Rot = Controller.ControlRotation;

                //get forward
                FVector Forward = MathLibrary.RotateAngleAxis(FVector.Forward, (float)Rot.Yaw, FVector.Up);
                FVector Right = MathLibrary.RotateAngleAxis(FVector.Right, (float)Rot.Yaw, FVector.Up);

                AddMovementInput(Forward, (float)MovementVector.Y);
                AddMovementInput(Right, (float)MovementVector.X);
            }
        }

        [UFunction]
        protected void HandleLookAction(FInputActionValue InValue, float DeltaTime, float WorldDeltaTime, UInputAction InputAction)
        {
            FVector2D LookAxisVector = InValue.GetAxis2D();
            if (Controller != null)
            {
                AddControllerYawInput((float)LookAxisVector.X);
                AddControllerPitchInput((float)LookAxisVector.Y);
            }
        }

    }
}
