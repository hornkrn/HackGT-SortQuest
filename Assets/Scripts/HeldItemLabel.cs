using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Shows the name of the item the player is holding, floating just above it.
    /// </summary>
    public class HeldItemLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text text;

        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private TrashSpawner spawner;

        [Tooltip("Meters above the item's center.")]
        [SerializeField] private float heightAbove = 0.12f;

        private TrashItem heldItem;
        private Renderer[] heldGeometry;

        private void Awake()
        {
            if (text == null)
            {
                text = GetComponent<TMP_Text>();
            }
            if (spawner == null)
            {
                spawner = FindAnyObjectByType<TrashSpawner>();
            }
            text.enabled = false;
        }

        private void OnEnable()
        {
            if (spawner != null)
            {
                spawner.ItemSpawned += Track;
            }
        }

        private void OnDisable()
        {
            if (spawner != null)
            {
                spawner.ItemSpawned -= Track;
            }
        }

        private void Start()
        {
            foreach (TrashItem item in FindObjectsByType<TrashItem>(FindObjectsSortMode.None))
            {
                Track(item);
            }
        }

        private void Track(TrashItem item)
        {
            item.Grabbed -= HandleGrabbed;
            item.Released -= HandleReleased;
            item.Grabbed += HandleGrabbed;
            item.Released += HandleReleased;
        }

        private void HandleGrabbed(TrashItem item)
        {
            heldItem = item;
            heldGeometry = item.GetComponentsInChildren<Renderer>();
            text.text = TrashTypes.DisplayName(item.ItemType);
            text.enabled = true;
        }

        private void HandleReleased(TrashItem item)
        {
            if (heldItem == item)
            {
                heldItem = null;
                text.enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (heldItem == null)
            {
                text.enabled = false;
                return;
            }
            Vector3 position = heldItem.transform.position;
            foreach (var renderer in heldGeometry)
                if (renderer != null && renderer.enabled) position.y = Mathf.Max(position.y, renderer.bounds.max.y);
            transform.position = position + Vector3.up * heightAbove;
        }
    }
}
