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
	bool ConsumeClickedButton(FString& OutElementId);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	FString GetLastClickedButton() const { return LastClickedId; }

	/** Called by UJsonButton when the user clicks it. */
	void NotifyButtonClicked(const FString& ElementId);

	virtual void NativeTick(const FGeometry& MyGeometry, float InDeltaTime) override;

private:
	UWidget* BuildElement(const TSharedPtr<FJsonObject>& Element, UWidgetTree* Tree,
		const FVector2D& ParentOffset = FVector2D::ZeroVector);

	/** Button rectangles in widget-local design space (for manual click polling). */
	TMap<FString, FBox2D> ButtonRects;

	UPROPERTY()
	TMap<FString, TObjectPtr<UWidget>> ElementsById;

	FString LastClickedId;
	FString LastNotifiedId;
	double LastNotifySeconds = 0.0;
};
