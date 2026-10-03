# Input adapter checks

These checks compile the production input adapter, controller profile storage, backend config choices and input contract. They use the tracked upstream controller layouts and a small action-priority fixture. Only the Unity clock, device controls, logging and native boundary are replaced. This proves managed behavior; it does not prove native timing, headset performance or haptics.

Requires the .NET 9 SDK, a local Valheim install and BepInEx. From the repository root:

```powershell
dotnet restore OpenXR/tests/InputAdapterChecks/InputAdapterChecks.csproj --configfile OpenXR/tests/InputAdapterChecks/NuGet.Config
dotnet run --no-restore --project OpenXR/tests/InputAdapterChecks/InputAdapterChecks.csproj -c Release -p:ValheimDir=D:/SteamLibrary/steamapps/common/Valheim
```

The suite covers skipped reads, separate hand/Any edges, chords, axis callbacks, action priority and exclusivity, imported press/release thresholds, same-frame tracking loss/recovery, device replacement, delayed haptics and shutdown. A warmed 500-frame allocation check covers sampling, mixed-case action reads and binding/set queries. `-p:AdapterSource=/absolute/path/InputAdapter.cs` can run the same checks against an older source file.

Profile checks cover Touch, Vive, Index and WMR controls, unsupported layouts, trackpad directions/scroll deltas, personal save/reload/reset, malformed JSON and recovery. Backend choices preserve mixed-case names, the SteamVR alias and rejection of unknown providers.
