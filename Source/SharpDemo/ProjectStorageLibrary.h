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

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static bool HasSharedStorageAccess();

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static bool RequestSharedStorageAccess();
};
