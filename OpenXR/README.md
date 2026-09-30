# VHVR OpenXR v0.1.0 release candidate

One Valheim VR fork package containing current VHVR and its OpenXR additions: rebuilt gameplay and controller assemblies, matching bindings and upstream asset bundles, and VR runtime dependencies. Install BepInEx separately. The Nikami gameplay mod and illustrated launcher are excluded.

## Version and upstream

This candidate contains upstream master through [7fa70ef129a1f022365ebdc1e4ffdf1ff8836802](https://github.com/brandonmousseau/vhvr-mod/commit/7fa70ef129a1f022365ebdc1e4ffdf1ff8836802), checked September 29, 2026 Pacific time. It includes the screenshot, mirror, main-menu visibility and two-handed secondary attack changes available at that check. The rebuilt DLL retains upstream's internal 0.10.5 version string; the package manifest records its exact source revision and every runtime file's SHA-256.

Our current local OpenXR input fixes are included: live controller reads and nonvisual action updates before the game/UI sample them. A small fork correction guards the disabled upstream debug walking indicator against an uninitialized LineRenderer during startup, fixing the exception caught by this candidate's first game run. SOURCE-PROVENANCE.json records the correction and 19 adapter source files.

Unchanged VR dependencies come from the official [VHVR v0.10.5 archive](https://github.com/brandonmousseau/vhvr-mod/releases/tag/v0.10.5). Gameplay, controller assemblies, actions and tracked bundles are replaced with the current fork build. Users do not need a separate VHVR installation.

## Requirements

- Windows Valheim 1.0.16 (Unity 6000.0.75f1).
- BepInExPack Valheim installed in the game folder.
- A working PC OpenXR runtime and tracked controllers. Quest Link/Air Link uses the Meta Link PC runtime. Touch bindings have simulator validation.

## Included OpenXR features

- Native Unity OpenXR rendering and controller input, retaining VHVR locomotion, combat, building and VR menus. OpenXR mode bypasses SteamVR's compositor.
- Controller-driven native finger articulation: fists, open hands, independent thumb/index gestures and contact curl. Optical controller-free hand tracking is not implemented.
- Solid hands and held-weapon contact with native surfaces, sustained pressure, sliding, wrist rotation and feedback.
- Physical grab, lift, rotation, drop and throwing of original loose items. Native networked dynamic bodies are held by finite spring forces. Gravity stays enabled; compensation ends at release and native momentum is preserved with safety limits.
- Eligible small-creature restraint with native AI/damage retained, up to eight seconds and eight stamina per second.
- Scene/input priority fixes, stereo occlusion and FXAA corrections, and guarded display-session recovery.

Physics covers hands, held weapons, gripped loose items and eligible creatures. Hands/weapons use swept native collision constraints. More than 8 cm or 20 degrees of tracking separation from a blocked pose permits escape; contact reconnects after withdrawal. Normal hand/weapon contact has no timed expiry. Held objects release on grip release, tracking/focus loss, death, excessive separation or lost ownership. Normal player movement collision is retained.

The Nikami.OpenXR.dll filename/plugin identifier is retained for compatibility; the separate Nikami gameplay DLL is not required. Upstream bHaptics support/patterns are included for compatible hardware.

## Install and launch

1. Save and quit Valheim. Install BepInExPack Valheim first.
2. Extract the complete package, keeping openxr and openxr-manifest.json together.
3. From the extracted folder, run this command with your game directory:

       powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenXR.ps1 -GameDirectory "D:\SteamLibrary\steamapps\common\Valheim"

   The installer verifies hashes and backs up replaced files. Manual installation is possible by copying the contents of openxr into the game folder.
4. Set Valheim's Steam launch options to:

       -ModEnabled=true -flatScreenMode=false

5. Connect the headset through your active PC OpenXR runtime and start Valheim. Keep the desktop game window visible.

Use -ModEnabled=false for desktop play. The optional -vrbackend=steamvr flag uses upstream's original backend. Remove duplicate ValheimVRMod.dll or Nikami.OpenXR.dll files in other plugin folders when updating.

## Grabbing controls

With an empty hand, bring the palm near a loose object, then press and hold grip (Touch side button). Release to drop or throw. Acquisition requires a new grip press; release and press again if already held before approaching. Physical grabbing leaves the item in the world. Trigger, grip and capacitive thumb touch drive finger poses.

Eligible small ground creatures include necks, boars and graylings, subject to size, mass, level and ownership. Players, bosses, flying/swimming or ridden creatures cannot be held. Stronger creatures push the player back.

## Validation and limits

Read TESTED.txt for checks on these exact rebuilt DLLs. Earlier published-DLL tests are a baseline only. Physical headset feel, comfort, haptics and frame pacing await the final human playtest. Broad multiplayer and every weapon combination remain unproven.

Rendering uses multipass/deferred mode. Game binaries/data, BepInEx loader files, worlds, experimental single-pass caches, simulator/QA/filming plugins and Nikami gameplay/launcher files are excluded.

## Source and build

Repository: https://github.com/nikamigaming-create/vhvr-openxr. VHVR-OpenXR-v0.1.0-source.zip contains corresponding fork sources, build scripts, tracked VR assets and notices. See LICENSE-GPL-3.0.txt, openxr-licenses and third-party-notices.

Install .NET 8 SDK and BepInEx in your owned game installation. From this Git checkout, run:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\OpenXR\scripts\Build-CurrentFork.ps1 -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"

The script obtains/checks the pinned official VHVR dependency archive and Unity OpenXR 1.16.1 package, then builds VHVR/controllers/OpenXR into OpenXR/dist/current-fork. Owned assemblies are references only. Unity editor post-build scripts are not run. The checkout must contain the selected upstream commit and tracked Unity assets.
