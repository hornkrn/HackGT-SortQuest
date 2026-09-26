# Hand tracking over Quest Link

If controllers and head tracking work but natural hands do not appear, collect a live report from the Windows PC running Unity:

1. Enter Play mode with the headset connected through Quest Link.
2. Put both controllers down and hold your hands in front of the headset. The current scene does not enable simultaneous hands and controllers.
3. Open **SortQuest → Diagnose Hand Tracking**.
4. Click **Refresh**, then **Copy Report** while the headset is being worn. Include the first red Console error, if any, when sharing the report.

The report separates the runtime, raw `OVRHand` data, Interaction SDK hand state, and visual renderer state. It does not change any settings, add scene objects, force hands visible, or save files. The tool lives under `Assets/Editor`, so it is excluded from standalone builds.

- No valid tracked `OVRHand` data: inspect headset hand tracking, switching away from controllers, Link developer runtime features and runtime errors.
- Valid `OVRHand` data but disconnected or hidden Interaction SDK hands: inspect the hand data source, confidence and visual references.
- Tracked and visible hands reported: inspect camera layers, transforms, material/shader errors and occlusion.

**SortQuest → Validate Hand Scene Wiring** checks the saved scene's camera rig, primary hand-anchor sources, interaction rig reference and OpenXR hand-renderer references. Passing this check does not prove that a remote PC receives live hand input. The comprehensive rig legitimately contains additional OVRHand sources; their presence is not by itself an error. Its disabled legacy mesh renderers are also intentional because the Interaction SDK supplies separate hand visuals.

The project's Meta XR OpenXR feature already requests `XR_EXT_hand_tracking` and the Meta hand extensions. The Windows and Android configurations both enable Meta XR and the Oculus Touch profile, and use the OpenXR hand skeleton. No additional hand package or alternate input pipeline is required merely to run this diagnostic.

For Link, Meta documents enabling Developer Runtime Features in the PC app and restarting Unity after changing Link feature settings. These settings belong to the test PC and do not travel with Git. See [Meta's Link setup](https://developers.meta.com/horizon/documentation/unity/unity-link/) and [hand tracking overview](https://developers.meta.com/horizon/documentation/unity/unity-handtracking-overview/).
