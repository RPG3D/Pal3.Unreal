// Fill out your copyright notice in the Description page of Project Settings.


#include "SharpGameFuncLib.h"
#include "Kismet/KismetSystemLibrary.h"

UObject* USharpGameFuncLib::LoadAsset(const FString& InAssetPath)
{
	UObject* LoadedAsset = UKismetSystemLibrary::LoadAsset_Blocking(TSoftObjectPtr<UObject>(FSoftObjectPath(InAssetPath)));
	return LoadedAsset;
}
