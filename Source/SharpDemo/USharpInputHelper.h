// USharpInputHelper.h — UFUNCTION bridge for IMC.MapKey with per-key modifiers
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "USharpInputHelper.generated.h"

class UInputMappingContext;
class UInputAction;
class UInputModifier;
struct FKey;

UCLASS()
class SHARPDEMO_API USharpInputHelper : public UBlueprintFunctionLibrary
{
    GENERATED_BODY()
public:
    /// IMC.MapKey(Action, Key) without modifiers.
    UFUNCTION(BlueprintCallable, Category = "SharpDemo|Input")
    static void MapKey(
        UInputMappingContext* IMC,
        UInputAction* Action,
        FKey Key);

    /// IMC.MapKey(Action, Key) + Modifiers. Workaround for UnrealSharp struct-copy.
    UFUNCTION(BlueprintCallable, Category = "SharpDemo|Input")
    static void MapKeyWithModifiers(
        UInputMappingContext* IMC,
        UInputAction* Action,
        FKey Key,
        const TArray<UInputModifier*>& Modifiers);
};
