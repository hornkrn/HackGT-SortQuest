# Expanded trash catalog

The game has 24 learning types, six per bin, with 66 prefabs: the original 48 visual variants plus 18 new shapes. These are the game's sorting categories, not municipal recycling instructions.

| Bin | Added types |
| --- | --- |
| Metal | Food tin, tuna can, metal lid, foil tray, steel bottle |
| Plastic | Yogurt cup, detergent bottle, shampoo bottle, plastic tub, plastic cap |
| Paper | Cereal carton, egg carton, paper tube, folded newspaper |
| Hazardous | 9V battery, coin cell, smartphone, circuit board |

Every new type has its own stable server ID and learning history. Original enum values 0–5, prefab GUIDs, item transforms, and recorded local grasp frames are preserved. New types are appended to the enum; do not reorder it.

## Collider changes

- All 48 existing visual variants receive colliders derived from their individual visual meshes.
- Existing bottles use separate body, shoulder, and cap/neck hulls. AA batteries separate the terminal from the body; cans use height sections.
- Crumpled paper uses 32 convex sections so one enclosing hull does not bridge all its folds.
- New cups, open tins, caps, trays, and the paper tube use compound walls. Their openings are not filled by a single convex hull.
- The detergent bottle's handle has three solid bars around a real gap.
- Electronics have separate raised components where appropriate. Sub-millimeter printing, screen cracks, and page-edge details remain visual details.
- All physical pieces are convex, share one root Rigidbody, and are discovered by the existing Meta hand and controller interactables. There are no dynamic non-convex colliders and no new packages.

These remain lightweight collision approximations. In particular, the legacy folded/crumpled surface is not a triangle-perfect concave collider. The Editor checks measure sampled nearest-surface gaps, hollow openings, handle clearance, collider ownership, and settling on a thin floor.

Sampled fit tolerances are 2.5 mm for new models, 6 mm for most originals, 8 mm for crumpled-paper folds, and 9 mm for dented cans. Small dents are approximated to keep collider complexity bounded; bottle necks, hollow openings, and handles are represented structurally.

Checker version **4** invalidates old practice feasibility labels after these geometry changes. Existing human records are retained, and recent demonstrations are practiced again. Verified-contact dots and opposing-pad grasp checks are included; successful human sorting does not automatically make a grasp robot-feasible. Larger objects and very thin parts still require an appropriate tool and reachable approach. This update does not enlarge jaws or guarantee every item works with every tool.

## Use in the existing scene

1. Exit Play mode and let Unity import and compile.
2. Select the existing **TrashSpawner** GameObject. Its new **Include Expanded Catalog** field defaults to enabled. Existing prefab references stay assigned; the 18 new prefabs load from `Resources/TrashCatalog` at startup. No new components or scene wiring are required.
3. Press Play. Spawning first chooses an item type uniformly, then a skin, so the original eight skins per type do not crowd out new types. Teaching filters and augmentation use the same catalog.
4. The grasp-agreement display cycles six types per page. The trash guide rotates its models through the existing shelves every ten seconds, preserving the current board geometry.
5. Select the existing **GrabDots** object to tune **Max Grasps** if desired. Green markers show checked gripper contacts, not unchecked fingertip samples.

The local server must run the updated `server/main.py` before it can accept the new IDs. Nothing is deployed automatically. Offline gameplay and local recording continue to work.

## Rebuild and verify

- **SortQuest > Trash Art > Build Expanded Catalog and Accurate Colliders** regenerates the catalog and refits the original variants through Unity's asset APIs. It does not modify the active scene.
- **SortQuest > Trash Art > Validate Expanded Catalog** checks all 66 prefabs, contact geometry, drop behavior, hand/controller Rigidbody bindings, uniform spawning, and teaching filters.
- **SortQuest > Trash Art > Render Expanded Catalog** creates `docs/visuals/expanded-trash-catalog.png` in a temporary preview scene.
- **SortQuest > Checks > Gripper Contacts** checks opposing-pad contacts, invalid same-face pinches, oversized boxes, suction seals, and approach obstructions.
- `python -m unittest server.test_catalog` checks the Unity/server IDs and original enum ordering without third-party dependencies. `server.test_api` adds upload/query checks when the declared API development dependencies are installed.

The original **Rebuild Models and Wire Scene** command still targets the original six families. If those models are regenerated, run the expanded collider builder afterward. It is not necessary to rebuild scene art for this catalog update.

Headset frame time, hand feel, and complete robot rounds must still be tested on Quest. Compound collider counts are higher than the former single-primitive approximations.
