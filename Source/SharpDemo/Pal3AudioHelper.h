// Pal3AudioHelper.h — runtime audio for the PAL3 port.
//
// PAL3 ships SFX as 16-bit WAV (snd/*.wav) and music as MPEG audio inside
// music.cpk. UE has no runtime "play these bytes" path for either format, so this
// helper decodes in-process (dr_libs FMP3Decoder for MPEG, a small RIFF parser for
// WAV) and feeds the PCM to a UProceduralSoundWave. No external plugin required.
#pragma once

#include "CoreMinimal.h"
#include "Kismet/BlueprintFunctionLibrary.h"
#include "Pal3AudioHelper.generated.h"

UCLASS()
class SHARPDEMO_API UPal3AudioHelper : public UBlueprintFunctionLibrary
{
	GENERATED_BODY()

public:
	/**
	 * Decodes and plays the given audio bytes.
	 * AudioType: 7 = WAV, 6 = MPEG (Unity AudioType values).
	 * LoopCount: -1 = loop forever, otherwise number of plays.
	 * Returns a handle (0 on failure) for Stop/SetVolume.
	 */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static int32 PlayAudio(const TArray<uint8>& Data, int32 AudioType, float Volume, int32 LoopCount);

	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static void StopAudio(int32 Handle);

	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static void StopAllAudio();

	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static void SetAudioVolume(int32 Handle, float Volume);

	/** Per-frame housekeeping: restarts looping instances that finished. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static void TickAudio();

	/** Diagnostics text: decoded/played/failed counters + active instances. */
	UFUNCTION(BlueprintCallable, Category = "PAL3|Audio")
	static FString GetAudioStats();
};
