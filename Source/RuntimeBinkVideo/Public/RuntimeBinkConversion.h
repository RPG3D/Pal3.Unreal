#pragma once

#include "Async/Future.h"
#include "Kismet/BlueprintAsyncActionBase.h"
#include "RuntimeBinkConversion.generated.h"

UCLASS()
class RUNTIMEBINKVIDEO_API URuntimeBinkConversion : public UBlueprintAsyncActionBase
{
	GENERATED_BODY()

public:
	/** Takes ownership of a temporary Bink file and atomically publishes an MP4 cache entry. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo", meta = (WorldContext = "WorldContextObject"))
	static URuntimeBinkConversion* ConvertStagedFile(UObject* WorldContextObject, const FString& Input, const FString& Destination);

	/** Returns 0 while working, 1 on success and -1 on failure. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo")
	int32 QueryResult();

	/** Returns 0 to 1 when FFmpeg reports progress, or -1 while unknown. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo")
	float QueryProgress();

	/** Releases an unobserved job handle. The worker still finishes and cleans up its files. */
	UFUNCTION(BlueprintCallable, Category = "RuntimeBinkVideo")
	void ReleaseHandle();

private:
	TFuture<bool> Job;
	FString ProgressPath;
	double DurationMicroseconds = 0;
	double NextProgressRead = 0;
	float Progress = -1.f;
};
