# VHVR OpenXR / OpenVR v0.3.0 preview

[Download the public prerelease](https://github.com/nikamigaming-create/vhvr-openxr/releases/tag/openxr-v0.3.0). Download **VHVR-OpenXR-v0.3.0.zip** for installation; the separate source ZIP is for developers.

One package contains rebuilt VHVR gameplay, controller libraries, matching upstream bindings and asset bundles, and both runtime backends. Choose OpenXR or OpenVR at startup. Install BepInEx separately. The Nikami gameplay mod and launcher are excluded.

OpenXR now has direct native input and its own head/hand rig behind shared interfaces. The managed SteamVR input/rig compatibility bridge is removed. OpenVR keeps its original loader, SDK input and authored rig.

This is a community testing preview. Physical headset playability, comfort, haptics and full-resolution performance still need human testing; read [TESTED.md](TESTED.md) for the exact checks and limits.

## Version and upstream

Upstream master is merged through [c267f9d75dad21f1eac5c372d48c5a6f38003faa](https://github.com/brandonmousseau/vhvr-mod/commit/c267f9d75dad21f1eac5c372d48c5a6f38003faa), checked October 1, 2026. This adds all nine upstream commits since preview 0.2.0, including barber mirrors/reflexive IK, barber UI positioning, momentum-scaled attacks, damage texts, mirror configuration and upper-body IK stabilization. Inclusion of their source changes is established; each upstream feature has not been separately playtested.

The gameplay DLL retains upstream's internal 0.10.5 version string. The companion and release are version 0.3.0. `openxr-manifest.json` identifies the exact upstream snapshot, both backends and SHA-256 hashes for all 118 runtime files. `SOURCE-PROVENANCE.json` records adapter, shared physics and fork correction sources.

Unchanged VR dependencies come from the official [VHVR v0.10.5 archive](https://github.com/brandonmousseau/vhvr-mod/releases/tag/v0.10.5). Current gameplay/controller assemblies, actions and tracked bundles replace that archive's versions. A separate VHVR installation is unnecessary.

## Requirements

- Windows Valheim 1.0.16, Unity 6000.0.75f1.
- BepInExPack Valheim installed in the game folder.
- OpenXR: a working PC OpenXR runtime and tracked controllers. Quest Link/Air Link uses the Meta Link PC runtime. This mode uses native Unity OpenXR and the selected PC runtime; choosing Meta Link bypasses the SteamVR compositor.
- OpenVR: SteamVR installed and a working SteamVR headset/controller session. This mode uses the original upstream OpenVR loader and compositor.

## Install and choose a backend

1. Save and quit Valheim. Install BepInExPack Valheim first.
2. Extract the complete runtime package, keeping `openxr` and `openxr-manifest.json` together.
3. Run the installer from the extracted folder, substituting your game directory:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenXR.ps1 -GameDirectory "D:\SteamLibrary\steamapps\common\Valheim"
   ```

   The installer verifies every payload hash and backs up replaced files. Manual installation is possible by copying the contents of `openxr` into the game folder. Remove duplicate copies of `ValheimVRMod.dll` or `Nikami.OpenXR.dll` in other plugin folders when updating.
4. Set Valheim's Steam launch options to one of these:

   | Backend | Steam launch options |
   |---|---|
   | OpenXR | `-ModEnabled=true -flatScreenMode=false -vrbackend=openxr` |
   | OpenVR / SteamVR | `-ModEnabled=true -flatScreenMode=false -vrbackend=openvr` |

5. Connect the headset through the chosen runtime and start Valheim. Keep the desktop game window visible.

Alternatively, start once, quit, then set the generated `BepInEx/config/nikami.openxr.cfg`:

```ini
[Runtime]
Backend = openxr
```

Use `openvr` for SteamVR. A `-vrbackend` launch option overrides the config; `steamvr` remains an alias for `openvr`. The default is OpenXR. Restart the game after changing the backend. Unknown values stop VR startup with an error instead of silently choosing another runtime. Use `-ModEnabled=false` for desktop play.

Both choices use exactly the same package files. Keep the companion installed in OpenVR mode too: it selects the runtime and supplies optional shared gameplay. The historical DLL name and plugin identifier remain for installation compatibility.

## Added gameplay is opt-in

The following four independent settings apply to either backend and default to **false**, including when upgrading from 0.2.0:

```ini
[Gameplay]
EnablePhysicalContact = false
EnablePhysicalGrabbing = false
EnableCreatureGrabbing = false
EnableFingerArticulation = false
```

Restart after changing them. With all four off, no added physics/finger hooks are installed. Original VHVR combat collision, climbing, locomotion, building and VR menus remain available.

## Features

- VHVR locomotion, combat, building and VR menus with shared runtime, input and rig contracts. See [ARCHITECTURE.md](ARCHITECTURE.md) for the separate native providers and upstream maintenance.
- **Opt-in finger articulation:** controller-driven native joints: fists, open hands, independent thumb/index gestures and contact curl. Optical controller-free hand tracking is not implemented.
- **Opt-in physical contact:** solid hands and held-weapon contact with native surfaces, sustained pressure, sliding and wrist rotation. Haptic feedback accompanies contact.
- **Opt-in physical grabbing:** grab, lift, rotation, drop and throwing of original loose items. Native networked dynamic bodies are held by finite spring forces. Gravity stays enabled; compensation ends at release and native momentum is preserved with safety limits.
- **Opt-in creature grabbing:** eligible small-creature restraint with native AI/damage retained, up to eight seconds and eight stamina per second.
- Upstream bHaptics support and patterns for compatible hardware.

When enabled, added physics covers hands, held weapons, gripped loose items and eligible creatures. Hands/weapons use swept native collision constraints. More than 8 cm or 20 degrees of tracking separation from a blocked pose permits escape; contact reconnects after withdrawal. Normal hand/weapon contact has no timed expiry. Held objects release on grip release, tracking/focus loss, death, excessive separation or lost ownership. Normal player movement collision is retained.

OpenXR also supplies controller input updates before game/UI sampling, stereo occlusion and FXAA corrections, and guarded display-session recovery. OpenVR retains its native mirror, overlay, binding editor and tracker integration. Runtime-specific features do not have identical support: OpenXR uses packaged controller bindings and world-space GUI; SteamVR overlay keyboard/editor and full-body tracker behavior are not implemented as native OpenXR features.

## Grabbing controls

With physical grabbing enabled and an empty hand, bring the palm near a loose object, then press and hold grip (Touch side button). Release to drop or throw. Acquisition requires a new grip press; release and press again if already held before approaching. Physical grabbing leaves the item in the world. Trigger, grip and capacitive thumb touch drive finger poses.

Eligible small ground creatures include necks, boars and graylings, subject to size, mass, level and ownership. Players, bosses, flying/swimming or ridden creatures cannot be held. Stronger creatures push the player back.

## Validation and package contents

The native OpenXR simulator suite covers default gameplay, opted-in enhancements, the packaged DLLs and both final eyes. Original OpenVR startup is checked separately; this does not establish OpenVR gameplay physics or a human headset playtest. Broad multiplayer, every weapon/controller combination and physical headset frame pacing remain unproven. [TESTED.md](TESTED.md) / `TESTED.txt` records the evidence.

OpenXR rendering uses multipass/deferred mode. Game binaries/data, BepInEx loader files, worlds, experimental single-pass caches, simulator/QA/filming plugins and Nikami gameplay/launcher files are excluded.

[Shareable architecture PNG](https://github.com/nikamigaming-create/vhvr-openxr/releases/download/openxr-v0.3.0/VHVR-backend-architecture-v0.3.0.png).

## Source and build

The [repository](https://github.com/nikamigaming-create/vhvr-openxr) and `VHVR-OpenXR-v0.3.0-source.zip` contain corresponding fork sources, build scripts, backend contract tests, tracked VR assets and notices. See `LICENSE-GPL-3.0.txt`, `openxr-licenses` and `third-party-notices`.

Install .NET 8 SDK. Your owned game directory needs BepInEx and the VR libraries from this package or official VHVR v0.10.5 as build references. Run from this checkout:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\OpenXR\scripts\Build-CurrentFork.ps1 -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"
```

The script checks the pinned official dependency archive and Unity OpenXR 1.16.1 package, then builds both backends into `OpenXR/dist/current-fork`. Owned assemblies are references only. Unity editor post-build scripts are not run. The checkout must contain the selected upstream commit and tracked Unity assets. See [ARCHITECTURE.md](ARCHITECTURE.md) for updating upstream action catalogs and running contract tests.
