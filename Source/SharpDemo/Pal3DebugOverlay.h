// Pal3DebugOverlay.h — semi-transparent side panel that renders, straight into the
// viewport: story dialogue, game/system log lines and port diagnostics. Written in
// C++ on purpose: it must stay alive and legible while the managed layer (Mono) is
// wedged, and it must not depend on any UMG asset that does not exist yet.
//
// Managed code pushes lines through UPal3DebugOverlay::Push (bound via UnrealSharp).
// The HUD installs itself on first push — no GameMode/UMG wiring required.
#pragma once

#include "CoreMinimal.h"
#include "GameFramework/HUD.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Pal3DebugOverlay.generated.h"

struct FBootMenuState;
class UTexture2D;

/** One overlay line: severity/category + text. */
UENUM()
enum class EPal3OverlayCategory : uint8
{
	Dialogue,
	Log,
	Debug,
	Error,
};

UCLASS()
class SHARPDEMO_API UPal3DebugOverlay : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/** Appends a line to the overlay (thread-safe, bounded to 200 lines). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void Push(EPal3OverlayCategory Category, const FString& Text);

	/** Clears the overlay buffer. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void Clear();

	/** Installs the HUD on the local player controller (idempotent). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void EnsureHud();

	// --- RC-driven game loop control (managed side polls these) ---
	/** Requests the managed boot menu to start a new game (consumed next tick). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void StartGame();

	/** Returns true once and clears the pending start-game request. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static bool ConsumeStartGameRequest();

	/** Enables/disables the dialogue auto-advance driver (RC toggled). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void EnableAutoDialogue(bool bEnabled);

	/** Requests ONE dialogue-advance pulse (RC single-step). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void AdvanceDialogue();

	/** Returns true once and clears the pending advance request. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static bool ConsumeAdvanceDialogueRequest();

	/** Returns the current dialogue auto-advance state. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static bool IsAutoDialogueOn();

	// --- remote inspection (called through the Remote Control HTTP API) ---
	/** Shows/hides the side log panel (the game view stays clean when hidden). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void SetOverlayVisible(bool bVisible);

	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static bool IsOverlayVisible();

	// --- native dialogue bar (portrait + text) ---
	/**
	 * Sets the portrait drawn in the dialogue bar. UV0/UV1 delimit the opaque region
	 * of the texture (the avatar TGAs carry transparent padding); rightAligned puts
	 * the portrait on the right like the original UI.
	 */
	UFUNCTION(BlueprintCallable, Category = "PAL3|UI")
	static void SetDialogueAvatar(UTexture2D* Texture, bool bRightAligned,
		float U0 = 0.0f, float V0 = 0.0f, float U1 = 1.0f, float V1 = 1.0f);

	UFUNCTION(BlueprintCallable, Category = "PAL3|UI")
	static void ClearDialogueAvatar();

	/** Clears only the dialogue lines (the HUD text box disappears). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|UI")
	static void ClearDialogue();

	/** Dev helper: executes a console command in the game viewport (profiling stats). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void ExecConsoleCommand(const FString& Command);

	/** RC trigger for the JSON->UMG demo widget (built by managed code). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static void RequestJsonDemo();

	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static bool ConsumeJsonDemoRequest();

	/** One line per actor: "name | class | loc=(x,y,z) | rot=(p,y,r)" (up to MaxCount). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static FString DumpActors(int32 MaxCount = 150);

	/** Actors whose name or class contains the substring. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static FString FindActors(const FString& NameContains, int32 MaxCount = 40);

	/** Positions of actors whose class matches exactly (e.g. "StaticMeshActor"). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Debug")
	static FString DumpActorPositions(const FString& ClassContains, int32 MaxCount = 150);

	// --- boot menu (HUD-drawn, click via managed mouse poll) ---
	UFUNCTION(BlueprintCallable, Category = "PAL3|Boot")
	static void SetBootMenuVisible(bool bVisible);

	UFUNCTION(BlueprintCallable, Category = "PAL3|Boot")
	static bool GetBootMenuVisible();

	/** Current menu button rect in viewport pixels (X, Y, W, H order in Out). */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Boot")
	static void GetBootMenuButtonRect(int32& OutX, int32& OutY, int32& OutW, int32& OutH);

	// --- native helpers used by the HUD ---
	static void Snapshot(TArray<TPair<EPal3OverlayCategory, FString>>& OutLines);
	

	/** The game world (PIE/standalone), used by the inspection helpers. */
	static UWorld* FindGameWorld();
};

/**
 * Right-side overlay. Draws a translucent panel with the newest lines at the bottom;
 * categories are colour-coded (dialogue = warm white, log = grey, debug = cyan,
 * error = red).
 */
UCLASS()
class SHARPDEMO_API APal3DebugHUD : public AHUD
{
	GENERATED_BODY()

public:
	virtual void DrawHUD() override;

private:
	/** Bottom dialogue bar used while the side log panel is hidden. */
	void DrawDialogueBar();

	static FLinearColor ColorFor(EPal3OverlayCategory Category);
};
