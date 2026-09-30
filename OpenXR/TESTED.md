# VHVR OpenXR v0.1.0 release candidate validation

Validated September 29, 2026 with owned Valheim 1.0.16, Unity OpenXR 1.16.1 and Meta XR Simulator v207. The complete current fork package was installed in an isolated game fixture. Nikami gameplay was absent; the private acceptance plugin and test world are excluded from the package.

Upstream revision: 47fad0494de5daee19356d5eb4d692671a0e3994, checked September 29, 2026 at 21:03 Pacific time.
ValheimVRMod.dll SHA256: 65F973E1E667E0B295745E0C8AA58135342CCB246B875CC94394343A6D6FD169.
OpenXR adapter SHA256: CC12B4AAB1ECFC79CEF430FE1D6918974F1436731C5DD38D75498073ABE885D4.
The matching controller assemblies, actions and tracked upstream bundles were tested with these DLLs. The manifest covers all 118 runtime files.

| Gate | Result |
|---|---|
| Startup and tracked rig | Passed current gameplay/controller builds, active stereo and anatomical wrist/palm alignment. Both measured wrist/target and palm/controller gaps were below recorded 0.1 mm precision in the neutral test. |
| Articulated hands | All 30 native finger joints, independent thumb/index gestures, reopen and contact curl passed. Both final eyes for open and closed hands reviewed. |
| Solid hands and held weapons | Passed 1.2 m sweep against a 5 mm wall, sliding, wrist rotation and opposing equipment contact. Hands and long weapons kept contact during eight seconds of pressure; rotation/peer checks sustained five seconds. |
| Escape from contact | Passed 768 corner pull/rotation poses, immediate withdrawal/reconnection and avatar-reach release. Normal contact yields at 8 cm or 20 degrees of tracking separation, with no timed contact expiry. Native stick movement escaped a held pole without leaving an environment locomotion latch. |
| Native surfaces | 42 fence/post/bed/chest paths, 3024 rubbing/rotation poses and 2659 contacts passed. |
| Loose-item grabbing | Normal controller grip acquired the original native networked dynamic body. Lift, rotation, inventory pickup protection, release and native momentum preservation passed. |
| Gravity | Held drift 0.2 mm over 2.59 seconds of sampled observation. Release fell 1.648 m, minimum vertical velocity -5.905 m/s, final speed zero. Release samples span 9.65 seconds. Native gravity/constraints remained intact; a disabled-gravity/non-falling fixture was rejected. |
| Creature restraint | Live grayling hold, free-hand native damage and release passed. Live neck/boar eligibility passed. Players, high-level and oversized creatures were rejected. |
| Input and recovery | 1536 simulator input frames, 1083 grip transitions, 12 focus/tracking recoveries across male/female and native padded armor passed. Tracking loss, inventory, separation, ownership, disable, focus, equip and death released bodies and restored settings. |
| Weapon impact and final eyes | Measured strike closing speed 2.074 m/s exceeded the unchanged 1.4 m/s threshold. One clink, sustained contact without repeated clinks and gentle contact without clinks passed. Distinct nonblank compositor eyes passed; both native axe meshes, hands, forearms and contact geometry reviewed. Contact volumes matched rendered equipment. Negative fixtures reject missing, black, UI-only and duplicate eyes. |
| Full functional suite | 320 PASS assertions, zero FAIL assertions in run-11-final-upstream. No runtime exceptions in this completed run. |
| Physical headset feel, comfort, haptics and frame pacing | Pending the user's final headset playtest. Simulator coverage cannot establish these. |

All 48 paired gravity compositor samples were reviewed with measured sample times. The item falls out of the lower view; its ground settling is established by native body telemetry. The review video uses the measured sample durations and is not a continuous full-frame-rate recording. Final eye PNGs measure 840 by 880 pixels per eye.

This final simulator run used a private eye resolution scale of 0.6 during the physics cases. Compositor readback was paused during the short prescribed strike and resumed for sustained-contact visual evidence. Motion telemetry records the delivered strike, rather than inferring its speed from the requested movement. Two preceding attempts failed this gate because too few strike frames were delivered; one recorded a 196 ms stall. Those attempts are retained as failures. The final run uses the same runtime DLLs and thresholds. No physics settings were weakened. Normal user graphics settings are unchanged; full-resolution performance on this final candidate and physical headset frame pacing remain unproven.

The first current-upstream candidate exposed a startup null reference in an unused walking indicator. The fork now guards its absent LineRenderer. Two following attempts exposed missing SteamVR Unity package build symbols, which prevented the native head-pose driver from being installed and placed hands beyond arm reach. The build wrapper now matches upstream SteamVR.asmdef's OPENVR_XR_API and UNITY_LEGACY_INPUT_HELPERS definitions. No offset was added to conceal the transform error. Those failed candidates are not the shipped DLLs.

Earlier checks against official v0.10.5, the retained E57 adapter and the successful 7fa70ef candidate are baseline evidence only. The final upstream snapshot has been rebuilt and separately validated in the successful run above. Included screenshot and drawbridge gesture source changes have no separate behavior acceptance claim.

Physics covers hands, held weapons, gripped loose objects and eligible creatures. Hands and held weapons use swept native constraints; loose held objects use their native rigid bodies and finite spring forces. Player movement collision is retained. Strong tracking separation allows escape and can allow temporary penetration; the physical playtest must judge that feel. Broad multiplayer and every weapon combination remain unproven.
