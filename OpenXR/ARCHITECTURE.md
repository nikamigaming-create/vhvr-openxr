# Shared gameplay and selectable VR backends

Release 0.3.0 uses one payload and one rebuilt gameplay DLL. The companion selects `openxr` or `openvr` before VHVR initializes its runtime. Selection is fixed for the process; there is no live runtime switch or automatic fallback. See `TESTED.md` for validation and physical headset limits.

## Ownership and boundary

| Layer | Source | Responsibility |
|---|---|---|
| Shared contract | `ValheimVRMod/VRCore/Backends/IVRBackend.cs`, `VRRig.cs` | SDK-free runtime, input and rig interfaces using Unity values, action paths and neutral input sources |
| Shared action facade | `VRInput.cs`, `VRInputActions.cs` | Digital edges, axes, poses/velocities, binding state, action sets, update callbacks and haptics used by gameplay |
| Runtime host | `VRBackendHost`, `VRCore/VRManager.cs` | Selected backend lifecycle, startup lock and display/focus gates |
| Shared rig components | `VRRig.cs`, `VRLaserPointer.cs`, `VRFade.cs`, `VRShaders.cs` | Head/hand transforms, pose publication, velocities, pointers, fades and shader access |
| Original OpenVR implementation | `Backends/OpenVRRuntime.cs`, `SteamVRInputBackend.cs`, `OpenVRRigBackend.cs` | Upstream OpenVR loader/init/mirror behavior, Valve SDK input and authored rig; publishes poses into the shared rig |
| Native OpenXR implementation | `OpenXR/src/nikami-openxr/OpenXRBackend.cs`, `OpenXRPlugin.cs`, `InputAdapter.cs`, `OpenXRRigBackend.cs` | Unity OpenXR lifecycle, direct Input System controller/action reads and an independently constructed rig |
| OpenXR presentation | `RuntimeAdapter.cs` | Unity camera, rendering and scene-lifetime corrections; session recovery remains in the plugin |
| Optional hand/object physics | `OpenXR/src/shared-gameplay/` | Articulation, swept hand/equipment contact, impact policy, native object/creature forces and release policy; four independent opt-ins, off by default |
| Shared recenter policy | `Backends/VRRecenter.cs` | Active Unity XR input subsystem recenter plus the existing VHVR roomscale/pelvis/height policy |

Combat, locomotion, building, interaction and menu consumers use the neutral facade and shared `VRHand`, `VRPoseDriver` and pointer components. The OpenVR provider delegates to the original SDK. The OpenXR provider implements the input interface directly: action paths resolve to native Input System controls, digital edges and callbacks are sampled once per game frame, and poses refresh during input and before render. Native tracking state validates a pose; stale nonzero positions cannot keep a grip alive.

OpenXR creates its head and hand objects directly. It never instantiates the Valve player prefab or creates Valve hand, pose, pointer, render-model or runtime behaviours. The former fake SteamVR runtime and CVRInput interception are removed. The companion itself has no SteamVR assembly reference. Its historical DLL name and plugin identifier remain compatible with existing installations.

In OpenVR mode the companion selects the original loader and authored rig. The OpenVR rig provider copies native pose publication into the shared components, so gameplay does not need Valve hand types. The companion observes that display's focus and installs optional gameplay only when selected. Invalid selection or failed initialization cannot fall back to another backend.

## Optional services and packaged libraries

The single package retains Valve SDK/controller assemblies, authored assets and shader names for its OpenVR backend. Deserializing an asset or using a shader named `SteamVR_*` is different from executing Valve runtime/input code. OpenXR's input and rig paths do not execute that SDK. `Valve.Newtonsoft.Json` is the game's JSON library, used to read the upstream binding file.

OpenXR uses world-space GUI and packaged bindings. SteamVR's overlay keyboard, binding editor, mirror modes and body-tracker provider remain OpenVR services. OpenXR text fields keep their native panel and accept a physical keyboard; they do not start a SteamVR overlay keyboard. Optical hand tracking and native OpenXR body tracking are not implemented. These services are explicit feature gaps, not a hidden managed input bridge.

The optional Nikami gameplay integration was removed. The package supplies VHVR and its runtime providers; it does not contain a Nikami gameplay mod or launcher.

## Added gameplay options

The companion binds these settings in `BepInEx/config/nikami.openxr.cfg`, for either backend:

```ini
[Gameplay]
EnablePhysicalContact = false
EnablePhysicalGrabbing = false
EnableCreatureGrabbing = false
EnableFingerArticulation = false
```

Restart after changing them. With all four disabled, no added physics/finger hooks are installed. Contact enables added hand/weapon constraints and impact feedback. Item grabbing enables loose-item spring forces and throwing. Creature grabbing enables eligible creature restraint/repelling. Finger articulation enables controller-driven native joints and contact curl. Each option gates its own behavior; none implies the others. Upstream combat collision, climbing and other original gameplay remain part of VHVR.

## Keeping upstream current

The release merges upstream master `c267f9d75dad21f1eac5c372d48c5a6f38003faa`, nine commits newer than release 0.2.0, checked October 1, 2026. Shared gameplay changes are predominantly action/source/haptic and head/hand/pointer substitutions. Runtime initialization was extracted from upstream `VRManager` into `OpenVRRuntime`; subsequent upstream loader or mirror fixes must be carried into that file.

After merging a new upstream snapshot:

1. Review upstream changes against the existing fork boundary. Apply new gameplay action consumers to the shared facade and transfer native OpenVR runtime changes into `OpenVRRuntime`.
2. Run `python OpenXR/scripts/Update-ActionFacade.py`. This reads both tracked upstream generated catalogs and preserves the exact action/action-set path spelling. New action types used by gameplay require a corresponding neutral contract implementation.
3. Update `OpenXR/FORK-CHANGES.json` with the reviewed gameplay/runtime differences and their reasons. The build wrapper rejects differences outside that list. Update the wrapper's selected upstream commit.
4. Build the single package, run contract checks, then exercise both native startup paths and the interaction acceptance suite. Do not infer one runtime's hardware behavior from the other.
5. Record the exact source and DLL hashes in `SOURCE-PROVENANCE.json`, the package manifest and `TESTED.md`.

Upstream developers can continue their work. This fork merges their commits, reviews new SDK calls at the boundary, and tests both providers. Upstream can adopt the abstraction independently of the optional physics. The interfaces do not make arbitrary future merges automatic or conflict-free. Replacing this fork's gameplay DLL with an unmodified upstream DLL is unsupported: the companion expects the shared contracts compiled into the fork.

## Production contract checks

After `Build-CurrentFork.ps1`, run with an owned installation containing the build reference libraries:

```powershell
dotnet build OpenXR/tests/BackendContracts/BackendContracts.csproj -c Release -p:ValheimDir="D:\SteamLibrary\steamapps\common\Valheim"
dotnet OpenXR/tests/BackendContracts/bin/Release/net8.0/BackendContracts.dll "D:\SteamLibrary\steamapps\common\Valheim"
dotnet OpenXR/tests/BackendContracts/bin/Release/net8.0/BackendContracts.dll "D:\SteamLibrary\steamapps\common\Valheim" failure
```

These tests load the production gameplay assembly. Fake providers exercise the same shared API under both backend kinds, including digital/axis callbacks, poses, tracking loss, haptics, action-set arbitration, cleanup, failed startup, restart-only selection and stale display rejection. Actual Valve action-set dictionary lookups also guard the upstream catalog capitalization regression found by the native OpenVR smoke check. Native rendering/input playback and physical headset feel are separate gates.
