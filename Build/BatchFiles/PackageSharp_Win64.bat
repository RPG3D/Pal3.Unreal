

set EngineDir="E:/Code/UnrealEngine/Engine/"

set Target=SharpDemo

set UProject=%~dp0/../../%Target%.uproject

set BuildTool=%~dp0/../../Plugins/UnrealSharp/Binaries/Managed/UnrealSharpBuildTool.exe

set Param= --Action PackageProject --EngineDirectory %EngineDir% --ProjectDirectory %~dp0/../../  ^
--ProjectName %Target% ^
--PluginDirectory "%~dp0/../../Plugins/UnrealSharp" ^
--DotNetPath "C:\Program Files\dotnet\dotnet.exe" ^
--AdditionalArgs ArchiveDirectory="%~dp0/../../Saved/StagedBuilds/Windows" BuildConfig=Debug

call %BuildTool% %Param%
