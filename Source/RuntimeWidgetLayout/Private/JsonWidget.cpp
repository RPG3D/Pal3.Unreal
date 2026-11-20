// JsonWidget.cpp — JSON → UMG tree builder.
#include "JsonWidget.h"
#include "JsonButton.h"
#include "JsonWidgetLibrary.h"

#include "Blueprint/WidgetTree.h"
#include "Engine/Font.h"
#include "Blueprint/SlateBlueprintLibrary.h"
#include "Brushes/SlateRoundedBoxBrush.h"
#include "Components/Border.h"
#include "Components/CanvasPanel.h"
#include "Components/CanvasPanelSlot.h"
#include "Components/Image.h"
#include "Components/ProgressBar.h"
#include "Components/ScaleBox.h"
#include "Components/ScrollBox.h"
#include "Components/TextBlock.h"
#include "Components/WidgetSwitcher.h"
#include "Dom/JsonObject.h"
#include "Engine/Texture2D.h"
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
	bool IsActiveButton(const UWidget* Button)
	{
		for (const UWidget* Widget = Button; Widget; Widget = Widget->GetParent())
		{
			if (!Widget->IsVisible() || Widget->GetVisibility() == ESlateVisibility::HitTestInvisible ||
				!Widget->GetIsEnabled())
			{
				return false;
			}
			if (const UWidgetSwitcher* Switcher = Cast<UWidgetSwitcher>(Widget->GetParent()))
			{
				if (Switcher->GetActiveWidget() != Widget) return false;
			}
		}
		return true;
	}

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
			else if (Anchor.Equals(TEXT("bottom_left"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(0.0f, 1.0f));
				Slot->SetAlignment(FVector2D(0.0f, 1.0f));
			}
			else if (Anchor.Equals(TEXT("top_right"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(1.0f, 0.0f));
				Slot->SetAlignment(FVector2D(1.0f, 0.0f));
			}
			else if (Anchor.Equals(TEXT("bottom_right"), ESearchCase::IgnoreCase))
			{
				Slot->SetAnchors(FAnchors(1.0f, 1.0f));
				Slot->SetAlignment(FVector2D(1.0f, 1.0f));
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
		Text->SetText(URuntimeJsonWidgetLibrary::GetLocalizedText(GetString(Element, TEXT("textKey")), Content));
		bool bWrap = false;
		Element->TryGetBoolField(TEXT("wrap"), bWrap);
		Text->SetAutoWrapText(bWrap);

		FSlateFontInfo Font = Text->GetFont();
		Font.Size = URuntimeJsonWidgetLibrary::ResolveFontSize((int32)GetNumber(Element, TEXT("fontSize"), Font.Size));
		if (UFont* FontAsset = URuntimeJsonWidgetLibrary::ResolveFont(GetString(Element, TEXT("fontAsset"))))
		{
			Font.FontObject = FontAsset;
			Font.TypefaceFontName = NAME_None;
		}
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
	LastClickedId.Reset();
	LastNotifiedId.Reset();
	KeyActions.Reset();
	const TSharedPtr<FJsonObject>* KeyActionsObject = nullptr;
	if (Document->TryGetObjectField(TEXT("keyActions"), KeyActionsObject) && KeyActionsObject && KeyActionsObject->IsValid())
	{
		for (const auto& Pair : (*KeyActionsObject)->Values)
		{
			FString Action;
			if (Pair.Value.IsValid() && Pair.Value->TryGetString(Action) && !Action.IsEmpty())
			{
				KeyActions.Add(FName(*Pair.Key), MoveTemp(Action));
			}
		}
	}
	SetIsFocusable(!KeyActions.IsEmpty());

	UWidget* RootWidget = BuildElement(RootElement, WidgetTree);
	if (RootWidget == nullptr)
	{
		return false;
	}

	WidgetTree->RootWidget = RootWidget;
	return true;
}

FReply URuntimeJsonWidget::NativeOnPreviewKeyDown(const FGeometry& InGeometry, const FKeyEvent& InKeyEvent)
{
	if (const FString* Action = KeyActions.Find(InKeyEvent.GetKey().GetFName()))
	{
		if (!InKeyEvent.IsRepeat()) NotifyButtonClicked(*Action);
		return FReply::Handled();
	}
	return Super::NativeOnPreviewKeyDown(InGeometry, InKeyEvent);
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

	// Fallback for embedded PIE when Slate does not deliver the button event.
	// Cached Slate geometry already includes layout anchors and DPI scaling.
	APlayerController* PC = GetOwningPlayer();
	if (PC == nullptr || !PC->WasInputKeyJustPressed(EKeys::LeftMouseButton) ||
		!FSlateApplication::IsInitialized() || GetVisibility() == ESlateVisibility::HitTestInvisible)
	{
		return;
	}

	const FVector2D CursorPosition = FSlateApplication::Get().GetCursorPos();
	for (const TPair<FString, TObjectPtr<UWidget>>& Pair : ElementsById)
	{
		const UButton* Button = Cast<UButton>(Pair.Value.Get());
		if (Button && IsActiveButton(Button) &&
			Button->GetCachedGeometry().IsUnderLocation(CursorPosition))
		{
			NotifyButtonClicked(Pair.Key);
			break;
		}
	}
}

bool URuntimeJsonWidget::ConsumeClickedButton(FName& OutElementId)
{
	OutElementId = NAME_None;
	if (LastClickedId.IsEmpty())
	{
		return false;
	}
	OutElementId = FName(*LastClickedId);
	LastClickedId.Reset();
	return true;
}

bool URuntimeJsonWidget::SetElementText(const FString& ElementId, const FString& Text)
{
	return SetElementTextValue(ElementId, FText::FromString(Text));
}

bool URuntimeJsonWidget::SetElementTextValue(const FString& ElementId, const FText& Text)
{
	UWidget* Element = ElementsById.FindRef(ElementId).Get();
	if (UButton* Button = Cast<UButton>(Element))
	{
		Element = Button->GetContent();
	}
	if (UTextBlock* TextBlock = Cast<UTextBlock>(Element))
	{
		TextBlock->SetText(Text);
		if (UScrollBox* ScrollBox = Cast<UScrollBox>(TextBlock->GetParent())) ScrollBox->ScrollToStart();
		return true;
	}
	return false;
}

bool URuntimeJsonWidget::SetElementProgress(const FString& ElementId, float Percent)
{
	if (UProgressBar* Bar = Cast<UProgressBar>(ElementsById.FindRef(ElementId).Get()))
	{
		Bar->SetPercent(FMath::Clamp(Percent, 0.0f, 1.0f));
		return true;
	}
	return false;
}

bool URuntimeJsonWidget::TryGetElementViewportRect(const FString& ElementId,
	FVector2D& Origin, FVector2D& Size) const
{
	Origin = FVector2D::ZeroVector;
	Size = FVector2D::ZeroVector;
	const UWidget* Element = ElementsById.FindRef(ElementId).Get();
	if (Element == nullptr)
	{
		return false;
	}

	const FGeometry& Geometry = Element->GetCachedGeometry();
	FVector2D End, ViewportPosition;
	USlateBlueprintLibrary::LocalToViewport(this, Geometry, FVector2D::ZeroVector, Origin, ViewportPosition);
	USlateBlueprintLibrary::LocalToViewport(this, Geometry, Geometry.GetLocalSize(), End, ViewportPosition);
	Size = End - Origin;
	return Size.X > 0 && Size.Y > 0 && !Size.ContainsNaN() && !Origin.ContainsNaN();
}

bool URuntimeJsonWidget::SetElementRenderTranslation(const FString& ElementId, FVector2D Translation)
{
	if (UWidget* Element = ElementsById.FindRef(ElementId).Get())
	{
		Element->SetRenderTranslation(Translation);
		return true;
	}
	return false;
}

bool URuntimeJsonWidget::SetSwitcherPage(const FString& SwitcherId, const FString& PageId)
{
	UWidgetSwitcher* Switcher = Cast<UWidgetSwitcher>(ElementsById.FindRef(SwitcherId).Get());
	UWidget* Page = ElementsById.FindRef(PageId).Get();
	if (!Switcher || !Page || Page->GetParent() != Switcher)
	{
		return false;
	}
	if (Switcher->GetActiveWidget() != Page)
	{
		Switcher->SetActiveWidget(Page);
		LastClickedId.Reset();
		LastNotifiedId.Reset();
	}
	return true;
}

bool URuntimeJsonWidget::SetElementRenderOpacity(const FString& ElementId, float Opacity)
{
	if (UWidget* Element = ElementsById.FindRef(ElementId).Get())
	{
		Element->SetRenderOpacity(FMath::Clamp(Opacity, 0.f, 1.f));
		return true;
	}
	return false;
}

bool URuntimeJsonWidget::SetElementVisible(const FString& ElementId, bool bVisible)
{
	if (UWidget* Element = ElementsById.FindRef(ElementId).Get())
	{
		Element->SetVisibility(bVisible
			? (Element->IsA<UButton>() || Element->IsA<UScrollBox>() ? ESlateVisibility::Visible : ESlateVisibility::SelfHitTestInvisible)
			: ESlateVisibility::Collapsed);
		return true;
	}
	return false;
}

bool URuntimeJsonWidget::SetElementImage(const FString& ElementId, UTexture2D* Texture,
	float U0, float V0, float U1, float V1)
{
	if (UImage* Image = Cast<UImage>(ElementsById.FindRef(ElementId).Get()))
	{
		FSlateBrush Brush;
		if (Texture != nullptr)
		{
			Brush.SetResourceObject(Texture);
			Brush.DrawAs = ESlateBrushDrawType::Image;
			Brush.SetUVRegion(FBox2f(FVector2f(U0, V0), FVector2f(U1, V1)));
			Brush.SetImageSize(FVector2D(Texture->GetSizeX() * (U1 - U0), Texture->GetSizeY() * (V1 - V0)));
		}
		else
		{
			Brush.DrawAs = ESlateBrushDrawType::NoDrawType;
		}
		Image->SetBrush(Brush);
		return true;
	}
	return false;
}

UWidget* URuntimeJsonWidget::BuildElement(const TSharedPtr<FJsonObject>& Element, UWidgetTree* Tree)
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
				if (UWidget* Child = BuildElement(ChildObject, Tree))
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
		const double CornerRadius = GetNumber(Element, TEXT("cornerRadius"), 0.0);
		if (CornerRadius > 0)
		{
			Border->SetBrush(FSlateRoundedBoxBrush(FLinearColor::White, static_cast<float>(CornerRadius)));
		}
		if (GetColor(Element, TEXT("color"), Color))
		{
			Border->SetBrushColor(Color);
		}
		Border->SetPadding(ParsePadding(Element, TEXT("padding"), FMargin(0.0f)));

		const TSharedPtr<FJsonObject>* ChildField = nullptr;
		if (Element->TryGetObjectField(TEXT("child"), ChildField) && ChildField != nullptr)
		{
			if (UWidget* Child = BuildElement(*ChildField, Tree))
			{
				Border->SetContent(Child);
			}
		}
		Result = Border;
	}
	else if (Type.Equals(TEXT("switcher"), ESearchCase::IgnoreCase))
	{
		UWidgetSwitcher* Switcher = Tree->ConstructWidget<UWidgetSwitcher>(UWidgetSwitcher::StaticClass());
		const TArray<TSharedPtr<FJsonValue>>* Children = nullptr;
		if (Element->TryGetArrayField(TEXT("children"), Children) && Children)
		{
			for (const TSharedPtr<FJsonValue>& ChildValue : *Children)
			{
				if (ChildValue.IsValid() && ChildValue->Type == EJson::Object)
				{
					if (UWidget* Child = BuildElement(ChildValue->AsObject(), Tree))
					{
						Switcher->AddChild(Child);
					}
				}
			}
		}
		Switcher->SetActiveWidgetIndex(0);
		Result = Switcher;
	}
	else if (Type.Equals(TEXT("text"), ESearchCase::IgnoreCase))
	{
		Result = MakeText(Tree, Element, TEXT(""));
	}
	else if (Type.Equals(TEXT("image"), ESearchCase::IgnoreCase))
	{
		UImage* Image = Tree->ConstructWidget<UImage>(UImage::StaticClass());
		bool bPreserveAspect = false;
		Element->TryGetBoolField(TEXT("preserveAspect"), bPreserveAspect);
		if (bPreserveAspect)
		{
			UScaleBox* ScaleBox = Tree->ConstructWidget<UScaleBox>(UScaleBox::StaticClass());
			ScaleBox->SetStretch(EStretch::ScaleToFit);
			ScaleBox->AddChild(Image);
			if (!Id.IsEmpty()) ElementsById.Add(Id, Image);
			return ScaleBox;
		}
		Result = Image;
	}
	else if (Type.Equals(TEXT("scrollbox"), ESearchCase::IgnoreCase))
	{
		UScrollBox* ScrollBox = Tree->ConstructWidget<UScrollBox>(UScrollBox::StaticClass());
		const TSharedPtr<FJsonObject>* ChildField = nullptr;
		if (Element->TryGetObjectField(TEXT("child"), ChildField) && ChildField)
		{
			if (UWidget* Child = BuildElement(*ChildField, Tree)) ScrollBox->AddChild(Child);
		}
		Result = ScrollBox;
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

		FButtonStyle ButtonStyle = Button->GetStyle();
		ButtonStyle.Normal.TintColor = FSlateColor(Normal);
		ButtonStyle.Hovered.TintColor = FSlateColor(Hover);
		ButtonStyle.Pressed.TintColor = FSlateColor(Pressed);
		Button->SetStyle(ButtonStyle);

		UTextBlock* Label = MakeText(Tree, Element, TEXT("Button"));
		Button->AddChild(Label);

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
		if (Type.Equals(TEXT("canvas"), ESearchCase::IgnoreCase) || Type.Equals(TEXT("switcher"), ESearchCase::IgnoreCase))
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
