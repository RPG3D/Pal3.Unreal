using UnrealSharp.GameplayTags;

public static class GameplayTags
{
	public static readonly FGameplayTag EnhancedInput = new("EnhancedInput");
	public static readonly FGameplayTag EnhancedInput_Modes = new("EnhancedInput.Modes");
	public static readonly FGameplayTag EnhancedInput_Modes_Default = new("EnhancedInput.Modes.Default");
	public static readonly FGameplayTag InputUserSettings = new("InputUserSettings");
	public static readonly FGameplayTag InputUserSettings_FailureReasons = new("InputUserSettings.FailureReasons");
	public static readonly FGameplayTag InputUserSettings_FailureReasons_InvalidMappingName = new("InputUserSettings.FailureReasons.InvalidMappingName");
	public static readonly FGameplayTag InputUserSettings_FailureReasons_NoKeyProfile = new("InputUserSettings.FailureReasons.NoKeyProfile");
	public static readonly FGameplayTag InputUserSettings_FailureReasons_NoMappingRowFound = new("InputUserSettings.FailureReasons.NoMappingRowFound");
	public static readonly FGameplayTag InputUserSettings_FailureReasons_NoMatchingMappings = new("InputUserSettings.FailureReasons.NoMatchingMappings");
	public static readonly FGameplayTag InputUserSettings_Profiles = new("InputUserSettings.Profiles");
	public static readonly FGameplayTag InputUserSettings_Profiles_Default = new("InputUserSettings.Profiles.Default");
}