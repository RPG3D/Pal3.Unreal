#include "ProjectStorageLibrary.h"

#include "HAL/PlatformFile.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/Paths.h"

#if PLATFORM_ANDROID
#include "Android/AndroidApplication.h"
#include "Android/AndroidJNI.h"

namespace
{
	bool CallStorageActivityMethod(const ANSICHAR* MethodName)
	{
		JNIEnv* Env = FAndroidApplication::GetJavaEnv();
		if (Env == nullptr || FJavaWrapper::GameActivityThis == nullptr)
		{
			return false;
		}
		const jmethodID Method = FJavaWrapper::FindMethod(
			Env, FJavaWrapper::GameActivityClassID, MethodName, "()Z", true);
		return Method != nullptr && Env->CallBooleanMethod(FJavaWrapper::GameActivityThis, Method);
	}
}
#endif

FString UProjectStorageLibrary::ResolveWritableSavedDirectory()
{
	return IPlatformFile::GetPlatformPhysical().ConvertToAbsolutePathForExternalAppForWrite(
		*FPaths::ProjectSavedDir());
}

float UProjectStorageLibrary::GetEngineConfigFloat(const FString& Section, const FString& Key, float DefaultValue)
{
	float Value = DefaultValue;
	return GConfig && GConfig->GetFloat(*Section, *Key, Value, GEngineIni) ? Value : DefaultValue;
}

bool UProjectStorageLibrary::HasSharedStorageAccess()
{
#if PLATFORM_ANDROID
	return CallStorageActivityMethod("AndroidThunkJava_HasSharedStorageAccess");
#else
	return true;
#endif
}

bool UProjectStorageLibrary::RequestSharedStorageAccess()
{
#if PLATFORM_ANDROID
	return CallStorageActivityMethod("AndroidThunkJava_RequestSharedStorageAccess");
#else
	return false;
#endif
}
