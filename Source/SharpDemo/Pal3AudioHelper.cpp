// Pal3AudioHelper.cpp
#include "Pal3AudioHelper.h"

#include "Engine/Engine.h"
#include "Engine/World.h"
#include "Components/AudioComponent.h"
#include "Kismet/GameplayStatics.h"
#include "Sound/SoundWaveProcedural.h"
#include "dr_libs_mp3decoder.h"

// --- decode helpers -------------------------------------------------------------

namespace
{
	int32 ReadU16(const uint8* P) { return P[0] | (P[1] << 8); }
	uint32 ReadU32(const uint8* P) { return (uint32)P[0] | ((uint32)P[1] << 8) | ((uint32)P[2] << 16) | ((uint32)P[3] << 24); }

	void AppendInt16LE(TArray<uint8>& Out, int32 Sample)
	{
		const int16 S = (int16)FMath::Clamp(Sample, -32768, 32767);
		Out.Add((uint8)(S & 0xFF));
		Out.Add((uint8)((S >> 8) & 0xFF));
	}

	bool DecodeWav(const TArray<uint8>& In, TArray<uint8>& OutPcm, int32& OutSampleRate, int32& OutChannels)
	{
		if (In.Num() < 32 || FMemory::Memcmp(In.GetData(), "RIFF", 4) != 0 ||
			FMemory::Memcmp(In.GetData() + 8, "WAVE", 4) != 0)
		{
			return false;
		}

		int32 Pos = 12;
		int32 FormatTag = 0, Channels = 0, SampleRate = 0, BitsPerSample = 0;
		int32 DataOffset = 0, DataSize = 0;

		while (Pos + 8 <= In.Num())
		{
			const uint8* Chunk = In.GetData() + Pos;
			const uint32 ChunkSize = ReadU32(Chunk + 4);
			const int32 Body = Pos + 8;

			if (FMemory::Memcmp(Chunk, "fmt ", 4) == 0 && Body + 16 <= In.Num())
			{
				FormatTag = (int32)ReadU16(In.GetData() + Body);
				Channels = (int32)ReadU16(In.GetData() + Body + 2);
				SampleRate = (int32)ReadU32(In.GetData() + Body + 4);
				BitsPerSample = (int32)ReadU16(In.GetData() + Body + 14);
			}
			else if (FMemory::Memcmp(Chunk, "data", 4) == 0)
			{
				DataOffset = Body;
				DataSize = (int32)FMath::Min<uint32>(ChunkSize, (uint32)FMath::Max(0, In.Num() - Body));
			}

			const int32 Advance = 8 + (int32)ChunkSize + ((ChunkSize & 1) ? 1 : 0);
			if (Advance <= 8) break;
			Pos += Advance;
		}

		if (Channels <= 0 || SampleRate <= 0 || DataOffset <= 0 || DataSize <= 0)
		{
			return false;
		}

		OutChannels = Channels;
		OutSampleRate = SampleRate;
		OutPcm.Reset();

		const uint8* Data = In.GetData() + DataOffset;
		const int32 BytesPerSample = FMath::Max(1, BitsPerSample / 8);
		const int32 SampleCount = DataSize / BytesPerSample;
		OutPcm.Reserve(SampleCount * 2);

		if (FormatTag == 3 && BitsPerSample == 32) // IEEE float
		{
			for (int32 i = 0; i < SampleCount; i++)
			{
				float F; FMemory::Memcpy(&F, Data + i * 4, 4);
				AppendInt16LE(OutPcm, FMath::RoundToInt(FMath::Clamp(F, -1.0f, 1.0f) * 32767.0f));
			}
		}
		else if (BitsPerSample == 16)
		{
			for (int32 i = 0; i < SampleCount; i++)
			{
				AppendInt16LE(OutPcm, (int16)ReadU16(Data + i * 2));
			}
		}
		else if (BitsPerSample == 8)
		{
			for (int32 i = 0; i < SampleCount; i++)
			{
				AppendInt16LE(OutPcm, ((int32)Data[i] - 128) << 8);
			}
		}
		else
		{
			return false;
		}

		return OutPcm.Num() > 0;
	}

	bool DecodeMp3(const TArray<uint8>& In, TArray<uint8>& OutPcm, int32& OutSampleRate, int32& OutChannels)
	{
		static const int32 BitrateV1[16] = {0, 32, 40, 48, 56, 64, 80, 96, 112, 128, 160, 192, 224, 256, 320, 0};
		static const int32 BitrateV2[16] = {0, 8, 16, 24, 32, 40, 48, 56, 64, 80, 96, 112, 128, 144, 160, 0};
		static const int32 SampleRates[3][3] = {
			{44100, 48000, 32000},
			{22050, 24000, 16000},
			{11025, 12000, 8000},
		};

		int32 Pos = 0;
		if (In.Num() > 10 && FMemory::Memcmp(In.GetData(), "ID3", 3) == 0)
		{
			const uint8* H = In.GetData();
			const int32 TagSize = ((H[6] & 0x7F) << 21) | ((H[7] & 0x7F) << 14) |
			                      ((H[8] & 0x7F) << 7) | (H[9] & 0x7F);
			Pos = 10 + TagSize;
		}

		OutPcm.Reset();
		OutSampleRate = 0;
		OutChannels = 0;

		FMP3Decoder Decoder;
		TArray<float> FrameBuffer;
		int32 FramesDecoded = 0;

		while (Pos + 4 < In.Num())
		{
			const uint8* P = In.GetData() + Pos;
			if (!(P[0] == 0xFF && (P[1] & 0xE0) == 0xE0))
			{
				++Pos;
				continue;
			}

			const int32 Version = (P[1] >> 3) & 0x3;   // 3 = MPEG1, 2 = MPEG2, 0 = 2.5
			const int32 Layer = (P[1] >> 1) & 0x3;     // 1 = Layer III
			const int32 BitrateIndex = (P[2] >> 4) & 0xF;
			const int32 SampleRateIndex = (P[2] >> 2) & 0x3;
			const int32 Padding = (P[2] >> 1) & 0x1;
			const int32 ChannelMode = (P[3] >> 6) & 0x3;

			if (Layer != 1 || Version == 1 || BitrateIndex == 0 || BitrateIndex == 15 || SampleRateIndex == 3)
			{
				++Pos;
				continue;
			}

			const int32 BitrateKbps = (Version == 3 ? BitrateV1[BitrateIndex] : BitrateV2[BitrateIndex]);
			const int32 SampleRate = SampleRates[Version == 3 ? 0 : (Version == 2 ? 1 : 2)][SampleRateIndex];
			const int32 FrameLength = ((Version == 3 ? 144 : 72) * BitrateKbps * 1000 / SampleRate) + Padding;
			const int32 Channels = (ChannelMode == 3) ? 1 : 2;

			if (FrameLength < 8 || Pos + FrameLength > In.Num())
			{
				++Pos;
				continue;
			}

			const int32 MaxSamples = 1152 * Channels;
			FrameBuffer.SetNumUninitialized(MaxSamples);

			FMP3Decoder::FFrameInfo Info;
			const int32 Decoded = Decoder.Decode(&Info, FrameBuffer.GetData(),
				MaxSamples * (int32)sizeof(float), In.GetData() + Pos, FrameLength);

			if (Decoded > 0)
			{
				const int32 OutChannelsThis = Info.NumChannels > 0 ? Info.NumChannels : Channels;
				if (OutChannels == 0)
				{
					OutChannels = OutChannelsThis;
					OutSampleRate = Info.SampleRate;
				}
				const int32 SampleCount = Decoded * OutChannelsThis;
				for (int32 i = 0; i < SampleCount && i < FrameBuffer.Num(); i++)
				{
					AppendInt16LE(OutPcm, FMath::RoundToInt(FMath::Clamp(FrameBuffer[i], -1.0f, 1.0f) * 32767.0f));
				}
				++FramesDecoded;
			}

			Pos += FrameLength;
		}

		return FramesDecoded > 0 && OutPcm.Num() > 0;
	}

	struct FPal3AudioInstance
	{
		TStrongObjectPtr<USoundWaveProcedural> Wave;
		TWeakObjectPtr<UAudioComponent> Component;
		TArray<uint8> Pcm;
		int32 SampleRate = 22050;
		int32 Channels = 2;
		int32 LoopRemaining = 1; // -1 = infinite
		float Volume = 1.0f;
		bool bPlaying = false;
	};

	TMap<int32, TSharedPtr<FPal3AudioInstance>> GAudioInstances;
	int32 GNextAudioHandle = 1;
	int32 GDecodedCount = 0;
	int32 GFailedCount = 0;

	UWorld* FindAudioWorld()
	{
		if (GEngine == nullptr)
		{
			return nullptr;
		}
		for (const FWorldContext& Context : GEngine->GetWorldContexts())
		{
			if (Context.WorldType == EWorldType::PIE || Context.WorldType == EWorldType::Game)
			{
				if (UWorld* World = Context.World())
				{
					return World;
				}
			}
		}
		return nullptr;
	}

	UAudioComponent* SpawnProcedural(FPal3AudioInstance& Instance)
	{
		UWorld* World = FindAudioWorld();
		if (World == nullptr || !Instance.Wave.IsValid())
		{
			return nullptr;
		}

		Instance.Wave->QueueAudio(Instance.Pcm.GetData(), Instance.Pcm.Num());
		UAudioComponent* Component = UGameplayStatics::SpawnSound2D(World, Instance.Wave.Get(),
			Instance.Volume, 1.0f, 0.0f, nullptr, false, false);
		Instance.Component = Component;
		Instance.bPlaying = Component != nullptr;
		return Component;
	}
}

// --- public API -----------------------------------------------------------------

int32 UPal3AudioHelper::PlayAudio(const TArray<uint8>& Data, int32 AudioType, float Volume, int32 LoopCount)
{
	if (Data.Num() < 16)
	{
		++GFailedCount;
		return 0;
	}

	TArray<uint8> Pcm;
	int32 SampleRate = 0;
	int32 Channels = 0;

	const bool bOk = (AudioType == 6)
		? DecodeMp3(Data, Pcm, SampleRate, Channels)
		: DecodeWav(Data, Pcm, SampleRate, Channels);

	if (!bOk || Pcm.Num() < 2)
	{
		++GFailedCount;
		UE_LOG(LogTemp, Warning, TEXT("[Pal3Audio] decode failed type=%d bytes=%d"), AudioType, Data.Num());
		return 0;
	}

	++GDecodedCount;

	TSharedPtr<FPal3AudioInstance> Instance = MakeShared<FPal3AudioInstance>();
	Instance->Pcm = MoveTemp(Pcm);
	Instance->SampleRate = SampleRate;
	Instance->Channels = Channels;
	Instance->Volume = FMath::Clamp(Volume, 0.0f, 2.0f);
	Instance->LoopRemaining = LoopCount == 0 ? 1 : LoopCount;

	USoundWaveProcedural* Wave = NewObject<USoundWaveProcedural>(GetTransientPackage());
	Wave->SetSampleRate(SampleRate);
	Wave->NumChannels = Channels;
	Wave->Duration = (float)Instance->Pcm.Num() / (float)FMath::Max(1, SampleRate * Channels * 2);
	Wave->SoundGroup = SOUNDGROUP_Default;
	Wave->bLooping = false;
	Wave->AddToRoot();
	Instance->Wave = TStrongObjectPtr<USoundWaveProcedural>(Wave);

	if (SpawnProcedural(*Instance) == nullptr)
	{
		Wave->RemoveFromRoot();
		++GFailedCount;
		return 0;
	}

	const int32 Handle = GNextAudioHandle++;
	GAudioInstances.Add(Handle, Instance);
	if (Instance->LoopRemaining < 0)
	{
		--Instance->LoopRemaining; // -2 means "after this play, loop forever"
	}

	UE_LOG(LogTemp, Display, TEXT("[Pal3Audio] play handle=%d type=%d %.1fkHz x%d loops=%d bytes=%d"),
		Handle, AudioType, SampleRate / 1000.0f, Channels, LoopCount, Data.Num());
	return Handle;
}

void UPal3AudioHelper::StopAudio(int32 Handle)
{
	TSharedPtr<FPal3AudioInstance> Instance;
	if (!GAudioInstances.RemoveAndCopyValue(Handle, Instance) || !Instance.IsValid())
	{
		return;
	}
	if (Instance->Component.IsValid())
	{
		Instance->Component->Stop();
	}
	if (Instance->Wave.IsValid())
	{
		Instance->Wave->RemoveFromRoot();
	}
}

void UPal3AudioHelper::StopAllAudio()
{
	TArray<int32> Handles;
	GAudioInstances.GetKeys(Handles);
	for (int32 Handle : Handles)
	{
		StopAudio(Handle);
	}
}

void UPal3AudioHelper::SetAudioVolume(int32 Handle, float Volume)
{
	if (TSharedPtr<FPal3AudioInstance>* Found = GAudioInstances.Find(Handle))
	{
		(*Found)->Volume = FMath::Clamp(Volume, 0.0f, 2.0f);
		if ((*Found)->Component.IsValid())
		{
			(*Found)->Component->SetVolumeMultiplier((*Found)->Volume);
		}
	}
}

void UPal3AudioHelper::TickAudio()
{
	TArray<int32> ToRemove;

	for (const TPair<int32, TSharedPtr<FPal3AudioInstance>>& Pair : GAudioInstances)
	{
		FPal3AudioInstance& Instance = *Pair.Value;
		if (!Instance.bPlaying)
		{
			ToRemove.Add(Pair.Key);
			continue;
		}

		const bool bStillPlaying = Instance.Component.IsValid() && Instance.Component->IsPlaying();
		if (bStillPlaying)
		{
			continue;
		}

		if (Instance.LoopRemaining < 0 || Instance.LoopRemaining > 1)
		{
			if (Instance.LoopRemaining > 1)
			{
				--Instance.LoopRemaining;
			}
			if (SpawnProcedural(Instance) != nullptr)
			{
				continue;
			}
		}

		ToRemove.Add(Pair.Key);
	}

	for (int32 Handle : ToRemove)
	{
		StopAudio(Handle);
	}
}

FString UPal3AudioHelper::GetAudioStats()
{
	return FString::Printf(TEXT("decoded=%d failed=%d active=%d"),
		GDecodedCount, GFailedCount, GAudioInstances.Num());
}
