// Generic engine inspection endpoints. The legacy class name keeps existing
// Remote Control actor queries working.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Pal3DebugOverlay.generated.h"

class UProceduralMeshComponent;

UCLASS()
class SHARPDEMO_API UPal3DebugOverlay : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static void ExecConsoleCommand(const FString& Command);

	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static FString DumpActors(int32 MaxCount = 150);

	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static FString FindActors(const FString& NameContains, int32 MaxCount = 40);

	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static FString DumpActorPositions(const FString& ClassContains, int32 MaxCount = 150);

	/** Full object paths (usable as Remote Control object paths) matching a name/class substring. */
	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static FString FindActorPaths(const FString& NameContains, int32 MaxCount = 40);

	/** Fingerprint of the local-space positions stored in a procedural mesh section. */
	UFUNCTION(BlueprintCallable, Category = "Diagnostics")
	static int64 FingerprintMeshSection(UProceduralMeshComponent* Mesh, int32 SectionIndex, int32& VertexCount);

	// Pass arrays by value so UnrealSharp releases the reflected argument storage.
	UFUNCTION(BlueprintCallable, Category = "Procedural Mesh")
	static void CreateProceduralSection(UProceduralMeshComponent* Mesh, int32 SectionIndex,
		TArray<FVector> Vertices, TArray<int32> Triangles, TArray<FVector> Normals,
		TArray<FVector2D> UV0, TArray<FVector2D> UV1, bool bCreateCollision);

	UFUNCTION(BlueprintCallable, Category = "Procedural Mesh")
	static void UpdateProceduralVertices(UProceduralMeshComponent* Mesh, int32 SectionIndex, TArray<FVector> Vertices);

#if WITH_EDITOR
	// --- Editor-only automation (Remote Control driven verification) ---
	/** Starts a Play-In-Editor session for the current level. */
	UFUNCTION(BlueprintCallable, Category = "Diagnostics|Editor")
	static void StartPieSession();

	/** Requests the running PIE session to end. */
	UFUNCTION(BlueprintCallable, Category = "Diagnostics|Editor")
	static void StopPieSession();
#endif

private:
	static UWorld* FindGameWorld();
};
