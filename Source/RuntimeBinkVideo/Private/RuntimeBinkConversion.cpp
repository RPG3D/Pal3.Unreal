#include "RuntimeBinkConversion.h"

#include "Async/Async.h"
#include "HAL/FileManager.h"
#include "HAL/PlatformProcess.h"
#include "Kismet/GameplayStatics.h"
#include "Misc/FileHelper.h"
#include "Misc/Guid.h"
#include "Misc/Paths.h"
#include "Misc/ScopeExit.h"

#if PLATFORM_ANDROID
#include "Android/AndroidApplication.h"
#include "Android/AndroidJNI.h"
#endif

namespace
{
	double ReadBinkDurationMicroseconds(const FString& Input)
	{
		TUniquePtr<FArchive> Reader(IFileManager::Get().CreateFileReader(*Input));
		uint32 Header[9] = {};
		if (!Reader || Reader->TotalSize() < sizeof(Header)) return 0;
		for (uint32& Field : Header) *Reader << Field;
		constexpr uint32 BinkSignature = 'B' | ('I' << 8) | ('K' << 16);
		if (Reader->IsError() || (Header[0] & 0x00FFFFFF) != BinkSignature || !Header[7] || !Header[8]) return 0;

		// Bink header fields 2, 7 and 8 are frame count, fps numerator and fps denominator.
		return 1000000.0 * Header[2] * Header[8] / Header[7];
	}

	bool RunConverter(const FString& Input, const FString& Output, const FString& ProgressPath)
	{
#if PLATFORM_ANDROID
		JNIEnv* Env = FAndroidApplication::GetJavaEnv();
		if (!Env || !FJavaWrapper::GameActivityThis) return false;
		jmethodID Method = FJavaWrapper::FindMethod(Env, FJavaWrapper::GameActivityClassID,
			"AndroidThunkJava_ConvertBink", "(Ljava/lang/String;Ljava/lang/String;Ljava/lang/String;)Z", true);
		if (!Method) return false;
		auto JavaInput = FJavaHelper::ToJavaString(Env, Input);
		auto JavaOutput = FJavaHelper::ToJavaString(Env, Output);
		auto JavaProgress = FJavaHelper::ToJavaString(Env, ProgressPath);
		return Env->CallBooleanMethod(FJavaWrapper::GameActivityThis, Method, *JavaInput, *JavaOutput, *JavaProgress);
#elif PLATFORM_WINDOWS
		FString Executable = FPlatformMisc::GetEnvironmentVariable(TEXT("RUNTIME_BINK_FFMPEG"));
		if (Executable.IsEmpty())
		{
			Executable = FPaths::ConvertRelativePathToFull(
				FPaths::ProjectDir() / TEXT("Binaries/ThirdParty/FFmpeg/Win64/ffmpeg.exe"));
		}
		if (!IFileManager::Get().FileExists(*Executable))
		{
			UE_LOG(LogTemp, Warning, TEXT("RuntimeBinkVideo could not find FFmpeg. Set RUNTIME_BINK_FFMPEG to ffmpeg.exe."));
			return false;
		}

		const FString Arguments = FString::Printf(TEXT("-nostdin -hide_banner -loglevel error -progress \"%s\" -y -i \"%s\" ")
			TEXT("-map 0:v:0 -map 0:a:0? -c:v libx264 -preset veryfast -threads 2 -pix_fmt yuv420p ")
			TEXT("-c:a aac -movflags +faststart \"%s\""), *ProgressPath, *Input, *Output);
		int32 ExitCode = -1;
		FString Error;
		const bool bStarted = FPlatformProcess::ExecProcess(*Executable, *Arguments, &ExitCode, nullptr, &Error);
		if (!bStarted || ExitCode != 0)
		{
			UE_LOG(LogTemp, Warning, TEXT("RuntimeBinkVideo converter failed (%d): %s"), ExitCode, *Error);
			return false;
		}
		return true;
#else
		return false;
#endif
	}
}

URuntimeBinkConversion* URuntimeBinkConversion::ConvertStagedFile(UObject* WorldContextObject, const FString& Input, const FString& Destination)
{
	if (FPaths::IsRelative(Input) || FPaths::IsRelative(Destination) ||
		!Input.EndsWith(TEXT(".staged.bik")) || !Destination.EndsWith(TEXT(".mp4")) ||
		FPaths::GetPath(Input) != FPaths::GetPath(Destination))
	{
		return nullptr;
	}

	UGameInstance* GameInstance = UGameplayStatics::GetGameInstance(WorldContextObject);
	if (!GameInstance) return nullptr;

	auto* Conversion = NewObject<URuntimeBinkConversion>();
	// Managed wrappers do not keep their native UObjects alive across UE GC.
	Conversion->RegisterWithGameInstance(GameInstance);
	const FString Temporary = Destination + TEXT(".") + FGuid::NewGuid().ToString(EGuidFormats::Digits) + TEXT(".mp4");
	Conversion->ProgressPath = Temporary + TEXT(".progress");
	Conversion->DurationMicroseconds = ReadBinkDurationMicroseconds(Input);
	Conversion->Job = Async(EAsyncExecution::ThreadPool, [Input, Destination, Temporary, ProgressPath = Conversion->ProgressPath]()
	{
		IFileManager& Files = IFileManager::Get();
		ON_SCOPE_EXIT
		{
			Files.Delete(*Input, false, true);
			Files.Delete(*Temporary, false, true);
			Files.Delete(*ProgressPath, false, true);
		};
		if (Files.FileSize(*Destination) > 0) return true;
		if (!RunConverter(Input, Temporary, ProgressPath) || Files.FileSize(*Temporary) <= 0) return false;
		const int64 ExistingSize = Files.FileSize(*Destination);
		if (ExistingSize > 0) return true;
		if (ExistingSize == 0) Files.Delete(*Destination, false, false, true);
		const bool bPublished = Files.Move(*Destination, *Temporary, false, false, false, true);
		if (bPublished) UE_LOG(LogTemp, Display, TEXT("RuntimeBinkVideo cached %s"), *Destination);
		return bPublished || Files.FileSize(*Destination) > 0;
	});
	return Conversion;
}

int32 URuntimeBinkConversion::QueryResult()
{
	if (Job.IsValid() && !Job.IsReady()) return 0;
	const int32 Result = Job.IsValid() && Job.Get() ? 1 : -1;
	ReleaseHandle();
	return Result;
}

void URuntimeBinkConversion::ReleaseHandle()
{
	SetReadyToDestroy();
}

float URuntimeBinkConversion::QueryProgress()
{
	const double Now = FPlatformTime::Seconds();
	if (DurationMicroseconds <= 0 || Now < NextProgressRead) return Progress;
	NextProgressRead = Now + 0.25;

	FString Report;
	if (!FFileHelper::LoadFileToString(Report, *ProgressPath, FFileHelper::EHashOptions::None,
		FILEREAD_AllowWrite | FILEREAD_Silent)) return Progress;

	// FFmpeg can be appending a report while we read. Only use a complete time field.
	int32 LastNewline;
	if (!Report.FindLastChar('\n', LastNewline)) return Progress;
	constexpr TCHAR Key[] = TEXT("\nout_time_us=");
	const int32 KeyIndex = Report.Find(Key, ESearchCase::CaseSensitive, ESearchDir::FromEnd, LastNewline);
	if (KeyIndex == INDEX_NONE) return Progress;
	const int32 ValueIndex = KeyIndex + UE_ARRAY_COUNT(Key) - 1;
	const int32 EndIndex = Report.Find(TEXT("\n"), ESearchCase::CaseSensitive, ESearchDir::FromStart, ValueIndex);
	int64 Microseconds = 0;
	if (EndIndex > ValueIndex && LexTryParseString(Microseconds, *Report.Mid(ValueIndex, EndIndex - ValueIndex).TrimEnd()))
	{
		Progress = FMath::Max(Progress, static_cast<float>(FMath::Clamp(Microseconds / DurationMicroseconds, 0.0, 0.99)));
	}
	return Progress;
}
