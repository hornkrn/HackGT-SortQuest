# SortQuest: guide for Codex

## What this project is
A Unity VR game for the Meta Quest 2, built at HackGT 13 (deadline Sunday, Sep 27, 2026, 12:00pm EDT).
Players stand at a conveyor belt in a virtual recycling plant and sort trash into bins using hand tracking.
Each grab is recorded as a two-finger robot gripper grasp, stored relative to the item.
A simulated robot gripper learns from the successful grasps and sorts items itself, with its accuracy shown live.
Pitch: "Every time you play, a recycling robot gets better at its job."

## Repository
- GitHub: https://github.com/hornkrn/HackGT-SortQuest (branch `main`).
- Never commit generated folders: `Library/`, `Temp/`, `Obj/`, `Build/`, `Builds/`, `Logs/`, `UserSettings/`, `MemoryCaptures/`, `.vs/`, or `*.csproj` and `*.sln` files.
- Never commit secrets. The MongoDB connection string lives in a `.env` file, which is gitignored.
- `CLAUDE.md` (Claude Code) and `AGENTS.md` (Codex) hold the same guide. When you change one, make the same change in the other.
- Pull before pushing. Commit after each working milestone with a clear message.
- Only one person edits the main scene at a time; others work in prefabs or their own test scenes.

## Tech stack
- Unity 6.3 LTS (6000.3.25f1), Universal Render Pipeline, Android build target.
- Meta XR SDK v207 (All-in-One), Unity OpenXR plugin with the Meta Quest feature group.
- Building Blocks in the scene: Camera Rig, Hand Tracking, Grab Interaction (Meta Interaction SDK).
- Testing: Meta Quest Link on Windows (press Play), or the Meta XR Simulator.
- Later: a Python FastAPI server with MongoDB Atlas, in `/server` at the repo root (outside `Assets`).

## Rules
- Put C# scripts in `Assets/Scripts/` and use the namespace `SortQuest`.
- Do not hand-edit `.unity`, `.prefab`, `.asset`, or `.meta` files, or anything in `ProjectSettings/`, `Library/`, or `Packages/manifest.json`, unless asked.
- A person wires scenes in the Unity Editor. After writing a script, give short step-by-step Editor instructions: which GameObject to create or select, which components to add, and which Inspector fields to set.
- Before using any Meta SDK API, check the real API in the installed package source under `Library/PackageCache/` (for example `com.meta.xr.sdk.interaction`). APIs change between versions; don't guess.
- Hand bone names depend on the SDK version (for example `Hand_ThumbTip` versus `XRHand_ThumbTip`). Check the package source.
- Keep code simple and readable. Use `[SerializeField]` fields so values can be tuned in the Inspector.
- Don't add packages without asking.
- Keep the game playable after each milestone.
- For positioning, related work, or the pitch (README, slides, judges), read `docs/RELATED_WORK.md` first and follow its rules: no "first" claims, link sources as given, and don't cite items marked "verify". Only claim features that exist in the code.

## Design decisions
- Items move kinematically along the belt (`Rigidbody.MovePosition`) and switch to dynamic physics when grabbed or released.
- Grab start and release come from the Interaction SDK's grab pointer events (Select and Unselect).
- Human grab to gripper grasp: center = midpoint of thumb tip and index tip; closing axis = index tip minus thumb tip; approach = wrist-to-middle-knuckle direction projected off the closing axis; width = thumb-to-index distance. Store position and rotation in the item's local frame.
- Good data = item landed in the correct bin without being dropped.
- Robot policy: with no data, try a random grasp on the item's bounds. With data, use the medoid grasp (the one with the most other successful grasps within about 2 cm and 20 degrees). Never average grasps directly. Confidence = share of successful grasps agreeing with the chosen one.
- Robot success: when the gripper closes, succeed if both fingers touch the item's collider, then attach with a `FixedJoint`. No friction-based grasping. Suction: succeed if the surface under the cup is flat enough and faces the cup (raycast check), then attach the same way.
- Collision: `GripperCollision` checks the whole gripper (every part from `IGripperModel.GetParts`, the same list `GripperVisual` draws) with physics overlap queries; the robot has no colliders. The robot plans first (line-up, approach sweep, grasp, closing), travels at a safe height, and guards every step; the policy uses the best-agreed learned grasp that fits the item's current pose, and random guesses are checked the same way. While carrying, the held item is ignored by the gripper check and checked against the scene as the payload. The player's body (tagged `Player`) is never an obstacle. `RobotArmDisplay.LinksClear` checks the arm's links against fixed scenery and fails as `arm_collision`, kept separate from gripper feasibility. Bump `GripperCollision.Version` whenever the check changes.
- Never alter human demonstrations. Gripper feasibility for a human grasp goes in a separate annotation (`grasp-feasibility.jsonl`, `POST /grasp-annotations`); `hand_penetration` flags hands inside scenery. Robot records carry `outcome.failure_reason` and `outcome.stage`; `aborted` attempts are saved but not scored.
- Records are read and written with `GraspJson` (Newtonsoft, public fields only) so nullable labels survive; `Assets/link.xml` keeps `SortQuest` types from being stripped on IL2CPP.
- Gripper types live in `GripperCatalog` (selected in the in-VR menu). Their checks share the `IGripperModel` interface (`GripperShape`, `SuctionShape`), used by the robot, the augmenter, and the ghost grippers.
- Save grasps locally as JSON under `Application.persistentDataPath`. The game must never wait on the network.

## Bins and items
- Bins: metal (blue), plastic (yellow), paper (green), hazardous (red).
- Starting items (primitives first): aluminum can, plastic bottle, cardboard box, crumpled paper, AA battery, power bank.

## Build order
1. `TrashItem`, `TrashSpawner`, `ConveyorBelt`, `SortingBin` (with a score).
2. `HandGripperPose`, `GraspRecorder`, `GraspDataset`.
3. `GraspPolicy`, `RobotGripper` (a floating gripper, no arm yet).
4. `GameManager` state machine: Intro, HumanRound, Training, RobotRound, TeachMe, Results.
5. `LearningViz` (accuracy chart, confidence bars, ghost grippers, grab dots), `GraspAugmenter`, `DataUploader`.

## Grasp record
```json
{
  "record_id": "9f1c2e7b4a6d4c0e8b3f5a2d7e9c1b04",
  "session_id": "s-20260926-0412",
  "player": "anon-3f2a",
  "timestamp": "2026-09-26T14:03:11Z",
  "source": "human",
  "item_type": "battery_aa",
  "correct_bin": "hazardous",
  "grasp": { "pos_local": [0.001, 0.012, -0.003], "rot_local": [0, 0.707, 0, 0.707], "width_m": 0.016 },
  "item_pose_world": { "pos": [0.42, 0.95, 0.30], "rot": [0, 0, 0, 1] },
  "outcome": { "bin": "hazardous", "correct": true, "dropped": false, "hold_s": 1.6,
               "failure_reason": "none", "stage": null, "grasp_success": null, "motion_success": null },
  "hand": "right",
  "gripper": "parallel_100mm",
  "input_device": "controllers",
  "schema_version": 3,
  "checker_version": 1,
  "feasible": null,
  "parent_record_id": null,
  "hand_penetration": false,
  "image": { "id": "4be07c1f93a24d6f8d0e2b7c5a1f3e90", "cam_pos": [0.2, 2.0, 0.6], "cam_rot": [0.7071, 0, 0, 0.7071], "fov_y_deg": 80, "rgb_size": 256, "depth_size": 128 }
}
```
`source` is one of `human`, `augmented`, or `robot`.
`record_id` is a GUID (32 hex characters) set once when the record is created; the server uses it to ignore duplicate uploads.
`gripper` is the gripper the record is for (see `GripperCatalog`: `parallel_100mm`, `parallel_85mm`, `parallel_140mm`, `suction_40mm`). Grasps are learned per gripper: two-finger grippers use good human grasps plus variations practiced for that gripper; the suction cup uses only practiced variations (pinches converted to surface contact points). `input_device` is `hands`, `controllers`, `simulator`, `unknown`, or `none` (robot). `schema_version` is the format a record was created in (3 today); older records get the new fields on load but keep their version.
`checker_version` is the `GripperCollision.Version` behind `feasible` and failure reasons (1 = old finger-only checks). The robot learns only from `augmented` records with the current checker and `feasible: true`. `parent_record_id` links augmented and robot records to the grasp they came from.
`outcome.failure_reason` is `none`, `collision_on_approach`, `collision_at_grasp`, `path_blocked`, `collision_during_motion`, `no_contact`, `no_seal`, `out_of_reach`, `arm_collision`, `aborted`, or `unknown` (old robot failures); `outcome.stage` is `plan`, `approach`, `grasp`, `carry`, or `release`. Keep the server's `FailureReason` list in sync.
`image` describes the overhead robot camera's view at the moment of the grasp (empty `id` if the item wasn't in view). The files live in `persistentDataPath/images/`: `{id}_rgb.png` (color), `{id}_depth.exr` (float depth in meters along the camera axis, 0 = nothing), and `{id}_mask.png` (0 nothing, 85 scene, 170 grasped item, 255 other trash). Augmented records reuse their original's image.
