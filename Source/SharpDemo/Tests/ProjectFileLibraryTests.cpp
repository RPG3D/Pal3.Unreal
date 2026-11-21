#include "../ProjectFileLibrary.h"

#if WITH_DEV_AUTOMATION_TESTS
#include "HAL/FileManager.h"
#include "Misc/AutomationTest.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"
#if PLATFORM_WINDOWS
#include "Windows/WindowsHWrapper.h"
#endif

IMPLEMENT_SIMPLE_AUTOMATION_TEST(FProjectAtomicTextWriteTest, "SharpDemo.Files.AtomicReplace",
	EAutomationTestFlags::EditorContext | EAutomationTestFlags::ClientContext | EAutomationTestFlags::CommandletContext | EAutomationTestFlags::EngineFilter)

bool FProjectAtomicTextWriteTest::RunTest(const FString&)
{
	const FString Directory = FPaths::ProjectSavedDir() / TEXT("Automation/AtomicReplace") / FGuid::NewGuid().ToString(EGuidFormats::Digits);
	const FString Path = Directory / TEXT("preference.txt");
	TestTrue(TEXT("Create the first complete value"), UProjectFileLibrary::WriteTextAtomic(Path, TEXT("旧值")));
	TestTrue(TEXT("Replace an existing file"), UProjectFileLibrary::WriteTextAtomic(Path, TEXT("new value")));
	FString Value;
	TestTrue(TEXT("Read replacement"), UProjectFileLibrary::ReadText(Path, Value));
	TestEqual(TEXT("Complete replacement contents"), Value, FString(TEXT("new value")));
#if PLATFORM_WINDOWS
	const FString Physical = IFileManager::Get().ConvertToAbsolutePathForExternalAppForRead(*Path);
	HANDLE Locked = ::CreateFileW(*Physical, GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
	TestTrue(TEXT("Lock destination against replacement"), Locked != INVALID_HANDLE_VALUE);
	if (Locked != INVALID_HANDLE_VALUE)
	{
		TestFalse(TEXT("Replacement fails while locked"), UProjectFileLibrary::WriteTextAtomic(Path, TEXT("must not replace")));
		::CloseHandle(Locked);
		UProjectFileLibrary::ReadText(Path, Value);
		TestEqual(TEXT("Failure preserves previous contents"), Value, FString(TEXT("new value")));
	}
#endif
	IFileManager::Get().DeleteDirectory(*Directory, false, true);
	return true;
}
#endif
