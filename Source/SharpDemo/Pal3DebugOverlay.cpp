// Pal3DebugOverlay.cpp
#include "Pal3DebugOverlay.h"
#include "GameFramework/PlayerController.h"

#include "Engine/Canvas.h"
#include "Engine/Engine.h"
#include "Engine/Font.h"
#include "Engine/Texture2D.h"
#include "EngineUtils.h"
#include "GameFramework/PlayerController.h"
#include "HAL/PlatformTLS.h"
#include "HAL/PlatformTime.h"
#include "Misc/ScopeLock.h"
#include "Framework/Application/SlateApplication.h"

namespace
{
	// Separate bounded buffers per category: dialogue must never be pushed out by
	// log spam (the port logs every SCE command).
	constexpr int32 MaxDialogue = 8;
	constexpr int32 MaxLog = 28;
	constexpr int32 MaxDebug = 8;

struct FOverlayState
{
	FCriticalSection Lock;
	TArray<FString> Dialogue;
	TArray<FString> Log;
	TArray<FString> Debug;
	TArray<FString> Errors;
	bool bStartGameRequested = false;
	bool bAutoDialogue = false;
	bool bAdvanceDialogueRequested = false;
	// Side log panel. Off by default so PIE shows the game, not the diagnostics;
	// F1 / RC SetOverlayVisible turns it on. The dialogue bar still draws.
	bool bOverlayVisible = false;
	// RC-triggered JSON->UMG demo widget request (consumed by managed driver).
	bool bJsonDemoRequested = false;
};

	struct FBootMenuState
	{
		bool bVisible = false;
		FIntRect ButtonRect = FIntRect(0, 0, 0, 0);
	};

	// Dialogue portrait (weak: the texture is owned by the managed texture cache).
	struct FDialogueState
	{
		TWeakObjectPtr<UTexture2D> Avatar;
		bool bRightAligned = false;
		float U0 = 0.0f, V0 = 0.0f, U1 = 1.0f, V1 = 1.0f;
	};

	FDialogueState& Dialogue()
	{
		static FDialogueState Instance;
		return Instance;
	}

	FBootMenuState& BootMenu()
	{
		static FBootMenuState Instance;
		return Instance;
	}

	FOverlayState& State()
	{
		static FOverlayState Instance;
		return Instance;
	}

	TArray<FString>& BufferFor(FOverlayState& S, EPal3OverlayCategory Category)
	{
		switch (Category)
		{
			case EPal3OverlayCategory::Dialogue: return S.Dialogue;
			case EPal3OverlayCategory::Debug:    return S.Debug;
			case EPal3OverlayCategory::Error:    return S.Errors;
			default:                             return S.Log;
		}
	}

	int32 MaxFor(EPal3OverlayCategory Category)
	{
		switch (Category)
		{
			case EPal3OverlayCategory::Dialogue: return MaxDialogue;
			case EPal3OverlayCategory::Debug:    return MaxDebug;
			case EPal3OverlayCategory::Error:    return MaxDebug;
			default:                             return MaxLog;
		}
	}

	// Consecutive identical pushes (the stub UI re-sets the same text every frame)
	// would otherwise flood the panel.
	bool ShouldSuppress(FOverlayState& S, EPal3OverlayCategory Category, const FString& Text)
	{
		const TArray<FString>& Buffer = BufferFor(S, Category);
		return Buffer.Num() > 0 && Buffer.Last() == Text;
	}
}

void UPal3DebugOverlay::Push(EPal3OverlayCategory Category, const FString& Text)
{
	if (Text.IsEmpty())
	{
		return;
	}

	FOverlayState& S = State();
	{
		FScopeLock Lock(&S.Lock);
		if (ShouldSuppress(S, Category, Text))
		{
			return;
		}

		TArray<FString>& Buffer = BufferFor(S, Category);
		Buffer.Add(Text);
		const int32 Max = MaxFor(Category);
		if (Buffer.Num() > Max)
		{
			Buffer.RemoveAt(0, Buffer.Num() - Max, EAllowShrinking::No);
		}
	}

	EnsureHud();
}

void UPal3DebugOverlay::Clear()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.Dialogue.Reset();
	S.Log.Reset();
	S.Debug.Reset();
	S.Errors.Reset();
}

// --- RC-driven game loop control ---

void UPal3DebugOverlay::StartGame()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.bStartGameRequested = true;
}

bool UPal3DebugOverlay::ConsumeStartGameRequest()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	const bool bRequested = S.bStartGameRequested;
	S.bStartGameRequested = false;
	return bRequested;
}

void UPal3DebugOverlay::EnableAutoDialogue(bool bEnabled)
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.bAutoDialogue = bEnabled;
}

bool UPal3DebugOverlay::IsAutoDialogueOn()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	return S.bAutoDialogue;
}

void UPal3DebugOverlay::AdvanceDialogue()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.bAdvanceDialogueRequested = true;
}

bool UPal3DebugOverlay::ConsumeAdvanceDialogueRequest()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	const bool bRequested = S.bAdvanceDialogueRequested;
	S.bAdvanceDialogueRequested = false;
	return bRequested;
}

void UPal3DebugOverlay::SetOverlayVisible(bool bVisible)
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.bOverlayVisible = bVisible;
}

bool UPal3DebugOverlay::IsOverlayVisible()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	return S.bOverlayVisible;
}

void UPal3DebugOverlay::SetDialogueAvatar(UTexture2D* Texture, bool bRightAligned,
	float U0, float V0, float U1, float V1)
{
	FDialogueState& D = Dialogue();
	D.Avatar = Texture;
	D.bRightAligned = bRightAligned;
	// Clamp so a bad rect can never collapse the portrait to nothing.
	D.U0 = FMath::Clamp(U0, 0.0f, 1.0f);
	D.U1 = FMath::Clamp(U1, 0.0f, 1.0f);
	D.V0 = FMath::Clamp(V0, 0.0f, 1.0f);
	D.V1 = FMath::Clamp(V1, 0.0f, 1.0f);
	if (D.U1 <= D.U0) { D.U0 = 0.0f; D.U1 = 1.0f; }
	if (D.V1 <= D.V0) { D.V0 = 0.0f; D.V1 = 1.0f; }
}

void UPal3DebugOverlay::ClearDialogueAvatar()
{
	FDialogueState& D = Dialogue();
	D.Avatar.Reset();
	D.bRightAligned = false;
}

void UPal3DebugOverlay::ClearDialogue()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.Dialogue.Reset();
}

void UPal3DebugOverlay::ExecConsoleCommand(const FString& Command)
{
	if (GEngine && GEngine->GameViewport && !Command.IsEmpty())
	{
		GEngine->GameViewport->Exec(nullptr, *Command, *GLog);
	}
}

void UPal3DebugOverlay::RequestJsonDemo()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	S.bJsonDemoRequested = true;
}

bool UPal3DebugOverlay::ConsumeJsonDemoRequest()
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	const bool bRequested = S.bJsonDemoRequested;
	S.bJsonDemoRequested = false;
	return bRequested;
}

void UPal3DebugOverlay::EnsureHud()
{
	if (!GEngine || !GEngine->GameViewport)
	{
		return;
	}

	UWorld* World = GEngine->GameViewport->GetWorld();
	if (!World)
	{
		return;
	}

	APlayerController* PC = GEngine->GetFirstLocalPlayerController(World);
	if (!PC)
	{
		return;
	}

	AHUD* Current = PC->GetHUD();
	if (Current && Current->IsA<APal3DebugHUD>())
	{
		return;
	}

	PC->ClientSetHUD(APal3DebugHUD::StaticClass());
}

void UPal3DebugOverlay::Snapshot(TArray<TPair<EPal3OverlayCategory, FString>>& OutLines)
{
	FOverlayState& S = State();
	FScopeLock Lock(&S.Lock);
	for (const FString& Line : S.Debug)    { OutLines.Emplace(EPal3OverlayCategory::Debug, Line); }
	for (const FString& Line : S.Errors)   { OutLines.Emplace(EPal3OverlayCategory::Error, Line); }
	for (const FString& Line : S.Log)      { OutLines.Emplace(EPal3OverlayCategory::Log, Line); }
	for (const FString& Line : S.Dialogue) { OutLines.Emplace(EPal3OverlayCategory::Dialogue, Line); }
}


UWorld* UPal3DebugOverlay::FindGameWorld()
{
	if (!GEngine)
	{
		return nullptr;
	}

	// -game can expose several contexts (empty/editor-preview worlds included).
	// Prefer the one that actually owns a game instance, and among those the one
	// with the most actors (the real play world).
	UWorld* Best = nullptr;
	int32 BestActors = -1;
	for (const FWorldContext& Context : GEngine->GetWorldContexts())
	{
		UWorld* Candidate = Context.World();
		if (Candidate == nullptr)
		{
			continue;
		}
		if (Context.WorldType != EWorldType::PIE && Context.WorldType != EWorldType::Game)
		{
			continue;
		}
		if (Candidate->GetGameInstance() == nullptr)
		{
			continue;
		}
		int32 Count = 0;
		for (TActorIterator<AActor> It(Candidate); It; ++It)
		{
			++Count;
		}
		if (Count > BestActors)
		{
			BestActors = Count;
			Best = Candidate;
		}
	}
	return Best;
}

static FString DescribeActor(const AActor* Actor)
{
	const FVector Location = Actor->GetActorLocation();
	const FRotator Rotation = Actor->GetActorRotation();
	return FString::Printf(TEXT("%s | %s | loc=(%.0f,%.0f,%.0f) | rot=(P%.0f,Y%.0f,R%.0f)"),
		*Actor->GetName(),
		*Actor->GetClass()->GetName(),
		Location.X, Location.Y, Location.Z,
		Rotation.Pitch, Rotation.Yaw, Rotation.Roll);
}

FString UPal3DebugOverlay::DumpActors(int32 MaxCount)
{
	// [build:v2] marker lets the client confirm the running binary is current.
	// Diagnostic: list every world context with its actor count first, so the
	// caller can see which world the managed layer is actually spawning into.
	FString Summary;
	for (const FWorldContext& Ctx : GEngine->GetWorldContexts())
	{
		if (Ctx.World() == nullptr) continue;
		int32 CtxCount = 0;
		for (TActorIterator<AActor> It(Ctx.World()); It; ++It) { ++CtxCount; }
		Summary += FString::Printf(TEXT("[ctx] world=%s type=%d actors=%d\n"),
			*Ctx.World()->GetName(), static_cast<int32>(Ctx.WorldType), CtxCount);
	}

	UWorld* World = FindGameWorld();
	if (!World)
	{
		return Summary + TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	int32 Total = 0;
	for (TActorIterator<AActor> It(World); It; ++It, ++Total)
	{
		if (Count >= MaxCount)
		{
			continue;
		}
		Out += DescribeActor(*It) + TEXT("\n");
		++Count;
	}

	return FString(TEXT("[build:v2]\n")) + Summary
		+ FString::Printf(TEXT("world=%s type=%d actors=%d (shown %d)\n%s"),
			*World->GetName(), static_cast<int32>(World->WorldType), Total, Count, *Out);
}

FString UPal3DebugOverlay::FindActors(const FString& NameContains, int32 MaxCount)
{
	UWorld* World = FindGameWorld();
	if (!World)
	{
		return TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		const FString Name = It->GetName();
		const FString ClassName = It->GetClass()->GetName();
		if (!Name.Contains(NameContains) && !ClassName.Contains(NameContains))
		{
			continue;
		}
		Out += DescribeActor(*It) + TEXT("\n");
		if (++Count >= MaxCount)
		{
			break;
		}
	}

	return Out.IsEmpty() ? FString::Printf(TEXT("<no actor matching '%s'>"), *NameContains) : Out;
}

FString UPal3DebugOverlay::DumpActorPositions(const FString& ClassContains, int32 MaxCount)
{
	UWorld* World = FindGameWorld();
	if (!World)
	{
		return TEXT("<no game world>");
	}

	FString Out;
	int32 Count = 0;
	for (TActorIterator<AActor> It(World); It; ++It)
	{
		if (!It->GetClass()->GetName().Contains(ClassContains))
		{
			continue;
		}
		const FVector Location = It->GetActorLocation();
		Out += FString::Printf(TEXT("%s %.1f %.1f %.1f\n"), *It->GetName(), Location.X, Location.Y, Location.Z);
		if (++Count >= MaxCount)
		{
			break;
		}
	}
	return Out;
}

void UPal3DebugOverlay::SetBootMenuVisible(bool bVisible)
{
	BootMenu().bVisible = bVisible;
	if (UWorld* World = FindGameWorld())
	{
		if (APlayerController* PC = GEngine->GetFirstLocalPlayerController(World))
		{
			PC->bShowMouseCursor = bVisible;
		}
	}
}

bool UPal3DebugOverlay::GetBootMenuVisible()
{
	return BootMenu().bVisible;
}

void UPal3DebugOverlay::GetBootMenuButtonRect(int32& OutX, int32& OutY, int32& OutW, int32& OutH)
{
	const FIntRect& R = BootMenu().ButtonRect;
	OutX = R.Min.X; OutY = R.Min.Y; OutW = R.Width(); OutH = R.Height();
}

FLinearColor APal3DebugHUD::ColorFor(EPal3OverlayCategory Category)
{
	switch (Category)
	{
		case EPal3OverlayCategory::Dialogue: return FLinearColor(1.0f, 0.95f, 0.80f, 1.0f);
		case EPal3OverlayCategory::Debug:    return FLinearColor(0.45f, 0.90f, 1.00f, 1.0f);
		case EPal3OverlayCategory::Error:    return FLinearColor(1.00f, 0.45f, 0.40f, 1.0f);
		default:                             return FLinearColor(0.78f, 0.80f, 0.82f, 1.0f);
	}
}

void APal3DebugHUD::DrawHUD()
{
	Super::DrawHUD();

	if (!Canvas)
	{
		return;
	}

	// --- boot menu (when the managed boot waits for a start click) ---
	if (UPal3DebugOverlay::GetBootMenuVisible())
	{
		const float W = Canvas->ClipX, H = Canvas->ClipY;
		DrawRect(FLinearColor(0.01f, 0.02f, 0.04f, 0.94f), 0, 0, W, H);

		UFont* Font = GEngine ? GEngine->GetLargeFont() : nullptr;
		if (Font)
		{
			DrawText(TEXT("仙剑奇侠传三"), FLinearColor(0.85f, 0.72f, 0.35f, 1.0f),
				W * 0.5f - 120.0f, H * 0.28f, Font, 2.4f, false);
			DrawText(TEXT("UnrealSharp 移植版"), FLinearColor(0.55f, 0.6f, 0.65f, 1.0f),
				W * 0.5f - 70.0f, H * 0.38f, Font, 0.9f, false);
		}

		const FVector2D BtnSize(280, 64);
		const FVector2D BtnPos((W - BtnSize.X) * 0.5f, H * 0.52f);
		const FVector2D Cursor = FSlateApplication::Get().GetCursorPos();
		const bool bHover = Cursor.X >= BtnPos.X && Cursor.X <= BtnPos.X + BtnSize.X &&
			Cursor.Y >= BtnPos.Y && Cursor.Y <= BtnPos.Y + BtnSize.Y;
		const FLinearColor BtnColor = bHover
			? FLinearColor(0.25f, 0.35f, 0.55f, 0.95f)
			: FLinearColor(0.12f, 0.18f, 0.30f, 0.95f);
		DrawRect(BtnColor, BtnPos.X, BtnPos.Y, BtnSize.X, BtnSize.Y);
		if (Font)
		{
			DrawText(TEXT("新 的 游 戏"), FLinearColor::White,
				BtnPos.X + BtnSize.X * 0.5f - 54.0f, BtnPos.Y + 18.0f, Font, 1.4f, false);
		}

		BootMenu().ButtonRect = FIntRect((int32)BtnPos.X, (int32)BtnPos.Y,
			(int32)(BtnPos.X + BtnSize.X), (int32)(BtnPos.Y + BtnSize.Y));
	}

	TArray<TPair<EPal3OverlayCategory, FString>> Lines;
	UPal3DebugOverlay::Snapshot(Lines);

	// Default view: no diagnostics panel, just the in-game dialogue bar.
	if (!UPal3DebugOverlay::IsOverlayVisible())
	{
		DrawDialogueBar();
		return;
	}

	const float ScreenW = Canvas->ClipX;
	const float ScreenH = Canvas->ClipY;
	const float PanelW = FMath::Clamp(ScreenW * 0.36f, 380.0f, 720.0f);
	const float Margin = 12.0f;
	const float PanelX = ScreenW - PanelW - Margin;
	const float PanelY = Margin;
	const float PanelH = ScreenH - 2.0f * Margin;

	// Translucent backdrop.
	DrawRect(FLinearColor(0.02f, 0.03f, 0.05f, 0.55f), PanelX, PanelY, PanelW, PanelH);

	UFont* Font = GEngine ? GEngine->GetSmallFont() : nullptr;
	if (!Font)
	{
		return;
	}

	const float LineH = 18.0f;
	const float PadX = 12.0f;
	const float TextX = PanelX + PadX;
	const int32 MaxChars = FMath::Max(24, static_cast<int32>((PanelW - 2.0f * PadX) / 7.0f));
	float TextY = PanelY + 8.0f;

	// Header.
	DrawText(TEXT("PAL3 DEBUG"), FLinearColor(0.60f, 1.00f, 0.75f, 1.0f), TextX, TextY, Font, 1.1f, false);
	TextY += LineH * 1.4f;

	// Dialogue sits at the bottom (what the player is reading), logs above it.
	float Y = PanelY + PanelH - LineH - 6.0f;
	for (int32 i = Lines.Num() - 1; i >= 0; --i)
	{
		if (Lines[i].Key != EPal3OverlayCategory::Dialogue) continue;

		FString Text = Lines[i].Value.Left(MaxChars);
		if (Text.Len() < Lines[i].Value.Len()) Text += TEXT("...");
		DrawText(Text, ColorFor(Lines[i].Key), TextX, Y, Font, 1.2f, false);
		Y -= LineH * 1.3f;
	}

	Y -= LineH * 0.6f;
	for (int32 i = Lines.Num() - 1; i >= 0; --i)
	{
		if (Lines[i].Key == EPal3OverlayCategory::Dialogue) continue;

		FString Text = Lines[i].Value.Left(MaxChars);
		if (Text.Len() < Lines[i].Value.Len()) Text += TEXT("...");
		DrawText(Text, ColorFor(Lines[i].Key), TextX, Y, Font, 1.0f, false);
		Y -= LineH;
		if (Y < PanelY + LineH) break;
	}
}

void APal3DebugHUD::DrawDialogueBar()
{
	if (!Canvas || !GEngine)
	{
		return;
	}

	TArray<TPair<EPal3OverlayCategory, FString>> Lines;
	UPal3DebugOverlay::Snapshot(Lines);

	FString Text;
	for (int32 i = Lines.Num() - 1; i >= 0; --i)
	{
		if (Lines[i].Key == EPal3OverlayCategory::Dialogue)
		{
			Text = Lines[i].Value;
			break;
		}
	}
	if (Text.IsEmpty())
	{
		return;
	}

	const float W = Canvas->ClipX, H = Canvas->ClipY;
	const float BarW = W * 0.86f;
	const float BarH = FMath::Clamp(H * 0.23f, 132.0f, 240.0f);
	const float BarX = (W - BarW) * 0.5f;
	const float BarY = H - BarH - H * 0.03f;

	// Dark letterbox panel with a thin warm border, mimicking the PAL3 text box.
	DrawRect(FLinearColor(0.03f, 0.035f, 0.05f, 0.86f), BarX, BarY, BarW, BarH);
	DrawRect(FLinearColor(0.72f, 0.62f, 0.36f, 0.85f), BarX, BarY, BarW, 2.0f);
	DrawRect(FLinearColor(0.72f, 0.62f, 0.36f, 0.85f), BarX, BarY + BarH - 2.0f, BarW, 2.0f);

	UFont* Font = GEngine->GetMediumFont();
	if (!Font)
	{
		return;
	}

	// Portrait (the game's DialogueRenderActorAvatar texture). Left-aligned by
	// default; RightAligned portraits sit on the right like the original UI.
	float TextLeft = BarX + 36.0f;
	float TextMaxW = BarW - 72.0f;
	UTexture2D* Avatar = Dialogue().Avatar.Get();
	if (Avatar)
	{
		const FDialogueState& D = Dialogue();
		const float Pad = 8.0f;
		const float PortraitH = BarH - 2.0f * Pad;
		const float TexW = static_cast<float>(FMath::Max(1, Avatar->GetSizeX()));
		const float TexH = static_cast<float>(FMath::Max(1, Avatar->GetSizeY()));
		const float CropW = (D.U1 - D.U0) * TexW;
		const float CropH = (D.V1 - D.V0) * TexH;
		const float Aspect = CropW / FMath::Max(1.0f, CropH);
		const float PortraitW = FMath::Min(PortraitH * Aspect, BarW * 0.32f);
		const bool bRight = D.bRightAligned;
		const float PortraitX = bRight ? (BarX + BarW - Pad - PortraitW) : (BarX + Pad);
		const float PortraitY = BarY + Pad;
		DrawTexture(Avatar, PortraitX, PortraitY, PortraitW, PortraitH,
			D.U0, D.V0, D.U1 - D.U0, D.V1 - D.V0, FLinearColor::White, BLEND_Translucent);
		if (!bRight)
		{
			TextLeft = PortraitX + PortraitW + Pad;
			TextMaxW = BarX + BarW - 36.0f - TextLeft;
		}
		else
		{
			TextMaxW = PortraitX - 12.0f - TextLeft;
		}
	}

	const float Scale = 1.85f;
	const float CharW = Font->GetMaxCharHeight() * Scale;
	const float MaxW = FMath::Max(120.0f, TextMaxW);
	const int32 MaxGlyphs = FMath::Max(8, static_cast<int32>(MaxW / FMath::Max(1.0f, CharW * 0.95f)));

	float Y = BarY + 22.0f;
	const float X = TextLeft;

	TArray<FString> Paragraphs;
	Text.ParseIntoArray(Paragraphs, TEXT("\n"), false);
	for (const FString& Paragraph : Paragraphs)
	{
		int32 Start = 0;
		while (Start < Paragraph.Len())
		{
			const int32 Count = FMath::Min(MaxGlyphs, Paragraph.Len() - Start);
			DrawText(Paragraph.Mid(Start, Count), FLinearColor(0.98f, 0.95f, 0.88f, 1.0f),
				X, Y, Font, Scale, false);
			Y += CharW + 6.0f;
			Start += Count;
		}
	}

	DrawText(TEXT("空格 / 回车 继续"), FLinearColor(0.55f, 0.55f, 0.55f, 0.85f),
		BarX + BarW - 170.0f, BarY + BarH - 28.0f, Font, 0.85f, false);
}
