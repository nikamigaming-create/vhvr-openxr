# VHVR OpenXR development branch

This branch adds an optional native Unity OpenXR runtime companion to official
VHVR v0.10.5. VHVR continues to own its rig, combat, building and UI. Its gameplay
DLL is unchanged. The companion replaces OpenVR runtime initialization and
adapts OpenXR controller input to VHVR's existing managed action objects.

Nikami is optional. Its inventory-decoration integration activates only when
Nikami is installed; the companion does not contain the Nikami gameplay mod or
launcher. The existing `Nikami.OpenXR.dll` filename and plugin identifier are
retained for compatibility with installations managed by that launcher.

## Runtime selection

- Desktop: `-ModEnabled=false`.
- Native OpenXR with this companion installed: `-ModEnabled=true -flatScreenMode=false`.
- Original SteamVR backend: `-ModEnabled=true -flatScreenMode=false -vrbackend=steamvr`.
  The companion installs no runtime or input patches in this mode.

OpenXR uses the runtime selected on the PC, or a process-local `XR_RUNTIME_JSON`.
Meta Link is the runtime for a Quest connected through USB Link. VDXR is Virtual
Desktop's OpenXR runtime, not a separate VHVR backend or a requirement for Link.
The companion never changes the machine-wide runtime selection.

Rendering uses Unity OpenXR 1.16.1 with two eye passes every frame. SteamVR's
compositor is bypassed in OpenXR mode, while managed SteamVR action/rig classes
remain in use. This is a compatibility implementation, not removal of every
SteamVR type from VHVR. Finger skeletons and SteamVR full-body trackers are not
implemented by the OpenXR companion. Only Touch bindings have simulator checks.

## Build

Use an owned Windows Valheim installation with BepInEx and official VHVR v0.10.5:

```powershell
./OpenXR/scripts/Build-OpenXR.ps1 -ValheimDir 'D:\SteamLibrary\steamapps\common\Valheim'
```

The script retrieves the pinned official Unity OpenXR package and verifies its
checksum. Output is in `OpenXR/dist/openxr-runtime`, with a payload manifest and
Unity notices. Builds do not install into the game. Game assemblies, VHVR asset
bundles and simulator files are not added to this repository or payload.

With Valheim closed, copy `Install-OpenXR.ps1` alongside the generated
`openxr-manifest.json`, `openxr` and `openxr-licenses` directories, then run it
with `-GameDirectory` pointing at the game. Do not install two copies of the
companion DLL. Install Nikami and its launcher separately if desired.

## Status

Development branch; not a release or a claim of complete gameplay compatibility.
Meta XR Simulator checks on Valheim 1.0.12 have exercised world-camera ownership,
inventory, locomotion, Nikami item placement, a bronze-sword hit at the unchanged
VHVR swing threshold, and controller-driven hammer crafting. Crafting checks
include exact inventory and nearby Nikami storage costs, UI input priority, and
rejection of insufficient resources. Combat uses a stationary spawned target;
these checks do not cover all weapons, full progression or multiplayer.

Physical Quest Link latency, comfort, haptics and performance are unverified.
The original SteamVR backend has not been rerun in this validation. Newer VHVR
releases require their own adapter validation.

The next upstream integration step is to expose backend selection within
VHVR's runtime boundary and replace the companion's private-method hooks with
that interface, while preserving the default SteamVR behavior.
