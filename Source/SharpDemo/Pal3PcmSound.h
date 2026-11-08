#pragma once

#include "Sound/SoundWaveProcedural.h"
#include "Pal3PcmSound.generated.h"

UCLASS()
class UPal3PcmSound : public USoundWaveProcedural
{
	GENERATED_BODY()

public:
	void Initialize(TArray<uint8>&& InPcm, int32 InSampleRate, int32 InChannels, int32 InPlayCount);
	virtual ISoundGeneratorPtr CreateSoundGenerator(const FSoundGeneratorInitParams& InParams) override;

private:
	TSharedPtr<const TArray<uint8>, ESPMode::ThreadSafe> Pcm;
	int32 PlayCount = 1;
};
