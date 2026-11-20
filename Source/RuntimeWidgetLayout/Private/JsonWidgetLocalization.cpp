#include "JsonWidgetLocalization.h"

#include "Dom/JsonObject.h"
#include "HAL/FileManager.h"
#include "Internationalization/Culture.h"
#include "Internationalization/Internationalization.h"
#include "Internationalization/PolyglotTextData.h"
#include "Internationalization/TextLocalizationManager.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#include "Serialization/JsonReader.h"
#include "Serialization/JsonSerializer.h"

namespace JsonWidgetLocalization
{
namespace
{
	constexpr const TCHAR* ConfigSection = TEXT("RuntimeWidgetLayout.Localization");

	struct FLocalizationState
	{
		bool bInitialized = false;
		bool bEditorPreviewInitialized = false;
		FString NativeCulture = TEXT("en");
		FString Namespace = TEXT("JsonUMG");
		FString Language;
		TMap<FString, TMap<FString, FString>> Translations;
		TMap<FString, FPolyglotTextData> Entries;
		TSet<FString> ConflictingKeys;
	};

	FLocalizationState State;

	bool ApplyLanguage(const FString& Language)
	{
#if WITH_EDITOR
		if (GIsEditor)
		{
			GConfig->SetString(TEXT("Internationalization"), TEXT("PreviewGameLanguage"),
				*Language, GEditorPerProjectIni);
			FTextLocalizationManager::Get().EnableGameLocalizationPreview(Language);
			return true;
		}
#endif
		return FInternationalization::Get().SetCurrentLanguageAndLocale(Language);
	}

	void Initialize()
	{
		if (State.bInitialized) return;
		State.bInitialized = true;
		GConfig->GetString(ConfigSection, TEXT("NativeCulture"), State.NativeCulture, GGameIni);
		GConfig->GetString(ConfigSection, TEXT("Namespace"), State.Namespace, GGameIni);

		FString Directory;
		GConfig->GetString(ConfigSection, TEXT("LanguageDirectory"), Directory, GGameIni);
		if (!Directory.IsEmpty())
		{
			Directory = FPaths::ConvertRelativePathToFull(FPaths::ProjectDir(), Directory);
			TArray<FString> Files;
			IFileManager::Get().FindFiles(Files, *(Directory / TEXT("Lang-*.json")), true, false);
			Files.Sort();
			for (const FString& File : Files)
			{
				FString Json, Culture;
				TSharedPtr<FJsonObject> Document;
				const TSharedPtr<FJsonObject>* Strings = nullptr;
				if (!FFileHelper::LoadFileToString(Json, *(Directory / File)) ||
					!FJsonSerializer::Deserialize(TJsonReaderFactory<>::Create(Json), Document) ||
					!Document.IsValid() || !Document->TryGetStringField(TEXT("culture"), Culture) ||
					!Document->TryGetObjectField(TEXT("strings"), Strings) || !Strings || !Strings->IsValid())
				{
					UE_LOG(LogTemp, Warning, TEXT("[JsonUMG localization] Invalid language file: %s"), *File);
					continue;
				}
				const FCulturePtr ResolvedCulture = FInternationalization::Get().GetCulture(Culture);
				if (!ResolvedCulture.IsValid())
				{
					UE_LOG(LogTemp, Warning, TEXT("[JsonUMG localization] Unknown culture '%s' in %s"), *Culture, *File);
					continue;
				}
				auto& Translations = State.Translations.FindOrAdd(ResolvedCulture->GetName());
				for (const auto& Pair : (*Strings)->Values)
				{
					FString Translation;
					if (Pair.Value.IsValid() && Pair.Value->TryGetString(Translation))
					{
						const FString Key(*Pair.Key);
						if (Translations.Contains(Key))
						{
							UE_LOG(LogTemp, Warning, TEXT("[JsonUMG localization] Duplicate key '%s' in %s"), *Key, *File);
						}
						else
						{
							Translations.Add(Key, MoveTemp(Translation));
						}
					}
				}
			}
		}

		State.Language = State.NativeCulture;
#if WITH_EDITOR
		if (!GIsEditor)
#endif
		{
			if (!ApplyLanguage(State.Language)) State.Language = State.NativeCulture;
		}
		UE_LOG(LogTemp, Display, TEXT("[JsonUMG localization] Loaded %d language packs; native=%s selected=%s"),
			State.Translations.Num(), *State.NativeCulture, *State.Language);
	}
}

FText Resolve(const FString& Key, const FString& NativeText)
{
	if (Key.IsEmpty() || NativeText.IsEmpty()) return FText::FromString(NativeText);
	Initialize();
	if (FPolyglotTextData* Existing = State.Entries.Find(Key))
	{
		if (Existing->GetNativeString() == NativeText) return Existing->GetText();
		if (!State.ConflictingKeys.Contains(Key))
		{
			State.ConflictingKeys.Add(Key);
			UE_LOG(LogTemp, Warning, TEXT("[JsonUMG localization] Conflicting native text for '%s'"), *Key);
		}
		return FText::AsCultureInvariant(NativeText);
	}

	FPolyglotTextData Entry(ELocalizedTextSourceCategory::Game, State.Namespace, Key,
		NativeText, State.NativeCulture);
	for (const auto& Culture : State.Translations)
	{
		if (const FString* Translation = Culture.Value.Find(Key))
			Entry.AddLocalizedString(Culture.Key, *Translation);
	}
	FText Text = Entry.GetText();
	State.Entries.Add(Key, MoveTemp(Entry));
#if WITH_EDITOR
	if (GIsEditor && !State.bEditorPreviewInitialized)
	{
		State.bEditorPreviewInitialized = true;
		ApplyLanguage(State.Language);
	}
#endif
	return Text;
}

FString GetLanguage()
{
	Initialize();
	return State.Language;
}

bool SetLanguage(const FString& Language)
{
	Initialize();
	const FCulturePtr Culture = FInternationalization::Get().GetCulture(Language);
	if (!Culture.IsValid()) return false;
	const FString Name = Culture->GetName();
	if (Name != State.NativeCulture && !State.Translations.Contains(Name)) return false;
	if (!ApplyLanguage(Name)) return false;
	State.Language = Name;
	UE_LOG(LogTemp, Display, TEXT("[JsonUMG localization] Language changed to %s"), *Name);
	return true;
}

void Reset()
{
	State = FLocalizationState();
}
}
