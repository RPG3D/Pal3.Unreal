// Copyright Epic Games, Inc. All Rights Reserved.

#include "SharpDemoGameMode.h"
#include "SharpDemoCharacter.h"
#include "UObject/ConstructorHelpers.h"

ASharpDemoGameMode::ASharpDemoGameMode()
{
	// set default pawn class to our Blueprinted character
	static ConstructorHelpers::FClassFinder<APawn> PlayerPawnBPClass(TEXT("/Game/ThirdPerson/Blueprints/BP_ThirdPersonCharacter"));
	if (PlayerPawnBPClass.Class != NULL)
	{
		DefaultPawnClass = PlayerPawnBPClass.Class;
	}
}
