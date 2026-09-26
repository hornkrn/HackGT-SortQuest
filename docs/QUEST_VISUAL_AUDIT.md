# Quest scene art audit

Generated from the saved scene by the Unity editor.

- Decorative mesh triangles: **12,661** (excludes text).
- Decorative mesh renderers: **12**, each using the same opaque palette material.
- Total decorative renderers including text: **18**.
- Added textures, realtime lights, shadow casters, colliders, rigidbodies, and runtime update scripts: **0**.
- Original nonvisual component snapshots verified unchanged: **2,817**.
- URP mobile: existing 0.8 render scale and 4x MSAA retained; one 1024px main-light shadow map, 12m range; no additional lights, depth texture, opaque texture, HDR, or added post-processing.

These are scene complexity checks, not headset frame-time measurements. The original robot/hand/graph renderers, camera capture and training simulation are additional costs. A standalone Quest 2 run with CPU/GPU profiling is required before claiming a stable frame rate.

Art uses opaque vertex colors with directional shading computed at build time. Geometry is combined by room zone so culling remains useful. The custom shader supports Unity stereo instancing/multiview macros. No transparent window layers or full-screen effects are used.

## On-device validation

Build a Development APK, connect the Quest 2, and measure CPU/GPU frame time during human sorting, augmentation/training, robot trials, and camera capture. At 72 Hz the total frame budget is 13.89 ms; leave headroom and check thermals over 15 minutes. Check text legibility, both-eye rendering, hand tracking, and all bin interactions.

Sources: [Meta performance guidance](https://developers.meta.com/horizon/documentation/unity/unity-perf/), [Unity untethered XR optimization](https://docs.unity.com/en-us/engine/6000.5/manual/xr/graphics/untethered-device-optimization).
