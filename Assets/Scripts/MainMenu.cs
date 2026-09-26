using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// The in-VR menu: a small table with one grabbable block per gripper and a START block.
    /// Grabbing a gripper block selects that gripper (the robot changes right away); grabbing START begins the game.
    /// After Results, two blocks offer KEEP IMPROVING (more lessons) or MENU. Shown only in the Menu and Results states.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        public const string StartId = "start";
        public const string KeepImprovingId = "keep_improving";
        public const string BackToMenuId = "back_to_menu";

        [Tooltip("Prefab with ChoiceBlock and grab interaction (made by the scene builder).")]
        [SerializeField] private ChoiceBlock blockPrefab;

        [Header("References (found automatically if empty)")]
        [SerializeField] private GripperCatalog catalog;
        [SerializeField] private GameManager game;
        [SerializeField] private GripperProgress progress;

        [Header("Placement")]
        [Tooltip("Each time the menu appears, move it to a comfortable height below the player's eyes.")]
        [SerializeField] private bool matchPlayerHeight = true;

        [Tooltip("How far below the player's eyes the blocks sit, in meters.")]
        [SerializeField] private float belowEyes = 0.45f;

        [SerializeField] private float minHeight = 0.95f;
        [SerializeField] private float maxHeight = 1.5f;

        [Tooltip("Keep adjusting for this long after the menu appears (the headset's height can take a moment to settle).")]
        [SerializeField] private float settleSeconds = 3f;

        [Header("Look")]
        [SerializeField] private Material tableMaterial;
        [Tooltip("Spacing between gripper blocks, in meters.")]
        [SerializeField] private float spacing = 0.16f;
        [SerializeField] private Color startColor = new Color(0.25f, 0.85f, 0.35f);
        [SerializeField] private Color keepImprovingColor = new Color(0.25f, 0.6f, 1f);
        [SerializeField] private Color backToMenuColor = new Color(0.85f, 0.3f, 0.3f);

        private Transform menuRoot;
        private Transform resultsRoot;
        private readonly Dictionary<string, ChoiceBlock> gripperBlocks = new Dictionary<string, ChoiceBlock>();
        private readonly Dictionary<string, TextMeshPro> gripperLabels = new Dictionary<string, TextMeshPro>();
        private float placeUntil;

        private void Awake()
        {
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (game == null) game = FindAnyObjectByType<GameManager>();
            if (progress == null) progress = FindAnyObjectByType<GripperProgress>();
        }

        private void OnEnable()
        {
            if (game != null) game.StateChanged += HandleStateChanged;
            if (catalog != null) catalog.Changed += HandleGripperChanged;
        }

        private void OnDisable()
        {
            if (game != null) game.StateChanged -= HandleStateChanged;
            if (catalog != null) catalog.Changed -= HandleGripperChanged;
        }

        // Built in Start so the catalog has chosen its default gripper.
        private void Start()
        {
            if (blockPrefab == null || catalog == null || game == null)
            {
                Debug.LogError("[SortQuest] MainMenu needs a block prefab, a GripperCatalog, and a GameManager.", this);
                enabled = false;
                return;
            }
            Build();
            HandleStateChanged(game.State);
        }

        /// <summary>True if the menu is set up and can be used; the GameManager skips the Menu state otherwise.</summary>
        public bool Ready => enabled && blockPrefab != null && catalog != null;

        private void Build()
        {
            menuRoot = new GameObject("MenuChoices").transform;
            menuRoot.SetParent(transform, false);
            resultsRoot = new GameObject("ResultsChoices").transform;
            resultsRoot.SetParent(transform, false);

            IReadOnlyList<GripperProfile> profiles = catalog.Profiles;
            float width = Mathf.Max(0.4f, spacing * profiles.Count + 0.06f);
            BuildTable(menuRoot, width);
            BuildTable(resultsRoot, 0.45f);

            // One block per gripper in the far row, START in the near row.
            for (int i = 0; i < profiles.Count; i++)
            {
                GripperProfile profile = profiles[i];
                var position = new Vector3((i - (profiles.Count - 1) * 0.5f) * spacing, 0f, 0.06f);
                ChoiceBlock block = AddBlock(menuRoot, profile.id, position, profile.accentColor);
                gripperBlocks[profile.id] = block;
                gripperLabels[profile.id] = AddLabel(menuRoot, position, "");
            }
            AddBlock(menuRoot, StartId, new Vector3(0f, 0f, -0.1f), startColor);
            AddLabel(menuRoot, new Vector3(0f, 0f, -0.1f), "START").fontSize = 0.4f;

            AddBlock(resultsRoot, KeepImprovingId, new Vector3(-0.12f, 0f, 0f), keepImprovingColor);
            AddLabel(resultsRoot, new Vector3(-0.12f, 0f, 0f), "KEEP\nIMPROVING");
            AddBlock(resultsRoot, BackToMenuId, new Vector3(0.12f, 0f, 0f), backToMenuColor);
            AddLabel(resultsRoot, new Vector3(0.12f, 0f, 0f), "MENU");

            RefreshLabels();
        }

        private void BuildTable(Transform parent, float width)
        {
            if (tableMaterial == null)
            {
                return;
            }
            // Visual only (no collider), so it never gets in the way of hands or items.
            Transform top = VizUtil.CreateShape("TableTop", parent, VizUtil.CubeMesh, tableMaterial).transform;
            top.localPosition = new Vector3(0f, -0.075f, -0.02f);
            top.localScale = new Vector3(width, 0.03f, 0.28f);
        }

        private ChoiceBlock AddBlock(Transform parent, string id, Vector3 localPosition, Color color)
        {
            ChoiceBlock block = Instantiate(blockPrefab, parent);
            block.name = $"Choice_{id}";
            block.ChoiceId = id;
            block.transform.localPosition = localPosition;
            block.transform.localRotation = Quaternion.identity;
            block.SetHome();
            block.SetColor(color);
            block.Chosen += HandleChosen;
            return block;
        }

        private TextMeshPro AddLabel(Transform parent, Vector3 blockPosition, string text)
        {
            TextMeshPro label = VizUtil.CreateText("Label", parent, blockPosition + new Vector3(0f, 0.12f, 0f),
                new Vector2(0.16f, 0.12f), 0.28f, TextAlignmentOptions.Center);
            label.gameObject.AddComponent<Billboard>();
            label.text = text;
            return label;
        }

        private void HandleChosen(ChoiceBlock block)
        {
            switch (block.ChoiceId)
            {
                case StartId:
                case KeepImprovingId:
                case BackToMenuId:
                    // These hide the block the player is holding. Wait a frame so the Interaction SDK finishes
                    // registering the grab first; hiding it mid-grab makes the SDK's throw handler throw.
                    StartCoroutine(ActNextFrame(block.ChoiceId));
                    break;
                default:
                    catalog.Select(block.ChoiceId);
                    break;
            }
        }

        private IEnumerator ActNextFrame(string choiceId)
        {
            yield return null;
            switch (choiceId)
            {
                case StartId:
                    game.StartGame();
                    break;
                case KeepImprovingId:
                    game.KeepImproving();
                    break;
                case BackToMenuId:
                    game.BackToMenu();
                    break;
            }
        }

        private void HandleGripperChanged(GripperProfile profile)
        {
            RefreshLabels();
        }

        private void HandleStateChanged(GameState state)
        {
            if (menuRoot == null)
            {
                return;
            }
            if (state == GameState.Menu || state == GameState.Results)
            {
                placeUntil = Time.time + settleSeconds;
                PlaceForPlayer();
            }
            menuRoot.gameObject.SetActive(state == GameState.Menu);
            resultsRoot.gameObject.SetActive(state == GameState.Results);
            if (state == GameState.Menu)
            {
                RefreshLabels();
            }
        }

        private void Update()
        {
            if (Time.time < placeUntil)
            {
                PlaceForPlayer();
            }
        }

        /// <summary>Moves the menu up or down to a comfortable reach below the player's eyes.</summary>
        private void PlaceForPlayer()
        {
            Camera eyes = Camera.main;
            if (!matchPlayerHeight || eyes == null)
            {
                return;
            }
            Vector3 position = transform.position;
            position.y = Mathf.Clamp(eyes.transform.position.y - belowEyes, minHeight, maxHeight);
            transform.position = position;
        }

        private void RefreshLabels()
        {
            string current = GripperCatalog.CurrentOrStandard(catalog).id;
            foreach (GripperProfile profile in catalog.Profiles)
            {
                if (gripperBlocks.TryGetValue(profile.id, out ChoiceBlock block))
                {
                    block.SetSelected(profile.id == current);
                }
                if (gripperLabels.TryGetValue(profile.id, out TextMeshPro label))
                {
                    string badge = progress != null ? progress.Badge(profile.id) : "";
                    string selected = profile.id == current ? "<b>SELECTED</b>\n" : "";
                    label.text = $"{selected}{profile.displayName}\n<size=70%>{profile.description}\n{badge}</size>";
                }
            }
        }
    }
}
