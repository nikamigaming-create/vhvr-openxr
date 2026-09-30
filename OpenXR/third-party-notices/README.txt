Dependency provenance for VHVR OpenXR v0.1.0

VHVR gameplay and this fork's OpenXR source: GPL-3.0, repository root LICENSE.
SteamVR controller runtime source: Valve Software, SteamVR-LICENSE.txt.
Valve.Newtonsoft.Json: retained JSON.NET and SaladLab notices in this folder.
Unity OpenXR 1.16.1: original LICENSE.md and Third Party Notices.md in
openxr-licenses. Native UnityOpenXR.dll and openxr_loader.dll are unmodified.

Other VR runtime dependencies (Final IK/root motion, bHaptics, NDesk.Options,
Unity XR Management/OpenVR/legacy helpers and OpenVR native libraries) are
carried unchanged from the official VHVR v0.10.5 release archive:
https://github.com/brandonmousseau/vhvr-mod/releases/tag/v0.10.5
Archive SHA256:
D7BCE3CD3663F0B8AF2A3044050A63F7A5417B409431E3363A744C2DC2D41003

SteamVR.dll and SteamVR_Actions.dll are rebuilt from the matching upstream
source. The tracked upstream VR bundles/actions are included. See
SOURCE-PROVENANCE.json and openxr-manifest.json for exact versions and hashes.
Game-owned assemblies are build references only and are excluded from this
distribution. BepInEx is a separate user installation requirement.
