// JsonWidget.cpp — JSON → UMG tree builder.
#include "JsonWidget.h"
#include "JsonButton.h"

#include "Blueprint/WidgetTree.h"
#include "Components/Border.h"
#include "Components/CanvasPanel.h"
#include "Components/CanvasPanelSlot.h"
#include "Components/ProgressBar.h"
#include "Components/TextBlock.h"
#include "Dom/JsonObject.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"
#include "Styling/SlateBrush.h"
#include "Styling/SlateColor.h"
#include "Framework/Application/SlateApplication.h"
#include "Components/Button.h"
#include "GameFramework/PlayerController.h"
#include "InputCoreTypes.h"

namespace
{
	FString GetString(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, const FString& Default = TEXT(""))
	{
		FString Value;
		return Object->TryGetStringField(Field, Value) ? Value : Default;
	}

	double GetNumber(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, double Default)
	{
		double Value;
		return Object->TryGetNumberField(Field, Value) ? Value : Default;
	}

	bool GetBool(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, bool Default)
	{
		bool Value;
		return Object->TryGetBoolField(Field, Value) ? Value : Default;
	}

	bool GetColor(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, FLinearColor& Out)
	{
		const TArray<TSharedPtr<FJsonValue>>* Array = nullptr;
		if (!Object->TryGetArrayField(Field, Array) || Array == nullptr || Array->Num() < 3)
		{
			return false;
		}
		const float R = (float)(*Array)[0]->AsNumber();
		const float G = (float)(*Array)[1]->AsNumber();
		const float B = (float)(*Array)[2]->AsNumber();
		const float A = Array->Num() > 3 ? (float)(*Array)[3]->AsNumber() : 1.0f;
		Out = FLinearColor(R, G, B, A);
		return true;
	}

	bool GetVector2(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, FVector2D& Out)
	{
		const TArray<TSharedPtr<FJsonValue>>* Array = nullptr;
		if (!Object->TryGetArrayField(Field, Array) || Array == nullptr || Array->Num() < 2)
		{
			return false;
		}
		Out = FVector2D((float)(*Array)[0]->AsNumber(), (float)(*Array)[1]->AsNumber());
		return true;
	}

	FMargin ParsePadding(const TSharedPtr<FJsonObject>& Object, const TCHAR* Field, const FMargin& Default)
	{
		const TArray<TSharedPtr<FJsonValue>>* Array = nullptr;
		if (Object->TryGetArrayField(Field, Array) && Array != nullptr)
		{
			if (Array->Num() >= 4)
			{
				return FMargin((float)(*Array)[0]->AsNumber(), (float)(*Array)[1]->AsNumber(),
					(float)(*Array)[2]->AsNumber(), (float)(*Array)[3]->AsNumber());
			}
			if (Array->Num() == 1)
			{
				const float All = (float)(*Array)[0]->AsNumber();
				return FMargin(All);
			}
		}

		double Scalar;
		if (Object->TryGetNumberField(Field, Scalar))
		{
			return FMargin((float)Scalar);
		}
		return Default;
	}

	void ApplyCanvasSlot(UCanvasPanel* Canvas, UWidget* Child, const TSharedPtr<FJsonObject>& Element)
	{
		UCanvasPanelSlot* Slot = Canvas->AddChildToCanvas(Child);
		if (Slot == nullptr)
		{
			return;
		}

		const FString Anchor = GetString(Element, TEXT("anchor"), TEXT("top_left"));
		FVector2D Position(0.0, 0.0);
		GetVector2(Element, TEXT("position"), Position);

		if (Anchor.Equals(TEXT("full"), ESearchCase::IgnoreCase))
		{
			Slot->SetAnchors(FAnchors(0.0f, 0.0f, 1.0f, 1.0f));
			Slot->SetOffsets(FMargin(0.0f));
			Slot->SetAlignment(FVector2D(0.0f, 0.0f));
		}
		else
		{
			if (Anchor.Equals(TEXT("center"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(0.5f, 0.5f));
				Slot->SetAlignment(FVector2D(0.5f, 0.5f));
			}
			else if (Anchor.Equals(TEXT("top_center"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(0.5f, 0.0f));
				Slot->SetAlignment(FVector2D(0.5f, 0.0f));
			}
			else if (Anchor.Equals(TEXT("bottom_center"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(0.5f, 1.0f));
				Slot->SetAlignment(FVector2D(0.5f, 1.0f));
			}
			else
			{
				Slot->SetAnchors(FAnchors(0.0f, 0.0f));
				Slot->SetAlignment(FVector2D(0.0f, 0.0f));
			}

			Slot->SetPosition(Position);

			FVector2D Size(200.0, 48.0);
			if (GetVector2(Element, TEXT("size"), Size))
			{
				Slot->SetAutoSize(false);
				Slot->SetSize(Size);
			}
			else
			{
				Slot->SetAutoSize(true);
			}
		}

		Slot->SetZOrder((int32)GetNumber(Element, TEXT("zorder"), 0));
	}

	UTextBlock* MakeText(UWidgetTree* Tree, const TSharedPtr<FJsonObject>& Element, const FString& DefaultText)
	{
		UTextBlock* Text = Tree->ConstructWidget<UTextBlock>(UTextBlock::StaticClass());
		const FString Content = GetString(Element, TEXT("text"), DefaultText);
		Text->SetText(FText::FromString(Content));

		FSlateFontInfo Font = Text->GetFont();
		Font.Size = (int32)GetNumber(Element, TEXT("fontSize"), Font.Size);
		Text->SetFont(Font);

		FLinearColor Color(1.0f, 1.0f, 1.0f, 1.0f);
		if (GetColor(Element, TEXT("color"), Color))
		{
			Text->SetColorAndOpacity(FSlateColor(Color));
		}

		FString Align = GetString(Element, TEXT("align"), TEXT("center"));
		if (Align.Equals(TEXT("left"), ESearchCase::IgnoreCase))
		{
			Text->SetJustification(ETextJustify::Left);
		}
		else if (Align.Equals(TEXT("right"), ESearchCase::IgnoreCase))
		{
			Text->SetJustification(ETextJustify::Right);
		}
		else
		{
			Text->SetJustification(ETextJustify::Center);
		}

		return Text;
	}
}

bool URuntimeJsonWidget::BuildFromJson(const FString& Json)
{
	TSharedPtr<FJsonObject> Document;
	const TSharedRef<TJsonReader<>> Reader = TJsonReaderFactory<>::Create(Json);
	if (!FJsonSerializer::Deserialize(Reader, Document) || !Document.IsValid())
	{
		UE_LOG(LogTemp, Warning, TEXT("[RuntimeWidgetLayout] JSON parse error"));
		return false;
	}

	TSharedPtr<FJsonObject> RootElement = Document;
	const TSharedPtr<FJsonObject>* RootField = nullptr;
	if (Document->TryGetObjectField(TEXT("root"), RootField) && RootField != nullptr)
	{
		RootElement = *RootField;
	}

	if (!RootElement.IsValid())
	{
		return false;
	}

	if (WidgetTree == nullptr)
	{
		WidgetTree = NewObject<UWidgetTree>(this, TEXT("WidgetTree"));
	}

	WidgetTree->RootWidget = nullptr;
	ElementsById.Reset();

	UWidget* RootWidget = BuildElement(RootElement, WidgetTree);
	if (RootWidget == nullptr)
	{
		return false;
	}

	WidgetTree->RootWidget = RootWidget;
	return true;
}

void URuntimeJsonWidget::NotifyButtonClicked(const FString& ElementId)
{
	// A click can arrive twice: once from Slate's OnClicked and once from the
	// NativeTick hit test (used when embedded PIE swallows Slate mouse events).
	const double Now = FPlatformTime::Seconds();
	if (ElementId == LastNotifiedId && Now - LastNotifySeconds < 0.25)
	{
		return;
	}
	LastNotifiedId = ElementId;
	LastNotifySeconds = Now;
	LastClickedId = ElementId;
	UE_LOG(LogTemp, Display, TEXT("[RuntimeWidgetLayout] button clicked: %s"), *ElementId);
}

void URuntimeJsonWidget::NativeTick(const FGeometry& MyGeometry, float InDeltaTime)
{
	Super::NativeTick(MyGeometry, InDeltaTime);

	// Slate input does not reach widgets added to the PIE game viewport in this
	// embedded-editor setup, so buttons are hit-tested against the player
	// controller's mouse position (the same path the HUD boot menu uses).
	APlayerController* PC = GetOwningPlayer();
	if (PC == nullptr || ButtonRects.Num() == 0)
	{
		return;
	}

	float MouseX = 0.0f, MouseY = 0.0f;
	const bool bHasMouse = PC->GetMousePosition(MouseX, MouseY);
	const bool bPressed = PC->WasInputKeyJustPressed(EKeys::LeftMouseButton);
	if (!bHasMouse || !bPressed)
	{
		return;
	}

	int32 ViewX = 0, ViewY = 0;
	PC->GetViewportSize(ViewX, ViewY);
	const FVector2D Local = MyGeometry.GetLocalSize();
	if (Local.X <= 0.0f || Local.Y <= 0.0f || ViewX <= 0 || ViewY <= 0)
	{
		return;
	}

	const FVector2D Scale(ViewX / Local.X, ViewY / Local.Y);
	const FVector2D LocalPoint(MouseX / Scale.X, MouseY / Scale.Y);

	for (const TPair<FString, FBox2D>& Pair : ButtonRects)
	{
		if (Pair.Value.IsInside(LocalPoint))
		{
			NotifyButtonClicked(Pair.Key);
			break;
		}
	}
}

bool URuntimeJsonWidget::ConsumeClickedButton(FString& OutElementId)
{
	if (LastClickedId.IsEmpty())
	{
		return false;
	}
	OutElementId = LastClickedId;
	LastClickedId.Reset();
	return true;
}

UWidget* URuntimeJsonWidget::BuildElement(const TSharedPtr<FJsonObject>& Element, UWidgetTree* Tree,
	const FVector2D& ParentOffset)
{
	if (!Element.IsValid() || Tree == nullptr)
	{
		return nullptr;
	}

	const FString Type = GetString(Element, TEXT("type"), TEXT("canvas"));
	const FString Id = GetString(Element, TEXT("id"));

	UWidget* Result = nullptr;

	if (Type.Equals(TEXT("canvas"), ESearchCase::IgnoreCase))
	{
		UCanvasPanel* Canvas = Tree->ConstructWidget<UCanvasPanel>(UCanvasPanel::StaticClass());
		Result = Canvas;

		const TArray<TSharedPtr<FJsonValue>>* Children = nullptr;
		if (Element->TryGetArrayField(TEXT("children"), Children) && Children != nullptr)
		{
			for (const TSharedPtr<FJsonValue>& ChildValue : *Children)
			{
				const TSharedPtr<FJsonObject> ChildObject = ChildValue.IsValid() ? ChildValue->AsObject() : nullptr;
				FVector2D ChildPos(0.0, 0.0);
				if (ChildObject.IsValid())
				{
					GetVector2(ChildObject, TEXT("position"), ChildPos);
				}
				if (UWidget* Child = BuildElement(ChildObject, Tree, ParentOffset + ChildPos))
				{
					ApplyCanvasSlot(Canvas, Child, ChildObject);
				}
			}
		}
	}
	else if (Type.Equals(TEXT("border"), ESearchCase::IgnoreCase))
	{
		UBorder* Border = Tree->ConstructWidget<UBorder>(UBorder::StaticClass());
		FLinearColor Color(0.05f, 0.06f, 0.09f, 0.85f);
		if (GetColor(Element, TEXT("color"), Color))
		{
			Border->SetBrushColor(Color);
		}
		Border->SetPadding(ParsePadding(Element, TEXT("padding"), FMargin(0.0f)));

		const FMargin BorderPadding = ParsePadding(Element, TEXT("padding"), FMargin(0.0f));
		const TSharedPtr<FJsonObject>* ChildField = nullptr;
		if (Element->TryGetObjectField(TEXT("child"), ChildField) && ChildField != nullptr)
		{
			if (UWidget* Child = BuildElement(*ChildField, Tree,
					ParentOffset + FVector2D(BorderPadding.Left, BorderPadding.Top)))
			{
				Border->SetContent(Child);
			}
		}
		Result = Border;
	}
	else if (Type.Equals(TEXT("text"), ESearchCase::IgnoreCase))
	{
		Result = MakeText(Tree, Element, TEXT(""));
	}
	else if (Type.Equals(TEXT("button"), ESearchCase::IgnoreCase))
	{
		UJsonButton* Button = Tree->ConstructWidget<UJsonButton>(UJsonButton::StaticClass());
		Button->InitJsonButton(this, Id);
		Button->BindClickHandler();

		FLinearColor Normal(0.16f, 0.22f, 0.34f, 1.0f);
		GetColor(Element, TEXT("normalColor"), Normal);
		FLinearColor Hover = Normal * 1.4f;
		GetColor(Element, TEXT("hoverColor"), Hover);
		FLinearColor Pressed = Normal * 0.7f;
		GetColor(Element, TEXT("pressedColor"), Pressed);

		Button->WidgetStyle.Normal.TintColor = FSlateColor(Normal);
		Button->WidgetStyle.Hovered.TintColor = FSlateColor(Hover);
		Button->WidgetStyle.Pressed.TintColor = FSlateColor(Pressed);

		UTextBlock* Label = MakeText(Tree, Element, TEXT("Button"));
		Button->AddChild(Label);

		FVector2D ButtonSize(200.0, 48.0);
		GetVector2(Element, TEXT("size"), ButtonSize);
		if (!Id.IsEmpty())
		{
			ButtonRects.Add(Id, FBox2D(ParentOffset, ParentOffset + ButtonSize));
		}
		Result = Button;
	}
	else if (Type.Equals(TEXT("progressbar"), ESearchCase::IgnoreCase))
	{
		UProgressBar* Progress = Tree->ConstructWidget<UProgressBar>(UProgressBar::StaticClass());
		Progress->SetPercent((float)FMath::Clamp(GetNumber(Element, TEXT("percent"), 0.0), 0.0, 1.0));

		FLinearColor Fill(0.2f, 0.8f, 0.4f, 1.0f);
		if (GetColor(Element, TEXT("fillColor"), Fill))
		{
			Progress->SetFillColorAndOpacity(Fill);
		}
		Result = Progress;
	}
	else
	{
		UE_LOG(LogTemp, Warning, TEXT("[RuntimeWidgetLayout] unknown element type '%s'"), *Type);
		return nullptr;
	}

	if (Result != nullptr)
	{
		if (!Id.IsEmpty())
		{
			ElementsById.Add(Id, Result);
		}
		// Buttons must stay hit-testable; canvases pass clicks through to children.
		if (Type.Equals(TEXT("canvas"), ESearchCase::IgnoreCase))
		{
			Result->SetVisibility(ESlateVisibility::SelfHitTestInvisible);
		}
		else if (Type.Equals(TEXT("button"), ESearchCase::IgnoreCase))
		{
			Result->SetVisibility(ESlateVisibility::Visible);
		}
	}

	return Result;
}
