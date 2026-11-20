#pragma once

#include "CoreMinimal.h"

namespace JsonWidgetLocalization
{
	FText Resolve(const FString& Key, const FString& NativeText);
	FString GetLanguage();
	bool SetLanguage(const FString& Language);
	void Reset();
}
