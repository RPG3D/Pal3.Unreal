#include "Pal3DebugOverlay.h"

#include "Engine/Engine.h"
#include "EngineUtils.h"
#include "GameFramework/PlayerController.h"
#include "ProceduralMeshComponent.h"

void UPal3DebugOverlay::CreateProceduralSection(UProceduralMeshComponent* Mesh, int32 SectionIndex,
	TArray<FVector> Vertices, TArray<int32> Triangles, TArray<FVector> Normals,
	TArray<FVector2D> UV0, TArray<FVector2D> UV1, bool bCreateCollision)
{
	if (IsValid(Mesh) && SectionIndex >= 0)
	{
		Mesh->CreateMeshSection(SectionIndex, Vertices, Triangles, Normals, UV0, UV1, {}, {}, {}, {}, bCreateCollision);
	}
}

void UPal3DebugOverlay::UpdateProceduralVertices(UProceduralMeshComponent* Mesh, int32 SectionIndex, TArray<FVector> Vertices)
{
	if (IsValid(Mesh) && SectionIndex >= 0)
	{
		Mesh->UpdateMeshSection(SectionIndex, Vertices, {}, {}, {}, {}, {}, {}, {});
	}
}

int64 UPal3DebugOverlay::FingerprintMeshSection(UProceduralMeshComponent* Mesh, int32 SectionIndex, int32& VertexCount)
{
	VertexCount = 0;
	const FProcMeshSection* Section = IsValid(Mesh) ? Mesh->GetProcMeshSection(SectionIndex) : nullptr;
	if (!Section || Section->ProcVertexBuffer.IsEmpty())
	{
		return 0;
	}

	uint32 Hash = 0;
	for (const FProcMeshVertex& Vertex : Section->ProcVertexBuffer)
	{
		Hash = HashCombineFast(Hash, GetTypeHash(Vertex.Position));
	}
	VertexCount = Section->ProcVertexBuffer.Num();
	return Hash;
}

void UPal3DebugOverlay::ExecConsoleCommand(const FString& Command)
{
	if (GEngine && GEngine->GameViewport && !Command.IsEmpty())
	{
		GEngine->GameViewport->Exec(nullptr, *Command, *GLog);
	}
}

UWorld* UPal3DebugOverlay::FindGameWorld()
{
	if (!GEngine)
	{
		return nullptr;
	}

	// -game can expose several contexts (empty/editor-preview worlds included).
	// Prefer the one that actually owns a game instance, and among those the one
	// with the most actors (the real play world).
	UWorld* Best = nullptr;
	int32 BestActors = -1;
	for (const FWorldContext& Context : GEngine->GetWorldContexts())
	{
		UWorld* Candidate = Context.World();
		if (Candidate == nullptr)
		{
			continue;
		}
		if (Context.WorldType != EWorldType::PIE && Context.WorldType != EWorldType::Game)
		{
			continue;
		}
		if (Candidate->GetGameInstance() == nullptr)
		{
			continue;
		}
		int32 Count = 0;
		for (TActorIterator<AActor> It(Candidate); It; ++It)
		{
			++Count;
		}
		if (Count > BestActors)
		{
			BestActors = Count;
			Best = Candidate;
		}
	}
	return Best;
}

static FString DescribeActor(const AActor* Actor)
{
	const FVector Location = Actor->GetActorLocation();
	const FRotator Rotation = Actor->GetActorRotation();
	return FString::Printf(TEXT("%s | %s | loc=(%.0f,%.0f,%.0f) | rot=(P%.0f,Y%.0f,R%.0f)"),
		*Actor->GetName(),
		*Actor->GetClass()->GetName(),
		Location.X, Location.Y, Location.Z,
		Rotation.Pitch, Rotation.Yaw, Rotation.Roll);
}

FString UPal3DebugOverlay::DumpActors(int32 MaxCount)
{
	// [build:v2] marker lets the client confirm the running binary is current.
	// Diagnostic: list every world context with its actor count first, so the
	// caller can see which world the managed layer is actually spawning into.
	FString Summary;
	for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
	{
		if (Ctx.World() == nullptr) continue;
		int32 CtxCount = 0;
		for (TActorIterator<AActor> It(Ctx.World()); It; ++It) { ++CtxCount; }
		Summary += FString::Printf(TEXT("[ctx] world=%s type=%d actors=%d\n"),
			*Ctx.World()->GetName(), static_cast<int32>(Ctx.WorldType), CtxCount);
	}

	UWorld* World = FindGameWorld();
	if (!World)
	{
		return Summary + TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	int32 Total = 0;
	for (TActorIterator<AActor> It(World); It; ++It, ++Total)
	{
		if (Count >= MaxCount)
		{
			continue;
		}
		Out += DescribeActor(*It) + TEXT("\n");
		++Count;
	}

	return FString(TEXT("[build:v2]\n")) + Summary
		+ FString::Printf(TEXT("world=%s type=%d actors=%d (shown %d)\n%s"),
			*World->GetName(), static_cast<int32>(World->WorldType), Total, Count, *Out);
}

FString UPal3DebugOverlay::FindActors(const FString& NameContains, int32 MaxCount)
{
	UWorld* World = FindGameWorld();
	if (!World)
	{
		return TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		const FString Name = It->GetName();
		const FString ClassName = It->GetClass()->GetName();
		if (!Name.Contains(NameContains) && !ClassName.Contains(NameContains))
		{
			continue;
		}
		Out += DescribeActor(*It) + TEXT("\n");
		if (++Count >= MaxCount)
		{
			break;
		}
	}

	return Out.IsEmpty() ? FString::Printf(TEXT("<no actor matching '%s'>"), *NameContains) : Out;
}

FString UPal3DebugOverlay::DumpActorPositions(const FString& ClassContains, int32 MaxCount)
{
	UWorld* World = FindGameWorld();
	if (!World)
	{
		return TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		if (!It->GetClass()->GetName().Contains(ClassContains))
		{
			continue;
		}
		const FVector Location = It->GetActorLocation();
		Out += FString::Printf(TEXT("%s %.1f %.1f %.1f\n"), *It->GetName(), Location.X, Location.Y, Location.Z);
		if (++Count >= MaxCount)
		{
			break;
		}
	}
	return Out;
}
