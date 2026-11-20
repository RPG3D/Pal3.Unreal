// RuntimeWidgetLayout module implementation.
#include "RuntimeWidgetLayout.h"
#include "JsonWidgetLocalization.h"

void FRuntimeWidgetLayoutModule::StartupModule()
{
}

void FRuntimeWidgetLayoutModule::ShutdownModule()
{
	JsonWidgetLocalization::Reset();
}

IMPLEMENT_MODULE(FRuntimeWidgetLayoutModule, RuntimeWidgetLayout)
