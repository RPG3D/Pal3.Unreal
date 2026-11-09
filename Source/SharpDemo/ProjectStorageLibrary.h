#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "ProjectStorageLibrary.generated.h"

UCLASS()
class SHARPDEMO_API UProjectStorageLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static FString ResolveWritableSavedDirectory();

	/** Reads a float from the merged Engine configuration for the running platform. */
	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Config")
	static float GetEngineConfigFloat(const FString& Section, const FString& Key, float DefaultValue);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static bool HasSharedStorageAccess();

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static bool RequestSharedStorageAccess();
};
