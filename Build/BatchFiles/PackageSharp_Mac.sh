

EngineDir="/Users/admin/Documents/Code/UnrealEngine/Engine"

Target=SharpDemo

UProject=$(pwd)/../../$Target.uproject

BuildTool=$(pwd)/../../Plugins/UnrealSharp/Binaries/Managed/UnrealSharpBuildTool

Param=" --Action PackageProject --EngineDirectory $EngineDir --ProjectDirectory $(pwd)/../../  \
--ProjectName $Target \
--PluginDirectory $(pwd)/../../Plugins/UnrealSharp \
--DotNetPath /usr/local/share/dotnet/dotnet \
--AdditionalArgs ArchiveDirectory=$(pwd)/../../Saved/StagedBuilds/Windows BuildConfig=Debug"

$BuildTool $Param

