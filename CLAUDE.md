# SortQuest: guide for Claude Code

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

## Design decisions
- Items move kinematically along the belt (`Rigidbody.MovePosition`) and switch to dynamic physics when grabbed or released.
- Grab start and release come from the Interaction SDK's grab pointer events (Select and Unselect).
- Human grab to gripper grasp: center = midpoint of thumb tip and index tip; closing axis = index tip minus thumb tip; approach = wrist-to-middle-knuckle direction projected off the closing axis; width = thumb-to-index distance. Store position and rotation in the item's local frame.
- Good data = item landed in the correct bin without being dropped.
- Robot policy: with no data, try a random grasp on the item's bounds. With data, use the medoid grasp (the one with the most other successful grasps within about 2 cm and 20 degrees). Never average grasps directly. Confidence = share of successful grasps agreeing with the chosen one.
- Robot success: when the gripper closes, succeed if both fingers touch the item's collider, then attach with a `FixedJoint`. No friction-based grasping.
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
  "session_id": "s-20260926-0412",
  "player": "anon-3f2a",
  "timestamp": "2026-09-26T14:03:11Z",
  "source": "human",
  "item_type": "battery_aa",
  "correct_bin": "hazardous",
  "grasp": { "pos_local": [0.001, 0.012, -0.003], "rot_local": [0, 0.707, 0, 0.707], "width_m": 0.016 },
  "item_pose_world": { "pos": [0.42, 0.95, 0.30], "rot": [0, 0, 0, 1] },
  "outcome": { "bin": "hazardous", "correct": true, "dropped": false, "hold_s": 1.6 },
  "hand": "right"
}
```
`source` is one of `human`, `augmented`, or `robot`.
