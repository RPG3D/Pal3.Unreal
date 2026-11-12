#include "RuntimeBinkVideoWidget.h"

#include "GameFramework/PlayerController.h"
#include "HAL/PlatformFileManager.h"
#include "Kismet/GameplayStatics.h"
#include "MediaPlayer.h"
#include "MediaSoundComponent.h"
#include "MediaTexture.h"
#include "Misc/Paths.h"
#include "Modules/ModuleManager.h"
#include "Styling/CoreStyle.h"
#include "Widgets/Images/SImage.h"
#include "Widgets/Layout/SScaleBox.h"
#include "Widgets/SOverlay.h"

IMPLEMENT_MODULE(FDefaultModuleImpl, RuntimeBinkVideo)

URuntimeBinkVideoWidget* URuntimeBinkVideoWidget::OpenVideo(UObject* WorldContextObject, const FString& Path, int32 ZOrder,
	FVector2D VerticalUvRange, bool bFillViewport)
{
	FString PlaybackPath = Path;
#if PLATFORM_IOS
	// The iOS platform file accepts /var/ as an absolute sandbox path.
	if (PlaybackPath.StartsWith(TEXT("/private/var/"), ESearchCase::CaseSensitive))
	{
		PlaybackPath.RightChopInline(8, EAllowShrinking::No);
	}
#endif
	APlayerController* Controller = UGameplayStatics::GetPlayerController(WorldContextObject, 0);
	if (!Controller || PlaybackPath.IsEmpty() || FPaths::IsRelative(PlaybackPath) ||
		!FPlatformFileManager::Get().GetPlatformFile().FileExists(*PlaybackPath))
	{
		return nullptr;
	}

	auto* Widget = CreateWidget<URuntimeBinkVideoWidget>(Controller);
	if (!Widget) return nullptr;
	Widget->bFillViewport = bFillViewport;
	if (VerticalUvRange.X >= 0 && VerticalUvRange.Y <= 1 && VerticalUvRange.X < VerticalUvRange.Y)
	{
		Widget->VisibleHeight = VerticalUvRange.Y - VerticalUvRange.X;
		Widget->VideoBrush.SetUVRegion(FBox2d(FVector2D(0, VerticalUvRange.X), FVector2D(1, VerticalUvRange.Y)));
	}

	Widget->Player = NewObject<UMediaPlayer>(Widget);
	Widget->Player->PlayOnOpen = true;
#if PLATFORM_ANDROID
	if (FPaths::GetExtension(PlaybackPath).Equals(TEXT("mp4"), ESearchCase::IgnoreCase))
	{
		Widget->Player->SetDesiredPlayerName(FName(TEXT("ElectraPlayer")));
	}
#endif
	Widget->Player->OnMediaOpened.AddDynamic(Widget, &URuntimeBinkVideoWidget::HandleOpened);
	Widget->Player->OnMediaOpenFailed.AddDynamic(Widget, &URuntimeBinkVideoWidget::HandleOpenFailed);
	Widget->Player->OnEndReached.AddDynamic(Widget, &URuntimeBinkVideoWidget::HandleEnded);

	Widget->Texture = NewObject<UMediaTexture>(Widget);
	Widget->Texture->NewStyleOutput = true;
	Widget->Texture->SetMediaPlayer(Widget->Player);
	Widget->Texture->UpdateResource();
	Widget->VideoBrush.SetResourceObject(Widget->Texture);
	Widget->VideoBrush.SetImageSize(FVector2D(16, 9));

	Widget->Sound = NewObject<UMediaSoundComponent>(Widget);
	Widget->Sound->SetMediaPlayer(Widget->Player);
	Widget->Sound->RegisterComponentWithWorld(Controller->GetWorld());

	Widget->PlaybackActive = true;
	Widget->OpenDeadline = FPlatformTime::Seconds() + 12.0;
	Widget->SetVisibility(ESlateVisibility::HitTestInvisible);
	Widget->AddToViewport(ZOrder);
	if (!Widget->Player->OpenFile(PlaybackPath))
	{
		Widget->CloseVideo();
		return nullptr;
	}
	return Widget;
}

TSharedRef<SWidget> URuntimeBinkVideoWidget::RebuildWidget()
{
	return SNew(SOverlay).Clipping(EWidgetClipping::ClipToBounds)
		+ SOverlay::Slot()
		[
			SNew(SImage).Image(FCoreStyle::Get().GetBrush("WhiteBrush")).ColorAndOpacity(FLinearColor::Black)
		]
		+ SOverlay::Slot()
		[
			SNew(SScaleBox).Stretch(bFillViewport ? EStretch::ScaleToFill : EStretch::ScaleToFit)
			[
				SAssignNew(VideoImage, SImage).Image(&VideoBrush)
			]
		];
}

void URuntimeBinkVideoWidget::ReleaseSlateResources(bool bReleaseChildren)
{
	Super::ReleaseSlateResources(bReleaseChildren);
	VideoImage.Reset();
}

void URuntimeBinkVideoWidget::NativeTick(const FGeometry& Geometry, float DeltaTime)
{
	Super::NativeTick(Geometry, DeltaTime);
	if (PlaybackActive && ((OpenDeadline > 0 && FPlatformTime::Seconds() >= OpenDeadline) ||
		(Player && Player->HasError())))
	{
		HandleOpenFailed(Player ? Player->GetUrl() : FString());
	}
}

void URuntimeBinkVideoWidget::HandleOpened(FString Url)
{
	OpenDeadline = 0;
	const int32 Track = FMath::Max(0, Player->GetSelectedTrack(EMediaPlayerTrack::Video));
	const int32 Format = FMath::Max(0, Player->GetTrackFormat(EMediaPlayerTrack::Video, Track));
	const FIntPoint Size = Player->GetVideoTrackDimensions(Track, Format);
	if (Size.X > 0 && Size.Y > 0)
	{
		VideoBrush.SetImageSize(FVector2D(Size.X, Size.Y * VisibleHeight));
		if (VideoImage) VideoImage->Invalidate(EInvalidateWidgetReason::Layout);
	}
	UE_LOG(LogTemp, Display, TEXT("RuntimeBinkVideo opened %s"), *Url);
}

void URuntimeBinkVideoWidget::HandleOpenFailed(FString Url)
{
	PlaybackFailed = true;
	PlaybackActive = false;
	UE_LOG(LogTemp, Warning, TEXT("RuntimeBinkVideo failed to play %s"), *Url);
}

void URuntimeBinkVideoWidget::HandleEnded()
{
	PlaybackActive = false;
	UE_LOG(LogTemp, Display, TEXT("RuntimeBinkVideo reached end"));
}

void URuntimeBinkVideoWidget::CloseVideo()
{
	RemoveFromParent();
	// Destruct can be deferred while Slate still holds the widget.
	ReleasePlayer();
}

void URuntimeBinkVideoWidget::NativeDestruct()
{
	ReleasePlayer();
	Super::NativeDestruct();
}

void URuntimeBinkVideoWidget::ReleasePlayer()
{
	PlaybackActive = false;
	if (Sound)
	{
		Sound->DestroyComponent();
		Sound = nullptr;
	}
	if (Player)
	{
		Player->OnMediaOpened.RemoveAll(this);
		Player->OnMediaOpenFailed.RemoveAll(this);
		Player->OnEndReached.RemoveAll(this);
		Player->Close();
		Player = nullptr;
	}
	if (Texture) Texture->SetMediaPlayer(nullptr);
}
