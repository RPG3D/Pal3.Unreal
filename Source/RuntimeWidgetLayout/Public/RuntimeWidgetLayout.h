// RuntimeWidgetLayout module — build UMG widget trees from JSON at runtime.
//
// The port has no .uasset authoring pipeline (AI/friends cannot easily create
// binary UMG assets), so this module parses a small JSON layout description and
// constructs a UUserWidget tree at runtime. Managed C# code hands in the JSON and
// adds the returned widget to the viewport; button clicks are consumed by polling
// (no managed delegate interop needed).
#pragma once

#include "CoreMinimal.h"
#include "Modules/ModuleManager.h"

class FRuntimeWidgetLayoutModule : public IModuleInterface
{
public:
	virtual void StartupModule() override;
	virtual void ShutdownModule() override;
};
