using System;
using Oculus.Interaction;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// A block the player grabs to make a menu choice (a gripper, START, KEEP IMPROVING, MENU).
    /// Grabbing it raises Chosen; letting go returns it to its spot. It is not trash: it is never recorded,
    /// scored, or spawned. Needs a Grabbable (the builder adds grab interaction) and a kinematic Rigidbody.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ChoiceBlock : MonoBehaviour
    {
        [SerializeField] private string choiceId;

        [Tooltip("Found in children if left empty.")]
        [SerializeField] private Grabbable grabbable;

        [SerializeField] private Color baseColor = new Color(0.8f, 0.8f, 0.85f);

        [Tooltip("Seconds to glide back to its spot after being let go.")]
        [SerializeField] private float returnSeconds = 0.3f;

        /// <summary>Raised when a player grabs the block.</summary>
        public event Action<ChoiceBlock> Chosen;

        public string ChoiceId
        {
            get => choiceId;
            set => choiceId = value;
        }

        public bool Selected { get; private set; }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private Rigidbody body;
        private Renderer blockRenderer;
        private Renderer indicator;
        private Material indicatorMaterial;
        private Vector3 homePosition;
        private Quaternion homeRotation;
        private Vector3 homeScale;
        private bool held;
        private float pulseUntil;

        private void Awake()
        {
            body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            blockRenderer = GetComponent<Renderer>();
            if (grabbable == null) grabbable = GetComponentInChildren<Grabbable>();
            indicatorMaterial = VizUtil.FallbackMaterial();
            var face = VizUtil.CreateShape("Inset control face", transform, VizUtil.QuadMesh, indicatorMaterial, FacilityUi.Ink);
            face.transform.localPosition = new Vector3(0, 0, -.505f);
            face.transform.localScale = new Vector3(.78f, .78f, 1);
            indicator = VizUtil.CreateShape("Selection indicator", transform, VizUtil.QuadMesh, indicatorMaterial, baseColor);
            indicator.transform.localPosition = new Vector3(0, -.29f, -.51f);
            indicator.transform.localScale = new Vector3(.55f, .045f, 1);
            foreach (float x in new[] { -.16f, .16f })
            {
                var glyph = VizUtil.CreateShape("Grip symbol", transform, VizUtil.QuadMesh, indicatorMaterial, Color.white);
                glyph.transform.localPosition = new Vector3(x, .04f, -.51f);
                glyph.transform.localScale = new Vector3(.09f, .29f, 1);
            }
            SetHome();
            ApplyColor();
        }

        private void OnEnable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised += HandlePointerEvent;
        }

        private void OnDisable()
        {
            if (grabbable != null) grabbable.WhenPointerEventRaised -= HandlePointerEvent;
            held = false;
            ReturnHomeNow();
        }

        /// <summary>Remembers the current pose as the spot the block returns to.</summary>
        public void SetHome()
        {
            homePosition = transform.localPosition;
            homeRotation = transform.localRotation;
            homeScale = transform.localScale;
        }

        public void SetColor(Color color)
        {
            baseColor = color;
            ApplyColor();
        }

        public void SetSelected(bool selected)
        {
            Selected = selected;
            ApplyColor();
        }

        private void HandlePointerEvent(PointerEvent evt)
        {
            switch (evt.Type)
            {
                case PointerEventType.Select:
                    held = true;
                    pulseUntil = Time.time + 0.25f;
                    Chosen?.Invoke(this);
                    break;
                case PointerEventType.Unselect:
                case PointerEventType.Cancel:
                    if (grabbable.SelectingPointsCount == 0)
                    {
                        held = false;
                    }
                    break;
            }
        }

        private void Update()
        {
            // While held, the grab interaction moves the block; afterwards it glides back.
            if (!held)
            {
                float t = returnSeconds <= 0f ? 1f : Mathf.Clamp01(Time.deltaTime / returnSeconds * 4f);
                transform.localPosition = Vector3.Lerp(transform.localPosition, homePosition, t);
                transform.localRotation = Quaternion.Slerp(transform.localRotation, homeRotation, t);
            }
            float pulse = Time.time < pulseUntil ? 1.15f : 1f;
            transform.localScale = Vector3.Lerp(transform.localScale, homeScale * pulse, 0.3f);
        }

        private void ReturnHomeNow()
        {
            transform.localPosition = homePosition;
            transform.localRotation = homeRotation;
            transform.localScale = homeScale;
        }

        private void OnDestroy() { if (indicatorMaterial != null) { if (Application.isPlaying) Destroy(indicatorMaterial); else DestroyImmediate(indicatorMaterial); } }

        private void ApplyColor()
        {
            if (blockRenderer == null)
            {
                return;
            }
            // Selected blocks glow brighter.
            Color color = Selected ? Color.Lerp(baseColor, Color.white, 0.45f) : baseColor * 0.75f;
            color.a = 1f;
            var block = new MaterialPropertyBlock();
            blockRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            blockRenderer.SetPropertyBlock(block);
            if (indicator != null) VizUtil.SetColor(indicator, Selected ? FacilityUi.Accent : baseColor * .45f);
        }
    }
}
