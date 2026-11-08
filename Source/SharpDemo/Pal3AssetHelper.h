// Pal3AssetHelper.h — runtime asset plumbing for the managed PAL3 port:
// RGBA-buffer → UTexture2D creation and texture-parameter binding on the
// M_Pal3Tex parent material. The decoders (DDS/TGA/BMP) live in managed code
// and hand over ready-to-use RGBA data.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Pal3AssetHelper.generated.h"

UCLASS()
class SHARPDEMO_API UPal3AssetHelper : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Creates a transient SRGB RGBA8 texture from raw pixel data (4 bytes/px). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static UTexture2D* CreateTextureFromRgba(int32 Width, int32 Height, const TArray<uint8>& Rgba);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Assets")
	static void ReleaseTransientAsset(UObject* Asset);

	UFUNCTION(BlueprintCallable, Category = "SharpDemo|Assets")
	static void RequestTransientAssetCollection();

	/** Creates a dynamic material instance of /Game/PAL3/M_Pal3Tex. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static UMaterialInterface* CreatePal3Material();

	/**
	 * Blend variants: 0 = opaque (M_Pal3Tex), 1 = additive (M_Pal3Tex_Add),
	 * 2 = translucent (M_Pal3Tex_Alpha). Same parameter names on all three.
	 */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static UMaterialInterface* CreatePal3MaterialBlend(int32 BlendMode);

	/** Binds a texture to a named parameter of M_Pal3Tex (Pal3Tex / ShadowTex). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static void BindTexture(UMaterialInterface* Material, FName ParameterName, UTexture2D* Texture);

	/** Sets a scalar parameter (HasShadowTex / ShadowExposure). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static void SetScalarParameter(UMaterialInterface* Material, FName ParameterName, float Value);

	/** Sets a vector parameter (TintColor). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static void SetVectorParameter(UMaterialInterface* Material, FName ParameterName, FLinearColor Value);

	/** Diagnostics: (textures created, texture binds). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static FString GetAssetStats();

	/** Execs HighResShot via the local player controller viewport (screenshot). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Assets")
	static void TakeScreenshot(int32 Width, int32 Height);
};
