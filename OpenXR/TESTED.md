# VHVR OpenXR / OpenVR v0.2.0 validation

Validated October 1, 2026 with owned Valheim 1.0.16, Unity 6000.0.75f1, Unity OpenXR 1.16.1 and Meta XR Simulator v207. One package was installed in an isolated game fixture. Nikami gameplay was absent. Private test plugins, world data and simulator files are excluded from the release.

Upstream master: `d3739391ac419c05d71563cb736aabad9cd2e0b3`, rechecked October 1, 2026 before freezing this build. All 14 upstream commits since the previous preview are merged.

- `ValheimVRMod.dll` SHA-256: `7F95ED3D9493E1F46100D5C11B9C106209CBDEFACBB2FBBE2431E90A4375AEEC`.
- VR companion SHA-256: `4F5DB8C0BFB94E0DA8A9A91E2E4CF5FBB3E9C80332DD89761C049C3D44F0644E`.
- Matching controller assemblies, action catalogs and tracked bundles were tested with these DLLs. All 118 installed runtime files matched the frozen package manifest after testing.
- Source provenance covers 12 adapter files, eight shared gameplay files and 51 documented gameplay/runtime differences from upstream.

## Gates on the frozen package

| Gate | Result |
|---|---|
| Production backend contracts | 146 PASS checks: 73 each for successful and failed initialization. Both provider kinds exercise digital/axis callbacks, source mappings, poses, tracking loss, finger controls, haptics, action sets, cleanup, invalid choices, no fallback and restart-only selection. Real Valve action-set dictionary lookup regression checks also pass. |
| Native OpenXR startup | Passed using Unity OpenXR and the v207 simulator, live controller input and active stereo. |
| Native OpenVR startup | Passed explicit OpenVR selection, original loader/SteamVR initialization and native display start. SteamVR reported connection to Meta Quest 3 through its Oculus driver. Process modules contained `XRSDKOpenVR.dll` and `openvr_api.dll`; neither UnityOpenXR nor openxr_loader was loaded. No startup/runtime exceptions in the successful final smoke check. This is a startup check, not a human gameplay playtest. |
| Invalid backend startup | Invalid selection is rejected before either native VR runtime initializes; no silent runtime fallback. |
| Articulation and rig | All 30 native finger joints, fists, independent thumb/index gestures, reopen and contact curl passed. Neutral anatomical alignment passed. Both final eyes show hands, forearms and held equipment. |
| Hand/weapon contact and escape | Passed 1.2 m sweep against a 5 mm wall, sliding, wrist rotation, sustained pressure, opposing equipment contact, 768 corner pull/rotation poses, withdrawal/reconnection and avatar-reach escape. Native movement escaped a held pole without a locomotion latch. |
| Native surfaces | 42 fence/post/bed/chest paths, 3024 rubbing/rotation poses and 2659 contacts passed. |
| Native loose-item/creature grips | Normal grip acquired original networked dynamic bodies. Lift, rotation, inventory pickup protection, native momentum preservation and release passed. Live grayling restraint/free-hand damage and neck/boar eligibility passed; players, oversized and high-level creatures were rejected. |
| Gravity | Held drift 0.2 mm over 2.527 seconds of sampled observation. Release fell 1.646 m, minimum vertical velocity -6.679 m/s, final speed zero. Release samples span 8.514 seconds. Native gravity and constraints remained intact; disabled-gravity/non-falling negative fixtures were rejected. |
| Input and cleanup | 1536 simulator input frames, 1083 grip transitions and 12 focus/tracking recoveries across male/female avatars and native padded armor passed. Tracking/focus loss, inventory, separation, ownership, disable, equip and death released bodies and restored settings. |
| Impact and stereo evidence | Delivered axe closing speed 2.568 m/s exceeded the unchanged 1.4 m/s threshold. One clink per deliberate strike, sustained contact without repeats and gentle contact without clinks passed. Distinct nonblank compositor eyes and rendered contact geometry passed. Missing, black, UI-only and duplicate-eye negative fixtures were rejected. |
| Full OpenXR integration suite | **320 PASS, zero FAIL** in `run-04-final-isolated`; no runtime exceptions in this completed run. |
| Physical headset acceptance | **Pending** for both runtimes: human comfort, haptics, controller fit, frame pacing and full-resolution performance. OpenVR gameplay physics, broad multiplayer and every controller/weapon combination remain unproven. |

Contact solver telemetry recorded 17094 calls, mean 0.0556 ms/call, maximum 9.4013 ms, and zero left/right query saturation. These measurements describe this simulator run, not physical headset performance.

## Visual and timing limits

Both final compositor eyes were reviewed for open/closed hands, weapon contact, native wall contact, loose-item grip and live creature restraint. All 48 paired gravity samples were reviewed in order using recorded sample times. The item falls out of the lower view; native body telemetry establishes ground settling. PNGs measure 840 by 880 pixels per eye. The review video uses measured sample durations and is a sampled sequence, not continuous full-frame-rate capture.

The private physics checks used eye resolution scale 0.6. Compositor readback was paused during the short prescribed axe strike and resumed for sustained-contact evidence. Actual delivered motion was measured; a requested trajectory alone is not impact-speed evidence. Normal user graphics settings and physical thresholds were unchanged.

One run of these final DLLs, `run-03-final-package`, failed the short strike gate: a 635 ms trace gap left delivered closing speed at 0.240 m/s. This failed attempt is retained. The SteamVR smoke-check helper processes were then closed and the same DLLs/test trajectory passed in `run-04-final-isolated`. The precise cause of that stall is not established; the passing run does not erase it or establish reliable physical frame pacing.

An earlier candidate's native OpenVR smoke check exposed an action-set capitalization mismatch in the facade generator. The generator now reads the original action-set catalog verbatim and production contract tests cover the real SDK lookup. Earlier candidate tests are baseline evidence; the final hashes above were separately tested. The existing startup null guard for the unused walking indicator remains included.

Shared physics is installed under either backend. Hands/weapons use swept native constraints; loose items/eligible creatures use their original rigid bodies and finite forces. Contact permits escape after 8 cm or 20 degrees of tracking separation, which can temporarily allow penetration. Physical feel requires human judgment. Optional OpenVR overlay, keyboard, binding editor and body tracker services do not have identical native OpenXR support; see [ARCHITECTURE.md](ARCHITECTURE.md).
