// JsonButton.h — UButton that remembers the JSON element id and forwards clicks to
// its owning RuntimeJsonWidget. A per-button handler is needed because UButton's
// dynamic OnClicked delegate carries no sender information.
#pragma once

#include "CoreMinimal.h"
#include "Components/Button.h"
#include "JsonButton.generated.h"

class URuntimeJsonWidget;

UCLASS()
class RUNTIMEWIDGETLAYOUT_API UJsonButton : public UButton
{
	GENERATED_BODY()

public:
	UJsonButton();

	void InitJsonButton(URuntimeJsonWidget* InOwner, const FString& InElementId);

	UPROPERTY()
	TObjectPtr<URuntimeJsonWidget> OwnerWidget;

	UPROPERTY()
	FString ElementId;

	/** Binds OnClicked after the object is fully constructed (ctor binding is lost). */
	void BindClickHandler();

private:
	UFUNCTION()
	void HandleJsonButtonClicked();
};

// debugging hooks (temporary)
