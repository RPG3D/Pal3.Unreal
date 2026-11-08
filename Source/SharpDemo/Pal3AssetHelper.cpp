// Pal3AssetHelper.cpp
#include "Pal3AssetHelper.h"
#include "Engine/Texture2D.h"
#include "Engine/GameViewportClient.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Misc/OutputDevice.h"
#include "UnrealClient.h" 
#include "Materials/MaterialInstanceDynamic.h"
#include "Misc/EngineVersionComparison.h"

static int32 GTexturesCreated = 0;
static int32 GTextureBinds = 0;

// Rooted PAL3 assets. The managed side only holds wrappers; the UObjects are
// created with GetTransientPackage() as Outer and would otherwise be GC'd (or
// purged at PIE stop) while the render thread still has commands queued for them:
//   "Cannot queue the Expression Cache for Material MID_... when it is about to be deleted"
// Keeping them rooted for the process lifetime is the safe fix for the dev port
// (memory is bounded by the number of materials/textures the game creates).
static TArray<TObjectPtr<UObject>> GPal3RootedAssets;

static void RootPal3Asset(UObject* Object)
{
	if (Object == nullptr || Object->IsRooted())
	{
		return;
	}
	Object->AddToRoot();
	GPal3RootedAssets.Add(Object);
}

void UPal3AssetHelper::TakeScreenshot(int32 Width, int32 Height)
{
	if (GEngine && GEngine->GameViewport)
	{
		FString Cmd = FString::Printf(TEXT("HighResShot %dx%d"), Width, Height);
		GEngine->GameViewport->Exec(nullptr, *Cmd, *GLog);
	}
}

FString UPal3AssetHelper::GetAssetStats()
{
	return FString::Printf(TEXT("texCreated=%d binds=%d rooted=%d"),
		GTexturesCreated, GTextureBinds, GPal3RootedAssets.Num());
}

UTexture2D* UPal3AssetHelper::CreateTextureFromRgba(int32 Width, int32 Height, const TArray<uint8>& Rgba)
{
	if (Width <= 0 || Height <= 0 || Rgba.Num() < Width * Height * 4)
	{
		return nullptr;
	}

	// UE7 official dynamic-texture API (handles platform data, mips, resource update).
	UTexture2D* Texture = UTexture2D::CreateTransient(
		Width, Height, PF_R8G8B8A8, NAME_None,
		TConstArrayView64<uint8>(Rgba.GetData(), (int64)Rgba.Num()));
	if (Texture)
	{
		Texture->SRGB = true;
		Texture->NeverStream = true;
		Texture->UpdateResource();
		RootPal3Asset(Texture);
	}
	else
	{
		UE_LOG(LogTemp, Warning, TEXT("[Pal3Tex] CreateTransient returned null for %dx%d"), Width, Height);
	}
	++GTexturesCreated;
	return Texture;
}

UMaterialInterface* UPal3AssetHelper::CreatePal3Material()
{
	return CreatePal3MaterialBlend(0);
}

UMaterialInterface* UPal3AssetHelper::CreatePal3MaterialBlend(int32 BlendMode)
{
	const TCHAR* Path = TEXT("/Game/Pal3/M_Pal3Tex.M_Pal3Tex");
	if (BlendMode == 1)
	{
		Path = TEXT("/Game/Pal3/M_Pal3Tex_Add.M_Pal3Tex_Add");
	}
	else if (BlendMode == 2)
	{
		Path = TEXT("/Game/Pal3/M_Pal3Tex_Alpha.M_Pal3Tex_Alpha");
	}
	else if (BlendMode == 3)
	{
		// Alpha cutout: the game sets _Cutout=0.3 for materials whose texture alpha
		// masks the shape (Mv3 hair/clothes); without it the transparent texels show
		// up as the black/white patches on the characters.
		Path = TEXT("/Game/Pal3/M_Pal3Tex_Masked.M_Pal3Tex_Masked");
	}

	UMaterialInterface* Parent = LoadObject<UMaterial>(nullptr, Path);
	if (Parent == nullptr)
	{
		UE_LOG(LogTemp, Warning, TEXT("[Pal3Mat] parent missing (%d): %s"), BlendMode, Path);
		return nullptr;
	}
	UMaterialInstanceDynamic* Mid = UMaterialInstanceDynamic::Create(Parent, GetTransientPackage());
	RootPal3Asset(Mid);
	return Mid;
}

void UPal3AssetHelper::BindTexture(UMaterialInterface* Material, FName ParameterName, UTexture2D* Texture)
{
	UMaterialInstanceDynamic* Mid = Cast<UMaterialInstanceDynamic>(Material);
	if (Mid && Texture)
	{
		++GTextureBinds;
		Mid->SetTextureParameterValue(ParameterName, Texture);
		if (GTextureBinds <= 6)
		{
			UE_LOG(LogTemp, Display, TEXT("[Pal3Mat] bind %s mid=%s tex=%s %dx%d format=%d"),
				*ParameterName.ToString(), *Mid->GetName(), *Texture->GetName(), Texture->GetSizeX(), Texture->GetSizeY(),
				(int32)Texture->GetPixelFormat());
		}
	}
}

void UPal3AssetHelper::SetScalarParameter(UMaterialInterface* Material, FName ParameterName, float Value)
{
	if (UMaterialInstanceDynamic* Mid = Cast<UMaterialInstanceDynamic>(Material))
	{
		Mid->SetScalarParameterValue(ParameterName, Value);
	}
}

void UPal3AssetHelper::SetVectorParameter(UMaterialInterface* Material, FName ParameterName, FLinearColor Value)
{
	if (UMaterialInstanceDynamic* Mid = Cast<UMaterialInstanceDynamic>(Material))
	{
		Mid->SetVectorParameterValue(ParameterName, Value);
	}
}
