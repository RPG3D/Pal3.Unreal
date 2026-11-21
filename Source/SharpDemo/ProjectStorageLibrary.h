#pragma once

#include "Kismet/BlueprintFunctionLibrary.h"
#include "ProjectStorageLibrary.generated.h"

UENUM(BlueprintType)
enum class EPal3FolderSelectionResult : uint8
{
	None,
	Selected,
	Cancelled,
	MissingData,
	Unavailable,
};

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

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static FString ResolveAuthorizedPal3Folder();

	/** Resolves a persisted folder grant owned by the caller's storage key. Empty uses the legacy grant. */
	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static FString ResolveAuthorizedFolder(const FString& StorageKey);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static void RequestPal3FolderSelection(const FString& RequiredFile);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static void RequestFolderSelection(const FString& StorageKey, const FString& RequiredFile);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Storage")
	static EPal3FolderSelectionResult ConsumePal3FolderSelectionResult();
};
