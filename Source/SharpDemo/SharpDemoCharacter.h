// Copyright Epic Games, Inc. All Rights Reserved.

#pragma once

#include "CoreMinimal.h"
#include "GameFramework/Character.h"
#include "Logging/LogMacros.h"
#include "SharpDemoCharacter.generated.h"

class USpringArmComponent;
class UCameraComponent;
class UInputMappingContext;
class UInputAction;
struct FInputActionValue;

DECLARE_LOG_CATEGORY_EXTERN(LogTemplateCharacter, Log, All);

UCLASS(config=Game)
class ASharpDemoCharacter : public ACharacter
{
public:
	GENERATED_BODY()

	DECLARE_DYNAMIC_MULTICAST_SPARSE_DELEGATE_TwoParams(FOnTestSparseSignature, ASharpDemoCharacter, OnTestSparseEvent, float, FloatParam, int32, IntParam);

	virtual void Landed(const FHitResult& Hit) override;
	
	/** Called when the component has been activated, with parameter indicating if it was from a reset */
	UPROPERTY(BlueprintAssignable)
	FOnTestSparseSignature OnTestSparseEvent;

};

