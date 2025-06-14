using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnrealSharp;
using UnrealSharp.Attributes;
using UnrealSharp.Core.Attributes;
using UnrealSharp.CoreUObject;
using UnrealSharp.Engine;
using UnrealSharp.EnhancedInput;
using UnrealSharp.SharpDemo;
using UnrealSharp.UnrealSharpCore;


namespace ManagedSharpDemo
{
    /// <summary>
    /// copy from SharpDemoCharacter.cpp
    /// </summary>
    [UClass]
    internal class ASharpThirdPersonCharacter : ACharacter
    {
        [UProperty(DefaultComponent = true, AttachmentComponent = nameof(RootComponent), Category = "Camera")]
        protected USpringArmComponent CameraBoom { get; set; }

        [UProperty(DefaultComponent = true, AttachmentComponent = nameof(CameraBoom), AttachmentSocket = "SpringEndpoint", Category = "Camera")]
        protected UCameraComponent FollowCamera { get; set; }

        [UProperty(Category = "Input")]
        UInputMappingContext DefaultMappingContext { get; set; }

        [UProperty(Category = "Input")]
        UInputAction JumpAction { get; set; }

        [UProperty(Category = "Input")]
        UInputAction MoveAction { get; set; }

        [UProperty(Category = "Input")]
        UInputAction LookAction { get; set; }

        protected override void BeginPlay()
        {
            base.BeginPlay();

            Console.WriteLine("ASharpThirdPersonCharacter.BeginPlay()");

            APlayerController PlayerController = Controller as APlayerController;
            UEnhancedInputLocalPlayerSubsystem Subsystem = (PlayerController.Player as ULocalPlayer).GetLocalPlayerSubsystem<UEnhancedInputLocalPlayerSubsystem>(PlayerController);

            //input config
            {

                UObject LoadedAsset = null;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/Input/IMC_Default.IMC_Default");
                DefaultMappingContext = LoadedAsset as UInputMappingContext;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/Input/Actions/IA_Jump.IA_Jump");
                JumpAction = LoadedAsset as UInputAction;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/Input/Actions/IA_Move.IA_Move");
                MoveAction = LoadedAsset as UInputAction;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/Input/Actions/IA_Look.IA_Look");
                LookAction = LoadedAsset as UInputAction;

                
            }

            //avatar mesh
            {
                UObject LoadedAsset = null;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/ParagonShinbi/Characters/Heroes/Shinbi/Shinbi_AnimBlueprint.Shinbi_AnimBlueprint_C");
                Mesh.AnimClass = LoadedAsset as UAnimBlueprintGeneratedClass;

                LoadedAsset = USharpGameFuncLib.LoadAsset("/Game/ParagonShinbi/Characters/Heroes/Shinbi/Meshes/Shinbi.Shinbi");
                Mesh.SkeletalMesh = LoadedAsset as USkeletalMesh;

                //see these in blueprint
                FTransform RelativeTransform = Mesh.RelativeTransform;
                RelativeTransform.Location.Z = -99.0f;
                RelativeTransform.Rotation.Z = -1;
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
