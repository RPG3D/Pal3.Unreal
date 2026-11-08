// JsonWidgetLibrary.cpp
#include "JsonWidgetLibrary.h"
#include "JsonWidget.h"

#include "Blueprint/UserWidget.h"
#include "Engine/Engine.h"
#include "Engine/World.h"
#include "GameFramework/PlayerController.h"
#include "Kismet/GameplayStatics.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

namespace
{
URuntimeJsonWidget* CreateWidgetInViewport(UObject* WorldContextObject,
	const FString& Json, int32 ZOrder, bool bInteractive)
{
	UWorld* World = GEngine ? GEngine->GetWorldFromContextObject(WorldContextObject,
		EGetWorldErrorMode::LogAndReturnNull) : nullptr;
	APlayerController* PC = World ? UGameplayStatics::GetPlayerController(World, 0) : nullptr;
	if (PC == nullptr)
	{
		UE_LOG(LogTemp, Warning, TEXT("[RuntimeWidgetLayout] no player controller for JSON widget"));
		return nullptr;
	}

	URuntimeJsonWidget* Widget = CreateWidget<URuntimeJsonWidget>(PC, URuntimeJsonWidget::StaticClass());
	if (Widget == nullptr)
	{
		return nullptr;
	}

	if (!Widget->BuildFromJson(Json))
	{
		UE_LOG(LogTemp, Warning, TEXT("[RuntimeWidgetLayout] JSON build failed"));
		return nullptr;
	}

	Widget->AddToViewport(ZOrder);
	if (!bInteractive)
	{
		Widget->SetVisibility(ESlateVisibility::HitTestInvisible);
		return Widget;
	}

	// Game viewports swallow mouse input by default; switch to a UI-friendly mode so
	// JSON buttons receive clicks (the port has no other UI input path yet).
	PC->bShowMouseCursor = true;
	FInputModeGameAndUI InputMode;
	InputMode.SetLockMouseToViewportBehavior(EMouseLockMode::DoNotLock);
	InputMode.SetHideCursorDuringCapture(false);
	// Give the widget Slate focus; without it PIE keeps routing the mouse to the
	// game viewport instead of the UMG tree.
	InputMode.SetWidgetToFocus(Widget->TakeWidget());
	PC->SetInputMode(InputMode);

	UE_LOG(LogTemp, Display, TEXT("[RuntimeWidgetLayout] widget added: pc=%s cursor=%d zorder=%d path=%s"),
		*PC->GetName(), PC->bShowMouseCursor ? 1 : 0, ZOrder, *Widget->GetPathName());
	return Widget;
}
}

URuntimeJsonWidget* URuntimeJsonWidgetLibrary::CreateJsonWidgetFromFile(UObject* WorldContextObject,
	const FString& Path, int32 ZOrder, bool bInteractive)
{
	FString Json;
	const FString Filename = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir(), Path);
	if (Path.IsEmpty() || !FFileHelper::LoadFileToString(Json, *Filename))
	{
		UE_LOG(LogTemp, Error, TEXT("[RuntimeWidgetLayout] cannot read layout: %s"), *Filename);
		return nullptr;
	}
	return CreateWidgetInViewport(WorldContextObject, Json, ZOrder, bInteractive);
}

URuntimeJsonWidget* URuntimeJsonWidgetLibrary::CreateJsonWidget(UObject* WorldContextObject,
	const FString& Json, int32 ZOrder)
{
	return CreateWidgetInViewport(WorldContextObject, Json, ZOrder, true);
}

URuntimeJsonWidget* URuntimeJsonWidgetLibrary::CreatePassiveJsonWidget(UObject* WorldContextObject,
	const FString& Json, int32 ZOrder)
{
	return CreateWidgetInViewport(WorldContextObject, Json, ZOrder, false);
}

void URuntimeJsonWidgetLibrary::RemoveJsonWidget(URuntimeJsonWidget* Widget)
{
	if (Widget != nullptr)
	{
		Widget->RemoveFromParent();
	}
}

void URuntimeJsonWidgetLibrary::SetJsonWidgetVisible(URuntimeJsonWidget* Widget, bool bVisible)
{
	if (Widget != nullptr)
	{
		Widget->SetVisibility(bVisible ? ESlateVisibility::SelfHitTestInvisible : ESlateVisibility::Collapsed);
	}
}
