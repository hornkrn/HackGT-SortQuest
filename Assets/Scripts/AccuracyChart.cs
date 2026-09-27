using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Line chart of the robot's recent grasp success rate, one point per attempt.
    /// Each point is the success rate over the last few attempts, so the line climbs as the robot learns.
    /// Cleared at the start of each game.
    /// </summary>
    public class AccuracyChart : MonoBehaviour
    {
        [Tooltip("Found automatically if left empty.")]
        [SerializeField] private RobotGripper robot;

        [Tooltip("Optional; found automatically. The chart clears when a new game starts.")]
        [SerializeField] private GameManager game;

        [Tooltip("See-through material (Sprites/Default). Created if left empty.")]
        [SerializeField] private Material material;

        [SerializeField] private float width = 0.8f;
        [SerializeField] private float height = 0.45f;

        [Tooltip("Each point is the success rate over this many attempts.")]
        [SerializeField] private int window = 5;

        [Tooltip("The x axis always has room for at least this many attempts.")]
        [SerializeField] private int minSlots = 10;

        [SerializeField] private Color lineColor = new Color(1f, 0.6f, 0.15f);
        [SerializeField] private Color axisColor = new Color(1f, 1f, 1f, 0.6f);

        private readonly List<bool> results = new List<bool>();
        private LineRenderer line;
        private TextMeshPro title;
        private TextMeshPro empty;
        private Transform latestDot;

        private void Awake()
        {
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            if (game == null) game = FindAnyObjectByType<GameManager>();
            if (material == null) material = VizUtil.FallbackMaterial();
            Build();
        }

        private void OnEnable()
        {
            if (robot != null) robot.AttemptFinished += HandleAttempt;
            if (game != null) game.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (robot != null) robot.AttemptFinished -= HandleAttempt;
            if (game != null) game.StateChanged -= HandleStateChanged;
        }

        private void Build()
        {
            FacilityUi.Panel(transform, new Vector2(width + .3f, height + .38f), new Vector3(-.03f, .035f, 0), material);
            // Chart area runs from (0, 0) to (width, height), centered on this object.
            var origin = new Vector3(-width * 0.5f, -height * 0.5f, 0f);

            LineRenderer axes = VizUtil.CreateLine("Axes", transform, material, axisColor, 0.004f);
            axes.positionCount = 3;
            axes.SetPositions(new[]
            {
                origin + new Vector3(0f, height, 0f),
                origin,
                origin + new Vector3(width, 0f, 0f)
            });

            LineRenderer half = VizUtil.CreateLine("FiftyPercent", transform, material, axisColor * new Color(1f, 1f, 1f, 0.4f), 0.002f);
            half.positionCount = 2;
            half.SetPositions(new[] { origin + new Vector3(0f, height * 0.5f, 0f), origin + new Vector3(width, height * 0.5f, 0f) });

            line = VizUtil.CreateLine("Accuracy", transform, material, FacilityUi.Accent, 0.008f);
            line.numCornerVertices = 3;
            line.numCapVertices = 3;
            latestDot = VizUtil.CreateShape("Latest sample", transform, VizUtil.QuadMesh, material, FacilityUi.Accent).transform;
            latestDot.localScale = Vector3.one * .018f;
            var axisLabel = VizUtil.CreateText("Attempt axis", transform, new Vector3(0, -height * .5f - .06f, 0), new Vector2(width, .05f), .25f, TextAlignmentOptions.Center);
            axisLabel.text = "ATTEMPTS →"; axisLabel.color = FacilityUi.Muted;
            empty = VizUtil.CreateText("Empty state", transform, Vector3.zero, new Vector2(width, .15f), .32f, TextAlignmentOptions.Center);
            empty.text = "Waiting for the first attempt"; empty.color = FacilityUi.Muted;

            VizUtil.CreateText("Label100", transform, origin + new Vector3(-0.06f, height, 0f), new Vector2(0.12f, 0.05f),
                0.35f, TextAlignmentOptions.Right).text = "100%";
            VizUtil.CreateText("Label0", transform, origin + new Vector3(-0.06f, 0f, 0f), new Vector2(0.12f, 0.05f),
                0.35f, TextAlignmentOptions.Right).text = "0%";

            title = VizUtil.CreateText("Title", transform, new Vector3(0f, height * 0.5f + 0.1f, 0f),
                new Vector2(width + 0.3f, 0.15f), 0.5f, TextAlignmentOptions.Center);
            FacilityUi.Style(title, .65f, true);
            Redraw();
        }

        private void HandleAttempt(RobotGripper.Attempt attempt)
        {
            if (!attempt.Counts)
            {
                return; // Aborted for reasons outside the robot's control.
            }
            results.Add(attempt.Success);
            Redraw();
        }

        private void HandleStateChanged(GameState state)
        {
            if (state == GameState.Intro)
            {
                results.Clear();
                Redraw();
            }
        }

        private void Redraw()
        {
            var origin = new Vector3(-width * 0.5f, -height * 0.5f, 0f);
            int slots = Mathf.Max(minSlots, results.Count);
            line.positionCount = results.Count;
            float latest = 0f;
            for (int i = 0; i < results.Count; i++)
            {
                latest = RecentRate(i);
                line.SetPosition(i, origin + new Vector3(width * (i + 1) / slots, height * latest, 0f));
            }

            latestDot.gameObject.SetActive(results.Count > 0);
            empty.gameObject.SetActive(results.Count == 0);
            if (results.Count > 0) latestDot.localPosition = line.GetPosition(results.Count - 1) + Vector3.back * .002f;
            title.text = results.Count == 0
                ? "GRASP SUCCESS\n<size=60%>Recent attempts</size>"
                : $"GRASP SUCCESS   {latest * 100f:F0}%\n<size=60%>Last {Mathf.Min(window, results.Count)} • {results.Count} attempts total</size>";
        }

        /// <summary>Success rate over the attempts ending at index i.</summary>
        private float RecentRate(int i)
        {
            int start = Mathf.Max(0, i - window + 1);
            int successes = 0;
            for (int j = start; j <= i; j++)
            {
                if (results[j])
                {
                    successes++;
                }
            }
            return (float)successes / (i - start + 1);
        }
    }
}
