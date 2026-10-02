# Input adapter checks

These checks compile the production `InputAdapter.cs` and input contract. They use the tracked Oculus Touch bindings and a small action-priority fixture. Only the Unity clock, device controls, logging and native boundary are replaced. This proves managed sampling behavior; it does not prove native input timing, headset performance or controller haptics.

Requires the .NET 9 SDK and `Valve.Newtonsoft.Json.dll` from a local Valheim install. From the repository root:

```powershell
dotnet restore OpenXR/tests/InputAdapterChecks/InputAdapterChecks.csproj --configfile OpenXR/tests/InputAdapterChecks/NuGet.Config
dotnet run --no-restore --project OpenXR/tests/InputAdapterChecks/InputAdapterChecks.csproj -c Release -p:ValheimDir=D:/SteamLibrary/steamapps/common/Valheim
```

The suite covers skipped reads, separate hand/Any edges, chords, axis callbacks, action priority and exclusivity, imported press/release thresholds, same-frame tracking loss/recovery, device replacement, delayed haptics and shutdown. A warmed 500-frame allocation check covers sampling, mixed-case action reads and binding/set queries. `-p:AdapterSource=/absolute/path/InputAdapter.cs` can run the same checks against an older source file.
