// Copyright Epic Games, Inc. All Rights Reserved.

#include "SharpDemoCharacter.h"
#include "Engine/LocalPlayer.h"
#include "Camera/CameraComponent.h"
#include "Components/CapsuleComponent.h"
#include "GameFramework/CharacterMovementComponent.h"
#include "GameFramework/SpringArmComponent.h"
#include "GameFramework/Controller.h"
#include "EnhancedInputComponent.h"
#include "EnhancedInputSubsystems.h"
#include "InputActionValue.h"

DEFINE_LOG_CATEGORY(LogTemplateCharacter);

//////////////////////////////////////////////////////////////////////////
// ASharpDemoCharacter

void ASharpDemoCharacter::Landed(const FHitResult& Hit)
{
	Super::Landed(Hit);

	OnTestSparseEvent.Broadcast(3.14f, 42);
}
