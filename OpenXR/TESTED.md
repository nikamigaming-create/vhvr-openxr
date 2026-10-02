# VHVR OpenXR / OpenVR v0.3.0 validation

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
