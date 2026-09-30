# VHVR OpenXR v0.1.0 release candidate validation

Checked September 29, 2026 with an isolated owned Valheim 1.0.16 installation, official VHVR v0.10.5, Unity OpenXR 1.16.1 and Meta XR Simulator v207. The Nikami gameplay DLL was absent. Private QA helpers and all game data are excluded from the downloadable package.

The adapter is the retained tested binary, SHA-256 E57B71754B70D55F9DEFF95EF5204F9E054486E187E1761EEF59EF5D61A022DB. Its 19 retained C# source files match SOURCE-PROVENANCE.json. The portable source project also compiled successfully against an owned installation. The rebuilt binary is not substituted for the retained binary.

| Gate | Result |
|---|---|
| Native articulated hands | Passed all 30 native finger joint transitions, independent thumb/index gestures, opening after release and contact curl. Both-eye open/closed images reviewed. |
| Solid hands and held weapons | Passed a 1.2 m sweep against a 5 mm wall, tangential sliding, wrist rotation and opposing equipment contact. Hands and long weapons retained contact under eight seconds of pressure. |
| Escape from blocked contact | Passed 768 corner pull/rotation poses, immediate withdrawal/reconnection and release of an item that leaves avatar reach. The normal contact limit is 8 cm or 20 degrees of tracking separation. Contact has no fixed timeout. |
| Native world surfaces | Passed 42 fence/post/bed/chest paths and 3,024 rubbing/rotation poses. |
| Loose item grab/lift/throw | Passed normal controller acquisition of the original native networked dynamic body, a 30 cm lift, inventory pickup protection and preservation of native release momentum. |
| Gravity and settling | Held drift 1.5 mm over 2.34 seconds of sampled observation. After release the same body fell 1.645 m, reached a downward speed of 5.873 m/s and settled to zero speed. Release samples span 7.86 seconds. Native gravity and constraints stayed intact. A disabled-gravity, non-falling case is rejected. |
| Creature grip | Passed original live grayling grip, free-hand native damage and release. Live neck/boar eligibility passed; players, high-level or oversized creatures were rejected. |
| Input and recovery | Passed 1,536 simulated input frames, 613 observed grip transitions and 12 focus/tracking recoveries across male/female avatars and equipment. Tracking loss, inventory, separation, ownership loss, component disable, focus loss, equipping and death released held bodies and restored their settings. |
| Functional suite | 319 PASS assertions, zero FAIL assertions in the final full functional run. |
| Focused weapon compositor check | Passed both distinct nonblank final eyes after compositor capture warmed up. Weapon meshes, hands and contact geometry were reviewed in both eyes. Negative fixtures reject a missing eye, black images, UI-only images and duplicated eyes. |
| Physical headset contact feel, comfort, haptics and frame pacing | Pending the human headset playtest. Simulator results do not certify these. |

Temporal visual review covers all 48 paired compositor samples of the held/released item, using recorded sample times. The images show the held item and its fall out of the lower view; the final ground-rest claim comes from native body telemetry. This is sampled eye evidence, not a continuous full-frame-rate recording.

Earlier private attempts are retained for audit: one launch used a removed simulator runtime; one creature fixture incorrectly queried an uninitialized catalog prefab; one screenshot pair was captured before asynchronous compositor capture resumed. These are not counted as successful checks. No adapter runtime code was changed to repair those QA setup/capture issues.

Physics scope is the hands, their held equipment and gripped loose items/eligible creatures. The hands and held weapons use swept native collision constraints; loose held items use native rigid bodies and finite spring forces. The player's existing movement/capsule collision is retained. Strong tracking separation permits escape and may allow temporary penetration; the physical playtest must judge that tradeoff.

The dependency is the latest published VHVR release, v0.10.5. Fork source is synchronized with upstream master 3ccd2a3c81cacb4208e8a2b0a7536ac4be09eccb, but the companion has not been validated against a rebuilt unreleased master gameplay DLL. Broad multiplayer and every weapon combination remain unproven.
