using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>A nearby item's name only; never reveals the correct sorting answer.</summary>
    public sealed class ItemNameTag : MonoBehaviour
    {
        private TrashItem item;
        private Transform card;
        private Transform viewer;
        private Renderer[] geometry;

        public void Initialize(TrashItem target, Material material)
        {
            item = target;
            geometry = item.GetComponentsInChildren<Renderer>();
            var text = VizUtil.CreateText("Item name", null, Vector3.zero, new Vector2(.34f, .09f), .45f, TextAlignmentOptions.Center);
            text.gameObject.layer = item.gameObject.layer;
            text.text = TrashTypes.DisplayName(item.ItemType);
            card = text.transform;
            ReadableSign.Apply(text, new Vector2(.34f, .09f), .45f, new Color(.36f, .68f, .7f), material);
            card.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (card == null || item == null) return;
            if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
            bool visible = viewer != null && (item.State == TrashItemState.OnBelt || item.State == TrashItemState.Loose)
                && (viewer.position - item.transform.position).sqrMagnitude < 6.25f;
            card.gameObject.SetActive(visible);
            if (!visible) return;
            float top = item.transform.position.y;
            foreach (var renderer in geometry)
                if (renderer != null && renderer.enabled) top = Mathf.Max(top, renderer.bounds.max.y);
            card.position = new Vector3(item.transform.position.x, top + .09f, item.transform.position.z);
            Vector3 direction = card.position - viewer.position;
            if (direction.sqrMagnitude > .0001f) card.rotation = Quaternion.LookRotation(direction);
        }

        private void OnDisable() { if (card != null) card.gameObject.SetActive(false); }
        private void OnDestroy() { if (card != null) { if (Application.isPlaying) Destroy(card.gameObject); else DestroyImmediate(card.gameObject); } }
    }
}
