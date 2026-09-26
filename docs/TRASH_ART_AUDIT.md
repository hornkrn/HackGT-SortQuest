# Used trash art audit

Generated and validated in Unity.

- **48 models**: eight per existing ItemType.
- One opaque URP Simple Lit material, one 2048 × 2048 atlas with mipmaps and Android ASTC 6×6 compression. Alpha stores surface smoothness; it is not transparency.
- **1,472 triangles maximum per item**, **11,776** for eight copies of the heaviest item (geometry only, before shadow passes). All 48 meshes total 27,792 triangles.
- One mesh renderer per item. No new runtime scripts, lights, colliders, rigidbodies, or packages.
- Original six prefab GUIDs retained; 42 Unity prefab variants inherit their gameplay components.
- Serialized transforms, interaction components, rigidbodies, item fields, collision meshes, layers, tags, and internal references compared with each original before and after saving.
- Spawner selection exercised 4,800 times across the pool and 200 times per item filter; every variant was reachable and filters returned only the requested ItemType.
- Eight variants per type preserve the existing per-type random weighting. The original prefab remains first for GetPrefab and training.

## Limits

The existing simplified collision envelopes are intentionally retained, including the bottle's cylindrical collider. Dents, bottle shoulders, and paper folds are visual details; this preserves recorded local grasp frames and robot collision behavior. Standalone Quest 2 frame time and hand interaction still need verification on the headset.

| Model | Existing type | Triangles |
|---|---|---:|
| Crushed Cola | AluminumCan | 794 |
| Orange Soda | AluminumCan | 794 |
| Lime Seltzer | AluminumCan | 794 |
| Iced Tea | AluminumCan | 794 |
| Ginger Fizz | AluminumCan | 794 |
| Berry Soda | AluminumCan | 794 |
| Sparkling Water | AluminumCan | 794 |
| Citrus Energy | AluminumCan | 794 |
| Spring Water | PlasticBottle | 1472 |
| Sports Drink | PlasticBottle | 1472 |
| Mineral Water | PlasticBottle | 1472 |
| Citrus Drink | PlasticBottle | 1472 |
| Iced Coffee | PlasticBottle | 1472 |
| Berry Juice | PlasticBottle | 1472 |
| Green Tea | PlasticBottle | 1472 |
| Cloudy Water | PlasticBottle | 1472 |
| Returned Parcel | CardboardBox | 46 |
| Water Stained Box | CardboardBox | 46 |
| Produce Carton | CardboardBox | 46 |
| Express Parcel | CardboardBox | 46 |
| Reused Shipping Box | CardboardBox | 46 |
| Taped Delivery | CardboardBox | 46 |
| Storage Carton | CardboardBox | 46 |
| Fragile Package | CardboardBox | 46 |
| Crumpled Newsprint | CrumpledPaper | 588 |
| Used Receipt | CrumpledPaper | 588 |
| Notebook Page | CrumpledPaper | 588 |
| Brown Packing Paper | CrumpledPaper | 588 |
| Junk Mail | CrumpledPaper | 588 |
| Coffee Stained Page | CrumpledPaper | 588 |
| Discarded Form | CrumpledPaper | 588 |
| Blue Graph Paper | CrumpledPaper | 588 |
| Worn Alkaline | BatteryAA | 426 |
| Copper Top | BatteryAA | 426 |
| Silver Cell | BatteryAA | 426 |
| Green Rechargeable | BatteryAA | 426 |
| Blue Alkaline | BatteryAA | 426 |
| Scraped Red Cell | BatteryAA | 426 |
| Industrial Cell | BatteryAA | 426 |
| Faded Yellow Cell | BatteryAA | 426 |
| Scuffed Black Charger | PowerBank | 148 |
| Worn White Charger | PowerBank | 148 |
| Blue Travel Charger | PowerBank | 148 |
| Dented Silver Charger | PowerBank | 148 |
| Old Red Charger | PowerBank | 148 |
| Taped Charger | PowerBank | 148 |
| Green Pocket Charger | PowerBank | 148 |
| Scratched Graphite Charger | PowerBank | 148 |
