@echo off
echo === Pulling UE log file ===
adb pull /sdcard/Android/data/com.YourCompany.SharpDemo/files/UnrealGame/SharpDemo/SharpDemo/Saved/Logs/SharpDemo.log ue_log.txt 2>nul
if exist ue_log.txt (
    echo UE log saved to ue_log.txt
) else (
    echo UE log file not found on device ^(app may crash before log file is created^)
)

