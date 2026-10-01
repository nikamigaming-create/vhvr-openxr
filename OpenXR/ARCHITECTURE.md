# Shared gameplay and selectable VR backends

Version 0.2.0 uses one payload and one rebuilt gameplay DLL. The companion selects `openxr` or `openvr` before VHVR initializes its runtime. Selection is fixed for the process; there is no live runtime switch or automatic fallback.

## Ownership and boundary

| Layer | Source | Responsibility |
|---|---|---|
| Shared contract | `ValheimVRMod/VRCore/Backends/IVRBackend.cs` | SDK-free runtime/input interfaces using Unity values, action paths and neutral input sources |
| Shared action facade | `VRInput.cs`, `VRInputActions.cs` | Digital edges, axes, poses/velocities, binding state, action sets, update callbacks and haptics used by gameplay |
| Runtime host | `VRBackendHost`, `VRCore/VRManager.cs` | Selected backend lifecycle, startup lock and display/focus gates |
| Original OpenVR implementation | `Backends/OpenVRRuntime.cs`, `SteamVRInputBackend.cs` | Upstream OpenVR loader/init/mirror behavior and the Valve SDK input adapter |
| OpenXR implementation | `OpenXR/src/nikami-openxr/OpenXRBackend.cs`, `OpenXRPlugin.cs`, `RuntimeAdapter.cs`, `InputAdapter.cs` | Unity OpenXR lifecycle, live native controller reads, authored rig compatibility and session recovery |
| Shared hand/object physics | `OpenXR/src/shared-gameplay/` | Articulation, swept hand/equipment contact, impact policy, native object/creature forces and release policy, installed for either backend |
| Shared recenter policy | `Backends/VRRecenter.cs` | Active Unity XR input subsystem recenter plus the existing VHVR roomscale/pelvis/height policy |

Combat, locomotion, building, interaction and menu action consumers use the neutral facade. The OpenVR provider delegates to the original SDK. The OpenXR provider currently reuses Valve's managed action/event machinery through a compatibility bridge, while native Unity OpenXR supplies rendering and controller values. This keeps the existing authored rig and action update ordering; it does not initialize OpenVR's native compositor in OpenXR mode.

In OpenVR mode the companion does not install OpenXR loader, settings or CVR interception patches. It installs the same shared hand/physics gameplay and observes the original native display's focus. Invalid selection or failed runtime initialization cannot fall back to another backend.

## Remaining SDK integration

This is an input/lifecycle abstraction, not removal of every SteamVR type from VHVR. The authored `Hand`/`Player` prefab integration, tracked-pose components, fades, optional OpenVR overlay/keyboard and SteamVR body tracker provider remain SDK-specific. Shared physics has authored rig hook signatures using those components; its contact/force rules and action reads use the neutral contract. Historical `OpenXR*` class names and the `Nikami.OpenXR.dll` identifier are retained.

OpenXR binding-editor support is explicitly unavailable. OpenXR uses world-space GUI and packaged bindings. Optional OpenVR overlay/keyboard and tracker features are retained on their original backend; native OpenXR equivalents require further implementation and validation. A future provider can replace runtime/action implementations without rewriting shared gameplay consumers, but replacing the authored rig or implementing those optional services also needs integration work.

## Keeping upstream current

The release records upstream master `d3739391ac419c05d71563cb736aabad9cd2e0b3`. The main gameplay changes are action/source/haptic substitutions and the small startup guard for the unused walking indicator. Runtime initialization was extracted from upstream `VRManager` into `OpenVRRuntime`; subsequent upstream loader or mirror fixes must be carried into that file.

After merging a new upstream snapshot:

1. Review upstream changes against the existing fork boundary. Apply new gameplay action consumers to the shared facade and transfer native OpenVR runtime changes into `OpenVRRuntime`.
2. Run `python OpenXR/scripts/Update-ActionFacade.py`. This reads both tracked upstream generated catalogs and preserves the exact action/action-set path spelling. New action types used by gameplay require a corresponding neutral contract implementation.
3. Update `OpenXR/FORK-CHANGES.json` with the reviewed gameplay/runtime differences and their reasons. The build wrapper rejects differences outside that list. Update the wrapper's selected upstream commit.
4. Build the single package, run contract checks, then exercise both native startup paths and the interaction acceptance suite. Do not infer one runtime's hardware behavior from the other.
5. Record the exact source and DLL hashes in `SOURCE-PROVENANCE.json`, the package manifest and `TESTED.md`.

This avoids freezing the fork to the old runtime, but upstream API changes still require review. A contract does not make future merges automatically conflict-free.

## Production contract checks

After `Build-CurrentFork.ps1`, run with an owned installation containing the build reference libraries:

```powershell
dotnet build OpenXR/tests/BackendContracts/BackendContracts.csproj -c Release -p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim"
dotnet OpenXR/tests/BackendContracts/bin/Release/net8.0/BackendContracts.dll "D:\SteamLibrary\steamapps\common\Valheim"
dotnet OpenXR/tests/BackendContracts/bin/Release/net8.0/BackendContracts.dll "D:\SteamLibrary\steamapps\common\Valheim" failure
```

These tests load the production gameplay assembly. Fake providers exercise the same shared API under both backend kinds, including digital/axis callbacks, poses, tracking loss, haptics, action-set arbitration, cleanup, failed startup, restart-only selection and stale display rejection. Actual Valve action-set dictionary lookups also guard the upstream catalog capitalization regression found by the native OpenVR smoke check. Native rendering/input playback and physical headset feel are separate gates.
