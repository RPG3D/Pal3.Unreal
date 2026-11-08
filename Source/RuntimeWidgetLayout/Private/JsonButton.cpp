// JsonButton.cpp
#include "JsonButton.h"
#include "JsonWidget.h"

UJsonButton::UJsonButton()
{
}

void UJsonButton::BindClickHandler()
{
	OnClicked.AddUniqueDynamic(this, &UJsonButton::HandleJsonButtonClicked);
}

void UJsonButton::InitJsonButton(URuntimeJsonWidget* InOwner, const FString& InElementId)
{
	OwnerWidget = InOwner;
	ElementId = InElementId;
}

void UJsonButton::HandleJsonButtonClicked()
{
	UE_LOG(LogTemp, Display, TEXT("[RuntimeWidgetLayout] OnClicked fired id='%s' owner=%s"),
		*ElementId, OwnerWidget != nullptr ? TEXT("yes") : TEXT("NO"));
	if (OwnerWidget != nullptr && !ElementId.IsEmpty())
	{
		OwnerWidget->NotifyButtonClicked(ElementId);
	}
}
