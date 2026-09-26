# Natural drop tuning

The trash keeps Meta Interaction SDK's measured release velocity and spin, with outlier limits raised from 1.5 to 4.5 m/s and from 10 to 16 rad/s. Gravity and existing object masses remain unchanged. No extra hand-follow smoothing or release delay is added.

Six shared physics materials distinguish paper/cardboard, plastic, metal and batteries through modest friction and restitution. All 48 trash prefabs inherit the settings from the six base prefabs. Rigidbody interpolation, speculative continuous collision detection, light damping, and per-item solver settings support smoother rendering and stable contacts without increasing the project's physics tick rate.

Items returned to the belt now resolve their landing/sliding/tumbling before becoming kinematic again: at least 0.12 seconds of stable top contact, small vertical and relative linear velocity, and low angular velocity. Bin scoring, missed-item timers, grab events, robot grasp rules, and XR configuration are unchanged.

To tune: select a base trash prefab's TrashItem component and adjust Release / Contact stability. Friction and bounce assets are under Assets/Physics. **SortQuest → Apply Natural Drop Physics** reapplies this preset to all trash prefabs; it overwrites those tuning values.

**SortQuest → Validate Drop Physics** uses an isolated editor preview physics scene. It checks all 48 resolved prefabs for retained ordinary release momentum, capped extreme velocities, gravity, landing on a 2 cm floor, settling, and interpolation. It does not exercise live hand tracking or runtime conveyor callbacks.

Headset acceptance: compare a stationary release, gentle toss, wrist rotation, landing on a bin rim, and dropping back onto the belt. Confirm the belt waits for settling and that correct/wrong-bin scoring is unchanged. Comfort and real-world feel require this hardware check.
