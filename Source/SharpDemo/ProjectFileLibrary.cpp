#include "ProjectFileLibrary.h"

#include "HAL/FileManager.h"
#include "HAL/PlatformFile.h"
#include "Misc/FileHelper.h"
#include "Misc/Paths.h"

#if PLATFORM_WINDOWS
#include "Windows/WindowsHWrapper.h"
#else
#include <cstdio>
#endif

bool UProjectFileLibrary::ReadText(const FString& Path, FString& Text)
{
	return FFileHelper::LoadFileToString(Text, *Path);
}

bool UProjectFileLibrary::ReadBytes(const FString& Path, TArray<uint8>& Bytes)
{
	return FFileHelper::LoadFileToArray(Bytes, *Path);
}

bool UProjectFileLibrary::WriteTextAtomic(const FString& Path, const FString& Text)
{
	IFileManager& Files = IFileManager::Get();
	const FString Directory = FPaths::GetPath(Path);
	if (!Directory.IsEmpty() && !Files.MakeDirectory(*Directory, true)) return false;
	const FString Temporary = Path + TEXT(".") + FGuid::NewGuid().ToString(EGuidFormats::Digits) + TEXT(".tmp");
	if (!FFileHelper::SaveStringToFile(Text, *Temporary, FFileHelper::EEncodingOptions::ForceUTF8WithoutBOM))
	{
		Files.Delete(*Temporary);
		return false;
	}
	// IFileManager::Move(Replace=true) first unlinks the destination. Use a
	// same-directory replace after UE has resolved sandbox/external-write paths.
	const FString Destination = Files.ConvertToAbsolutePathForExternalAppForWrite(*Path);
	const FString Source = Files.ConvertToAbsolutePathForExternalAppForWrite(*Temporary);
#if PLATFORM_WINDOWS
	const bool bMoved = ::MoveFileExW(*Source, *Destination, MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != 0;
#else
	const bool bMoved = ::rename(TCHAR_TO_UTF8(*Source), TCHAR_TO_UTF8(*Destination)) == 0;
#endif
	if (!bMoved) Files.Delete(*Temporary);
	return bMoved;
}

bool UProjectFileLibrary::FileExists(const FString& Path)
{
	return FPaths::FileExists(Path);
}

FString UProjectFileLibrary::GetProjectDirectory()
{
	return FPaths::ConvertRelativePathToFull(FPaths::ProjectDir());
}

FString UProjectFileLibrary::GetSavedDirectory()
{
	return IPlatformFile::GetPlatformPhysical().ConvertToAbsolutePathForExternalAppForWrite(*FPaths::ProjectSavedDir());
}
