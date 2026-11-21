#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "ProjectFileLibrary.generated.h"

/** Platform-file access, including files mounted inside a PAK. */
UCLASS()
class SHARPDEMO_API UProjectFileLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()
public:
	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Files")
	static bool ReadText(const FString& Path, FString& Text);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Files")
	static bool ReadBytes(const FString& Path, TArray<uint8>& Bytes);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Files")
	static bool WriteTextAtomic(const FString& Path, const FString& Text);

	UFUNCTION(BlueprintPure, Category = "SharpDemo|Files")
	static bool FileExists(const FString& Path);

	UFUNCTION(BlueprintPure, Category = "SharpDemo|Files")
	static FString GetProjectDirectory();

	UFUNCTION(BlueprintPure, Category = "SharpDemo|Files")
	static FString GetSavedDirectory();
};
