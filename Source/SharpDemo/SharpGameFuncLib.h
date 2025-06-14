// Fill out your copyright notice in the Description page of Project Settings.

#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "SharpGameFuncLib.generated.h"

/**
 * 
 */
UCLASS()
class SHARPDEMO_API USharpGameFuncLib : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()
	
public:

	UFUNCTION(BlueprintCallable)
	static UObject* LoadAsset(const FString& InAssetPath);

};
