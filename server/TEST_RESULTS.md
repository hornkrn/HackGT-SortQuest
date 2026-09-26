# Connection verification — September 26, 2026

API bound to `0.0.0.0:8000`; LAN address at test time: `10.90.56.133`.

- **10 Python tests passed:** request validation and limits, server-controlled
  fields, duplicate handling, partial-write/write-concern failures, authentication,
  missing-key fail-closed behavior, query filters, and sanitized database errors.
- **Live HTTP + MongoDB passed on loopback and LAN IP:** authenticated writes,
  duplicate retries, stored IDs/dates, Unity-compatible read payloads, good/source/
  item filters, result limits, stats, request bounds, MongoDB validation rejection.
- **Actual Unity 6000.3.25f1 networking smoke test passed:** JSON serialization of
  the game's GraspRecord; health; upload; duplicate acknowledgement; read-back;
  stats; 401 callback; offline callback; persisted queue deduplication; offline
  retention; component restart/queue reload; successful retry clearing the queue;
  remote merge deduplication without local upload events.
- **Cleanup verified:** only uniquely identified test records were deleted;
  the test queue was removed. No test fixtures remain.

The Unity smoke test ran in a minimal temporary Unity project with copies of the
actual SortQuestApi, DataUploader, GraspRecord, GraspDataset and TrashTypes scripts.
Non-network TrashItem/GripperGrasp types were stubbed to avoid Meta/XR asset imports.
Networking, serialization, storage, and MongoDB were real, not mocked.

The full project compiled scripts after rebuilding the generated Library cache,
but the editor process was terminated during shader/asset import before the
smoke test ran. A complete game build and APK installation were not verified.

The LAN-IP tests originated from this computer. Actual Quest-to-computer Wi-Fi
reachability, firewall/router client isolation, Android APK runtime behavior, and
long-duration reconnect/large-queue behavior remain to be verified on hardware.
Open `/health` from the headset browser, then build using the setup steps in
server/README.md and record a grasp. The browser check verifies Wi-Fi reachability;
the APK check verifies the complete deployed path.
