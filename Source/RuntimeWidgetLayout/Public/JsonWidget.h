// RuntimeJsonWidget.h — UUserWidget built from a JSON layout description.
//
// Initial element set: canvas, border, text, button, progressbar. All coordinates
// are in slate/design pixels (a 1920x1080 design space maps 1:1 at 1080p).
#pragma once

#include "CoreMinimal.h"
#include "Blueprint/UserWidget.h"
#include "JsonWidget.generated.h"

class UWidgetTree;
class UWidget;
class UTexture2D;
class FJsonObject;

UCLASS()
class RUNTIMEWIDGETLAYOUT_API URuntimeJsonWidget : public UUserWidget
{
	GENERATED_BODY()

public:
	/** Builds the widget tree from the JSON layout. Returns false on parse errors. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool BuildFromJson(const FString& Json);

	/** Returns true once per click and stores the JSON element id in OutElementId. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool ConsumeClickedButton(FName& OutElementId);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetElementText(const FString& ElementId, const FString& Text);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetElementProgress(const FString& ElementId, float Percent);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetElementVisible(const FString& ElementId, bool bVisible);

	/** Returns an element's laid-out bounds in viewport pixels. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool TryGetElementViewportRect(const FString& ElementId, FVector2D& Origin, FVector2D& Size) const;

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetElementRenderTranslation(const FString& ElementId, FVector2D Translation);

	/** Selects a direct child by id, independent of its order in the JSON. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetSwitcherPage(const FString& SwitcherId, const FString& PageId);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	bool SetElementImage(const FString& ElementId, UTexture2D* Texture,
		float U0 = 0.0f, float V0 = 0.0f, float U1 = 1.0f, float V1 = 1.0f);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	FName GetLastClickedButton() const { return FName(*LastClickedId); }

	/** Called by UJsonButton when the user clicks it. */
	void NotifyButtonClicked(const FString& ElementId);

	virtual void NativeTick(const FGeometry& MyGeometry, float InDeltaTime) override;
	virtual FReply NativeOnPreviewKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent) override;

private:
	UWidget* BuildElement(const TSharedPtr<FJsonObject>& Element, UWidgetTree* Tree);

	UPROPERTY()
	TMap<FString, TObjectPtr<UWidget>> ElementsById;
	TMap<FName, FString> KeyActions;

	FString LastClickedId;
	FString LastNotifiedId;
	double LastNotifySeconds = 0.0;
};
