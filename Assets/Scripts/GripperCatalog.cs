using System;
using System.Collections.Generic;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// The grippers players can choose in the menu, and which one is selected. Everything that depends on the
    /// gripper (robot, policy, augmenter, visuals, records) reads Current and listens to Changed.
    /// </summary>
    public class GripperCatalog : MonoBehaviour
    {
        /// <summary>Id of the original gripper; older records without a gripper field belong to it.</summary>
        public const string StandardId = "parallel_100mm";

        [SerializeField] private GripperProfile[] profiles = DefaultProfiles();
        [SerializeField] private string defaultProfileId = StandardId;

        /// <summary>Raised when a different gripper is selected.</summary>
        public event Action<GripperProfile> Changed;

        public IReadOnlyList<GripperProfile> Profiles => profiles;
        public GripperProfile Current { get; private set; }

        private static GripperProfile standard;

        /// <summary>Used when a scene has no catalog: the original 100 mm two-finger gripper.</summary>
        public static GripperProfile Standard => standard ?? (standard = DefaultProfiles()[0]);

        /// <summary>The selected gripper of the catalog in the scene, or the standard one.</summary>
        public static GripperProfile CurrentOrStandard(GripperCatalog catalog)
        {
            return catalog != null && catalog.Current != null ? catalog.Current : Standard;
        }

        private void Awake()
        {
            if (profiles == null || profiles.Length == 0)
            {
                profiles = DefaultProfiles();
            }
            Current = Find(defaultProfileId) ?? profiles[0];
        }

        public GripperProfile Find(string id)
        {
            foreach (GripperProfile profile in profiles)
            {
                if (profile != null && profile.id == id)
                {
                    return profile;
                }
            }
            return null;
        }

        public void Select(string id)
        {
            GripperProfile profile = Find(id);
            if (profile == null || profile == Current)
            {
                return;
            }
            Current = profile;
            Debug.Log($"[SortQuest] Gripper selected: {profile.displayName} ({profile.id})");
            Changed?.Invoke(profile);
        }

        public static GripperProfile[] DefaultProfiles()
        {
            return new[]
            {
                new GripperProfile
                {
                    id = StandardId, displayName = "Standard", description = "Two-finger, 100 mm",
                    kind = GripperKind.Parallel, parallel = new GripperShape(),
                    accentColor = new Color(1f, 0.55f, 0.1f)
                },
                new GripperProfile
                {
                    id = "parallel_85mm", displayName = "Compact", description = "Two-finger, 85 mm",
                    kind = GripperKind.Parallel,
                    parallel = new GripperShape { maxOpening = 0.085f, fingerLength = 0.045f },
                    accentColor = new Color(0.1f, 0.8f, 0.8f)
                },
                new GripperProfile
                {
                    id = "parallel_140mm", displayName = "Wide", description = "Two-finger, 140 mm",
                    kind = GripperKind.Parallel,
                    parallel = new GripperShape { maxOpening = 0.14f, fingerLength = 0.07f, fingerWidth = 0.03f },
                    accentColor = new Color(0.6f, 0.4f, 1f)
                },
                new GripperProfile
                {
                    id = "suction_40mm", displayName = "Suction", description = "Suction cup, 40 mm",
                    kind = GripperKind.Suction, suction = new SuctionShape(),
                    accentColor = new Color(0.3f, 0.9f, 0.4f)
                }
            };
        }
    }
}
