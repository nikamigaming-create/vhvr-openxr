# VHVR OpenXR v0.1.0

OpenXR runtime and interaction additions for Valheim VR. This download contains the XR companion and its required Unity OpenXR runtime files. Install official VHVR and BepInEx separately. The Nikami gameplay mod and illustrated launcher are not included.

## Requirements

- Windows Valheim 1.0.16 (Unity 6000.0.75f1).
- BepInExPack Valheim already installed.
- Official VHVR v0.10.5 already installed: https://github.com/brandonmousseau/vhvr-mod/releases/tag/v0.10.5
- A working PC OpenXR runtime and tracked controllers. Meta Quest Link is the runtime for a Quest using USB Link or Air Link. Touch controller bindings have simulator validation.

VHVR v0.10.5 is the latest official published release checked on September 29, 2026. The fork source is synchronized with upstream master at 3ccd2a3c81cacb4208e8a2b0a7536ac4be09eccb. This companion binary is tested against the published v0.10.5 gameplay DLL, not a rebuilt unreleased master DLL. New upstream source revisions must be validated with their matching actions and assets before replacing that dependency.

## Included XR additions

- Native Unity OpenXR headset rendering and controller input; SteamVR's compositor is bypassed in OpenXR mode. VHVR retains its rig, locomotion, combat, building and VR menus.
- Controller-driven native finger articulation, including independent thumbs-up and index pointing, open hands, fists and contact-constrained fingers. This is controller input animation, not optical controller-free hand tracking.
- Solid hand and equipment contact with native surfaces, sustained pressure and sliding, bounded release when tracking moves too far, and equipment contact feedback.
- Physical grip, lift, rotation, drop and throwing of original loose items. Held objects remain native networked dynamic rigid bodies. Finite spring forces hold them; gravity compensation stops at release and native momentum is preserved with safety limits.
- Bounded restraint of eligible small creatures with native AI and damage retained. Holds last up to eight seconds and consume eight stamina per second. Grip release, tracking/focus loss, death, excessive separation or lost ownership ends the hold.
- Input priority and scene-transition fixes, native stereo occlusion/FXAA corrections, and a guarded display-session recovery path.

The existing Nikami.OpenXR.dll filename and plugin identifier are retained for compatibility. The separate Nikami gameplay DLL is not a dependency. Its optional integration stays inactive when that mod is absent.

## Install and launch

1. Save and quit Valheim. Install the requirements above first.
2. Keep this download's openxr folder and openxr-manifest.json together.
3. From this extracted folder, run:

       powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Install-OpenXR.ps1 -GameDirectory "D:\SteamLibrary\steamapps\common\Valheim"

   Replace the directory with your own Valheim location. The installer checks all payload hashes, backs up replaced files and records the installed manifest. Manual installation is also possible: copy the contents of openxr into the Valheim folder.
4. In Steam, set Valheim's launch options to:

       -ModEnabled=true -flatScreenMode=false

5. Connect the headset through the PC's active OpenXR runtime, then start Valheim normally. Keep the desktop game window visible.

Use -ModEnabled=false for desktop play. The optional -vrbackend=steamvr flag retains VHVR's original backend and bypasses this companion. Do not install a second copy of the companion under another plugin folder.

## Interaction controls

With an empty hand, bring the palm close to a loose object, then press and hold grip (the Touch controller's side button) to hold it. Release grip to drop or throw. Acquisition requires a new grip press; release and press again if grip was already held before reaching the object. Physical grabbing does not add the object to inventory. Trigger, grip and capacitive thumb touch drive the finger poses.

Creature restraint applies to eligible small ground creatures such as necks, boars and graylings, subject to their size, mass, level and ownership. Other players, bosses, flying or swimming creatures and ridden creatures cannot be held. Stronger creatures push the player back.

## Validation and limits

The shipped adapter is the unchanged tested build with SHA-256 E57B71754B70D55F9DEFF95EF5204F9E054486E187E1761EEF59EF5D61A022DB. Prior native simulator runs exercised both-eye rendering, articulated fingers, surface/weapon contact, dynamic item lifting/release, creature restraint and forced-release recovery. Fresh release verification is recorded in TESTED.txt.

This is an experimental release. Simulator tests do not establish physical-headset comfort, tracking feel, haptics, physical frame pacing, all weapons or multiplayer coverage. Multipass/deferred rendering is used. Experimental single-pass shader caches, simulator software, QA/filming plugins, game data, saved worlds and Nikami gameplay/launcher files are excluded.

## Source and notices

Corresponding companion source is in the separate VHVR-OpenXR-v0.1.0-source.zip download, with SOURCE-PROVENANCE.json recording the retained build sources. Release repository: https://github.com/nikamigaming-create/vhvr-openxr
GPL-3.0 license: LICENSE-GPL-3.0.txt. Unity OpenXR notices: openxr-licenses. The required Unity OpenXR version is 1.16.1.

To build the companion from this repository, install the .NET 8 SDK and the requirements above, then run from the repository root:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\OpenXR\scripts\Build-OpenXR.ps1 -ValheimDir "D:\SteamLibrary\steamapps\common\Valheim"

The script obtains the official Unity OpenXR 1.16.1 package and checks its pinned checksum. It uses your own Valheim, VHVR and BepInEx assemblies as build references and writes the companion payload to OpenXR/dist/openxr-runtime. Those owned assemblies and official VHVR dependencies are not redistributed here.
