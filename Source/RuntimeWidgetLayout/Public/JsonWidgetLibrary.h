// JsonWidgetLibrary.h — C++/Blueprint/C# entry points for runtime JSON widgets.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "JsonWidgetLibrary.generated.h"

class URuntimeJsonWidget;
class UFont;

UCLASS()
class RUNTIMEWIDGETLAYOUT_API URuntimeJsonWidgetLibrary : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Adds or replaces a font alias for subsequently created JSON widgets. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout|Fonts")
	static void AddFontMapping(FName Alias, const FString& FontAssetPath);

	/** Overrides the default font with an alias or asset path. An empty value restores the project default. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout|Fonts")
	static void SetDefaultFont(const FString& AliasOrAssetPath);

	static UFont* ResolveFont(const FString& AliasOrAssetPath);
	static int32 ResolveFontSize(int32 LayoutSize);

	/** Reads a layout through UE's file layer. Relative paths start at the project root. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout", meta = (WorldContext = "WorldContextObject"))
	static URuntimeJsonWidget* CreateJsonWidgetFromFile(UObject* WorldContextObject, const FString& Path,
		int32 ZOrder, bool bInteractive);

	/**
	 * Creates the widget from JSON and adds it to the viewport.
	 * Managed code calls this with the JSON text; the returned widget can be
	 * polled with ConsumeClickedButton().
	 */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout", meta = (WorldContext = "WorldContextObject"))
	static URuntimeJsonWidget* CreateJsonWidget(UObject* WorldContextObject, const FString& Json, int32 ZOrder = 0);

	/** Adds a display-only widget without changing viewport focus or input mode. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout", meta = (WorldContext = "WorldContextObject"))
	static URuntimeJsonWidget* CreatePassiveJsonWidget(UObject* WorldContextObject, const FString& Json, int32 ZOrder = 0);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	static void RemoveJsonWidget(URuntimeJsonWidget* Widget);

	UFUNCTION(BlueprintCallable, Category = "RuntimeWidgetLayout")
	static void SetJsonWidgetVisible(URuntimeJsonWidget* Widget, bool bVisible);
};
