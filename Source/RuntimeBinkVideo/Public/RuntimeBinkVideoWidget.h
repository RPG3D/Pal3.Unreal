#pragma once

#include "Blueprint/UserWidget.h"
#include "Styling/SlateBrush.h"
#include "RuntimeBinkVideoWidget.generated.h"

class SImage;
class UMediaPlayer;
class UMediaSoundComponent;
class UMediaTexture;

/** Fullscreen player for MP4 files prepared from Bink sources. */
UCLASS()
class RUNTIMEBINKVIDEO_API URuntimeBinkVideoWidget : public UUserWidget
{
	GENERATED_BODY()

public:
	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo", meta = (WorldContext = "WorldContextObject"))
	static URuntimeBinkVideoWidget* OpenVideo(UObject* WorldContextObject, const FString& Path, int32 ZOrder,
		FVector2D VerticalUvRange, bool bFillViewport);

	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo")
	void CloseVideo();

	UPROPERTY(BlueprintReadOnly, Category = "RuntimeBinkVideo")
	bool PlaybackActive = false;

	UPROPERTY(BlueprintReadOnly, Category = "RuntimeBinkVideo")
	bool PlaybackFailed = false;

protected:
	virtual TSharedRef<SWidget> RebuildWidget() override;
	virtual void ReleaseSlateResources(bool bReleaseChildren) override;
	virtual void NativeTick(const FGeometry& Geometry, float DeltaTime) override;
	virtual void NativeDestruct() override;

private:
	void ReleasePlayer();
	UFUNCTION()
	void HandleOpened(FString Url);
	UFUNCTION()
	void HandleOpenFailed(FString Url);
	UFUNCTION()
	void HandleEnded();

	UPROPERTY()
	TObjectPtr<UMediaPlayer> Player;
	UPROPERTY()
	TObjectPtr<UMediaTexture> Texture;
	UPROPERTY()
	TObjectPtr<UMediaSoundComponent> Sound;
	FSlateBrush VideoBrush;
	TSharedPtr<SImage> VideoImage;
	float VisibleHeight = 1.f;
	bool bFillViewport = false;
	double OpenDeadline = 0;
};
