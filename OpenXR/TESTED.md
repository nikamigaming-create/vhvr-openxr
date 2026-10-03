# VHVR OpenXR / OpenVR validation

## 0.3.2 controller editor and camera fix

Checked October 3, 2026 against live upstream master `d78db84588c166431652c6fc50378e62b876079c`. Official `v0.10.5` and both newer master fixes remain included.

- Complete 118-file package builds with zero errors. Legacy Unity/SDK warnings remain.
- **275 managed checks pass:** 150 backend contracts, 80 input/profile/config checks, 31 grab lifecycle checks and 14 frame/focus checks. Production version and payload guards pass. The warmed 500-frame input workload allocates zero bytes.
- Native OpenXR daytime run: the rig copies the authored camera tag, `Utils.GetMainCamera()` returns `VRCamera`, day fraction is 0.5 and native sunlight intensity is 1.7. Lighting updates in both final eyes.
- Native XR pointer opens the controller editor, adds an input, saves personal JSON, reopens, resets defaults and retains the previous-layout backup. The native settings screen saves a changed physics opt-in and displays the backend chooser. Physics/backend changes require restart.
- Final-eye captures show native hands/fingers, sword/shield and axe during small wrist motions and locomotion. A native dynamic wood item is acquired, lifted, rotated and released with gravity. Withdrawal succeeds; the contact take enters the safety yield state. It does not establish sustained pressure/contact acceptance.
- Captures use Meta XR Simulator v207, scripted semantic controller input, a staged private save and native Valheim audio. Both final compositor eyes are recorded at 840 x 880 each and 30 fps. The edited walkthrough shows the left eye and labelled menu close-ups; the full stereo recording retains all seven clips at recorded speed.

Gameplay SHA-256: `51646A70EFE716ADF3F3C39756CCBA70C1C21637EDB74387678643A9820A7D2F`. Companion SHA-256: `D9DF62EE4DB6424BF0C4DD29CB22137AACB08F9AAC367F2E53DA6AB86CECC17C`.

Human headset hand angles, comfort, haptics and full-resolution performance remain unverified. The controller layouts have managed coverage; native controller testing here uses Touch. OpenVR controller playback and creature restraint were not freshly rechecked on these DLLs. This is an experimental community preview.

## 0.3.1 upstream sync

Checked October 3, 2026 against live upstream master `d78db84588c166431652c6fc50378e62b876079c`. The latest official release remains `v0.10.5`; its tag and the latest master are both ancestors of this fork. The two new upstream commits are included: `b0f81d02` (small minimap layout handling) and `d78db845` (HUD failure isolation). The minimap source matches upstream exactly; the HUD keeps only the existing shared-backend substitutions and loop cleanup.

- Complete 118-file package builds with zero errors. Legacy Unity/SDK warnings remain.
- **236 managed checks pass:** 150 backend contracts, 41 input checks, 31 grab lifecycle checks and 14 render/focus checks. Production packaging version and stale-file guards pass.
- Action facade regeneration produces the same two sets and 42 actions. OpenXR/OpenVR selection and all four disabled-by-default options are unchanged.
- Gameplay SHA-256: `4F6DF23EBDD450A1ED636A523F4E63E1920B09EFCDDB9E0A4E1ED8F5DE80F18D`; companion SHA-256: `949DADA38A1F0414024E711C38DB30D849ACAB14AAD6AFB907193C0E65E953F5`.

Fresh native and physical headset acceptance is pending for these DLLs, including the minimap changes. This remains an experimental community preview. Earlier native results below apply to their recorded builds.

## Code sweep candidate

Checked October 1, 2026 after `7e1475a`, using upstream `c267f9d75dad21f1eac5c372d48c5a6f38003faa`. This is a new local candidate. The published 0.3.0 package is unchanged.

- Reviewed input, rig, optional physics, rendering, UI, resource ownership and packaging. No new unbounded world scan found. The underwater grid stays bounded by its existing resolution limit.
- Fixed skipped input edges, stale velocity after tracking loss, failed-grab cleanup, finger-only climbing hooks and the shield/base render callback guard.
- Removed repeated input/bow allocations, idle bounds work, unused diagnostics and the duplicate scene-resource owner. Added cleanup for owned materials, textures, meshes, cameras, components and callbacks.
- **236 managed checks pass:** 150 backend contracts, 41 input checks, 31 grab lifecycle checks and 14 render/focus checks. Packaging checks accept matching versions and listed files, reject mismatched versions and stale/hidden plugins, and preserve files.
- The warmed input allocation workload dropped from **432,000 bytes to zero over 500 frames** against the prior adapter. This measures the managed test workload, not native frame time.
- The complete package builds with zero errors. Unity/SDK compatibility and legacy source warnings remain. All **118 runtime hashes and 87 source hashes** pass: 12 adapter, seven shared gameplay and 68 core corrections. The compiled companion has no SteamVR assembly reference or dormant single-pass renderer.
- Gameplay SHA-256: `C7F5EF28ACE5B7450AA729C008293706F0573F61B1136524CC69BEDDFCBD1088`; companion SHA-256: `E10E934A29F80DB0C27C6A87D199CBC36FB441665DE748615DB0B7DF9E4E62F2`.

Native acceptance has not been repeated for these DLLs. Test repeated UI/equipment cycles, underwater effects, scaled rig velocity, startup/shutdown and physical hand/controller behavior. The earlier native results below apply to their recorded DLLs.

## Unreleased source cleanup after 0.3.0

Checked October 1, 2026 against the same upstream `c267f9d75dad21f1eac5c372d48c5a6f38003faa`; upstream master was rechecked and had not advanced. The legacy build entry point produced the complete 118-file payload. All payload hashes and 83 source provenance entries were verified (11 adapter, seven optional gameplay and 65 core corrections).

- Production backend contracts: **150 PASS** (75 successful and 75 failed initialization checks).
- Native OpenXR: **24 PASS, zero FAIL** with additions disabled in `cleanup-default-02`; **332 PASS, zero FAIL** with all additions enabled in `cleanup-enhanced-02`. Both final eyes were reviewed for 12 default stereo pairs, seven enhanced feature pairs and all 48 paired gravity samples. Native gravity dropped the released body 1.646 m and settled it at zero speed.
- Compiled companion: unused single-pass renderer/color-pass types absent, no SteamVR SDK assembly references, and no optional gameplay references from the baseline equipment visibility guard.
- Native OpenVR: simulated startup passed with all four additions enabled in `cleanup-openvr-null-01`, using the installed SteamVR null-HMD driver. Native OpenVR modules loaded; OpenXR modules were absent. Controller gameplay and physical headset acceptance remain unproven.
- Gameplay SHA-256: `4A98D889A87C6DFF5BC44FAC08FAF5D2D066584261BF20E7D1B8762ED8A75C55`; companion SHA-256: `FF0EF31ADC16472989F7E7A723A8A5D0C6DDA87857964D8413471DBA7FC2D74E`.

The isolated fixture's 120 original file states were restored and verified. The earlier enhanced cleanup run was aborted before completion to tighten the non-player attachment guard; it is not counted as a pass. These checks retain the simulator, resolution and physical-headset limits below. The published 0.3.0 artifacts retain their original hashes and validation.

## Published 0.3.0

Validated October 1, 2026 with owned Valheim 1.0.16, Unity 6000.0.75f1, Unity OpenXR 1.16.1 and Meta XR Simulator v207. The frozen package was installed in an isolated game fixture. Nikami gameplay was absent. Private test plugins, owned game/world data, simulator files and captures are excluded from the release.

Upstream master: `c267f9d75dad21f1eac5c372d48c5a6f38003faa`, checked October 1, 2026 at approximately 17:54 PDT. All nine upstream commits since preview 0.2.0 are merged, including the latest upper-body IK activation. Their source inclusion is verified; each upstream feature has not been separately playtested.

- `ValheimVRMod.dll` SHA-256: `33F1F8C45D7CC57B4CF4BDCE3875C21868EAA08F4606E3C81115C56622AEFA52`.
- Companion SHA-256: `CA14A1A1BD471FF4350881CB0614BF0BE1AEBEB99E72FBEB45B1237A84EC1672`.
- All 118 runtime files match the frozen manifest. Corresponding source provenance covers 12 adapter sources, eight shared gameplay sources and 64 documented gameplay/runtime differences from upstream.

## Gates on the frozen package

| Gate | Result |
|---|---|
| Production backend contracts | **150 PASS**: 75 checks each for successful and failed initialization. Both provider kinds exercise digital/axis callbacks, source mappings, poses, tracking loss, finger controls, haptics, action sets, cleanup, invalid choices, no fallback and restart-only selection. Actual Valve action-set dictionary regression checks and SDK-free input/rig signatures pass. |
| Native OpenXR boundary | Passed at title and world: direct Input System provider, native head/both-hand tracking, zero Valve scene behaviours and no OpenXR-owned Valve/CVRInput patches. The companion has no SteamVR assembly reference. Unsupported waist tracking is unavailable rather than aliased to the head. |
| Default gameplay and stereo | **24 PASS, zero FAIL** in `native-default-latest-01`. All four additions were false; no added physics/finger hooks or components were installed at title or world. Native input, 1.99 m locomotion, combat damage, repeated inventory interaction, UI/world-click arbitration, hammer crafting with exact bag costs and insufficient-resource rejection passed. Twelve distinct nonblank eye pairs were captured during hand translation/wrist rotation; the world camera rendered 7730 times by exit. |
| Opted-in OpenXR suite | **332 PASS, zero FAIL** in `native-enhanced-latest-01`, with all four additions enabled. This separately tested the exact DLLs above after the final upstream merge. |
| Articulation and rig | All 30 native finger joints, fists, independent thumb/index gestures, reopen and contact curl passed. Both final eyes show hands, attached forearms and held equipment. |
| Contact and escape | Sweeps, sliding, wrist rotation, opposing equipment contact, corner pull/rotation, withdrawal/reconnection and avatar-reach escape passed. Eight pole-grab cycles and native locomotion escaped held contact. Native surfaces covered 42 fence/post/bed/chest paths, 3024 poses and 2659 contacts. |
| Impact and geometry | Delivered axe closing speed 1.949 m/s exceeded the unchanged 1.4 m/s threshold. Deliberate impact, gentle contact and sustained contact without repeated clinks passed. Dual-axe and shield contact geometry matched the rendered equipment centres. |
| Native loose-item/creature grips | Native dynamic/networked bodies, acquisition, lift, rotation, inventory pickup protection, momentum preservation and release passed. Eligible grayling/neck/boar handling and rejection of players, oversized and high-level creatures passed. |
| Gravity | Held drift 0.3 mm over 1.972 seconds. Release fell 1.726 m, minimum vertical velocity -6.168 m/s, final speed zero. Release samples span 6.891 seconds. Native gravity/constraints remained intact; disabled-gravity/non-falling negative fixtures were rejected. |
| Input and cleanup | 1536 simulator input frames, 931 observed grip transitions and 12 focus/tracking recoveries across male/female avatars and armor phases passed. Tracking/focus loss, inventory, separation, ownership, disable, equip and death released bodies and restored settings. |
| Native OpenVR startup | **Simulated startup passed** in `native-openvr-null-01`: installed SteamVR null-HMD driver, original SDK input/rig, native display start and title scene. Modules contained `XRSDKOpenVR.dll` and `openvr_api.dll`; UnityOpenXR/openxr_loader were absent. A private path-registry override isolated configuration/logs without changing global SteamVR settings. This stationary simulated HMD has no controllers and proves neither physical headset startup nor OpenVR gameplay. |
| Physical OpenVR attempt | **Failed / unproven** in `native-openvr-latest-01`: `Init_HmdNotFound` after the original loader retries, followed by a SteamVR critical-error dialog. No headset was available to this session. The failure is retained and is not counted as a pass. |
| Invalid backend startup | Passed in `native-invalid-latest-01`: expected rejection before either native runtime loaded, with no automatic fallback. |
| Physical headset acceptance | **Pending for both runtimes**: controller fit, hand/weapon feel, comfort, haptics, frame pacing and full-resolution performance. OpenVR controller gameplay/physics, individual opt-in combinations, broad multiplayer and every controller/weapon combination remain unproven. |

Contact solver telemetry recorded 23712 calls, mean 0.0436 ms/call, maximum 6.4951 ms and zero query saturation. These are simulator measurements, not headset performance guarantees. The completed OpenXR runs had no plugin runtime exceptions. Unity emitted warnings about destroying a Transform during scripted death cleanup; cleanup assertions passed and the warnings are retained.

## Visual and timing limits

Both final compositor eyes were reviewed for open/closed hands, weapon and wall contact, loose-item grip and creature restraint. All 12 default-mode stereo pairs and all 48 paired gravity samples were reviewed in order. The dropped item falls out of the lower view; native body telemetry establishes ground settling. Captures measure 840 by 880 pixels per eye. The temporal review video uses recorded sample durations and is a sampled sequence, not continuous full-frame-rate footage.

Private checks used eye resolution scale 0.6. Compositor readback was paused during the short axe strike and resumed for sustained-contact evidence. Actual delivered motion was measured. No independent HMD-movement acceptance sequence was run. Nighttime captures and scripted controller poses cannot establish physical anatomical fit or comfortable movement. Normal graphics settings and physical thresholds were unchanged.

Earlier hidden/batch candidates could not sustain usable stereo/controller playback and are retained as failed attempts. An earlier visible run was aborted after the wrong private helper target was copied. These are not counted as passing gates; the final visible runs above tested the frozen payload. The physical SteamVR failure remains a separate unresolved hardware acceptance result.

Added contact, item grabbing, creature grabbing and finger articulation are separate restart-required options, all off by default. With all four off, the original VHVR interaction baseline remains. Swept contact allows escape after 8 cm or 20 degrees of tracking separation, which can temporarily permit penetration. Physical feel requires human judgment. SteamVR overlay keyboard, binding editor and body trackers do not have native OpenXR equivalents in this release; see [ARCHITECTURE.md](ARCHITECTURE.md).
