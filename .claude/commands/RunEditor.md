# RunEditor — Launch SharpDemo Editor (Win64 Development)

Launches the Unreal Editor with the SharpDemo project.

## Steps

1. Launch the editor:
```bash
"E:/Code/UE5/Engine/Binaries/Win64/UnrealEditor.exe" "E:/UEProject/SharpDemo/SharpDemo.uproject" -log
```

2. The `-log` flag opens the output log window so you can see `LogUnrealSharp` messages.

For debugging Mono issues in the editor, check the output log for `[Mono]` prefixed messages.
