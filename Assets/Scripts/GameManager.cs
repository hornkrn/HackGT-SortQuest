using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    public enum GameState
    {
        Intro,
        HumanRound,
        Training,
        RobotRound,
        TeachMe,
        Results
    }

    /// <summary>
    /// Runs one game: Intro, HumanRound (the player sorts and teaches), Training (recap), RobotRound
    /// (the robot sorts alone), then lessons: TeachMe (the player demonstrates the robot's weakest item)
    /// followed by a short robot retry on that item, repeated until the robot has mastered every item
    /// or the lesson limit is reached. Then Results, and back to Intro for the next player.
    /// Picking the weakest item each time is active learning: new data goes where the robot is worst.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        private class Tally
        {
            public int Attempts;
            public int Successes;
        }

        [Header("References (found automatically if empty)")]
        [SerializeField] private TrashSpawner spawner;
        [SerializeField] private RobotGripper robot;
        [SerializeField] private ScoreBoard scoreBoard;
        [SerializeField] private GraspDataset dataset;
        [SerializeField] private GraspAugmenter augmenter;

        [Tooltip("Big text in front of the player that shows the current state and timer.")]
        [SerializeField] private TMP_Text statusText;

        [Header("Timing (seconds)")]
        [SerializeField] private float introSeconds = 10f;
        [SerializeField] private float humanRoundSeconds = 60f;
        [SerializeField] private float trainingSeconds = 6f;
        [SerializeField] private float robotRoundSeconds = 45f;
        [SerializeField] private float teachMeSeconds = 45f;
        [SerializeField] private float retrySeconds = 25f;
        [SerializeField] private float resultsSeconds = 20f;

        [Header("Rules")]
        [Tooltip("TeachMe ends once the player makes this many good grasps of the item.")]
        [SerializeField] private int teachTarget = 3;

        [Tooltip("During the robot's turn it picks from this point on the belt onward (0 = start, 1 = end).")]
        [SerializeField, Range(0f, 1f)] private float robotPickZoneStart = 0.25f;

        [Header("Keep teaching until mastered")]
        [Tooltip("An item is mastered when the robot's latest tries on it succeed at least this often.")]
        [SerializeField, Range(0f, 1f)] private float masteryRate = 0.6f;

        [Tooltip("...over at least this many tries.")]
        [SerializeField, Min(1)] private int masteryMinAttempts = 2;

        [Tooltip("Go to Results after this many lessons even if some items aren't mastered (0 = no limit).")]
        [SerializeField, Min(0)] private int maxLessons = 6;

        [Tooltip("After Results, start again at Intro for the next player.")]
        [SerializeField] private bool loopForNextPlayer = true;

        public event Action<GameState> StateChanged;

        public GameState State { get; private set; }

        /// <summary>True when the robot mastered every item before the lesson limit.</summary>
        public bool Certified { get; private set; }

        /// <summary>The item the robot did worst on, chosen at the start of TeachMe.</summary>
        public ItemType? TeachItem { get; private set; }

        /// <summary>True during the robot's retry on the taught item (a RobotRound after TeachMe).</summary>
        public bool IsRetry { get; private set; }

        private float stateStartTime;
        private float nextStatusRefresh;
        private int goodGraspsThisRound;
        private int goodGraspsTaught;
        private int goodGraspsTaughtTotal;
        private bool finishRequested;
        private int lessonsGiven;

        // Items taught in this game, in order, each listed once (for Results).
        private readonly List<ItemType> lessons = new List<ItemType>();
        private int robotRoundAttempts;
        private int robotRoundSuccesses;
        private int variationsTried;
        private int variationsKept;
        private readonly Dictionary<ItemType, Tally> firstRobotRound = new Dictionary<ItemType, Tally>();
        private readonly Dictionary<ItemType, Tally> retryRobotRound = new Dictionary<ItemType, Tally>();
        private readonly Dictionary<ItemType, int> goodGraspsByType = new Dictionary<ItemType, int>();

        private float Remaining => Mathf.Max(0f, CurrentDuration() - (Time.time - stateStartTime));

        private void Awake()
        {
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            if (scoreBoard == null) scoreBoard = FindAnyObjectByType<ScoreBoard>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (augmenter == null) augmenter = FindAnyObjectByType<GraspAugmenter>();
            if (spawner == null)
            {
                Debug.LogError("[SortQuest] GameManager needs a TrashSpawner.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (dataset != null) dataset.RecordAdded += HandleRecordAdded;
            if (robot != null) robot.AttemptFinished += HandleRobotAttempt;
            if (augmenter != null) augmenter.Practiced += HandlePracticed;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
            if (robot != null) robot.AttemptFinished -= HandleRobotAttempt;
            if (augmenter != null) augmenter.Practiced -= HandlePracticed;
        }

        private void Start()
        {
            SetState(GameState.Intro);
        }

        private void Update()
        {
            bool timeUp = Remaining <= 0f;
            switch (State)
            {
                case GameState.Intro:
                    if (timeUp) SetState(GameState.HumanRound);
                    break;
                case GameState.HumanRound:
                    if (timeUp) SetState(GameState.Training);
                    break;
                case GameState.Training:
                    if (timeUp) SetState(GameState.RobotRound);
                    break;
                case GameState.RobotRound:
                    if (timeUp) SetState(NextAfterRobotRound());
                    break;
                case GameState.TeachMe:
                    if (timeUp || goodGraspsTaught >= teachTarget)
                    {
                        IsRetry = true;
                        SetState(GameState.RobotRound);
                    }
                    break;
                case GameState.Results:
                    if (timeUp && loopForNextPlayer) SetState(GameState.Intro);
                    break;
            }

            if (Time.time >= nextStatusRefresh)
            {
                RefreshStatus();
            }
        }

        [ContextMenu("Finish teaching (go to Results after this step)")]
        public void FinishTeaching()
        {
            finishRequested = true;
        }

        [ContextMenu("Skip to next state")]
        public void SkipToNextState()
        {
            // Pretend the timer ran out; Update moves on next frame.
            stateStartTime = -10000f;
        }

        public void SetState(GameState state)
        {
            State = state;
            stateStartTime = Time.time;
            Enter(state);
            string detail = state == GameState.RobotRound && IsRetry ? $" (retry on {TeachItem})"
                : state == GameState.TeachMe ? $" (lesson {lessonsGiven}: {TeachItem}, mastered {CountMastered()} of {ItemCount})"
                : state == GameState.Results ? $" ({(Certified ? "certified" : "not certified")}, mastered {CountMastered()} of {ItemCount})"
                : "";
            Debug.Log($"[SortQuest] Game state: {state}{detail}");
            StateChanged?.Invoke(state);
            RefreshStatus();
        }

        private void Enter(GameState state)
        {
            // Score counts only while the player is sorting for points.
            if (scoreBoard != null)
            {
                scoreBoard.Counting = state == GameState.HumanRound;
            }

            switch (state)
            {
                case GameState.Intro:
                    StopEverything();
                    TeachItem = null;
                    IsRetry = false;
                    Certified = false;
                    finishRequested = false;
                    lessons.Clear();
                    lessonsGiven = 0;
                    goodGraspsTaughtTotal = 0;
                    firstRobotRound.Clear();
                    retryRobotRound.Clear();
                    goodGraspsByType.Clear();
                    goodGraspsThisRound = 0;
                    variationsTried = 0;
                    variationsKept = 0;
                    if (scoreBoard != null) scoreBoard.ResetScore();
                    if (robot != null) robot.ResetStats();
                    break;

                case GameState.HumanRound:
                    if (scoreBoard != null) scoreBoard.ResetScore();
                    goodGraspsThisRound = 0;
                    goodGraspsByType.Clear();
                    StartSpawning(null);
                    break;

                case GameState.Training:
                    StopEverything();
                    break;

                case GameState.RobotRound:
                    spawner.ClearItems();
                    if (IsRetry && TeachItem.HasValue)
                    {
                        // Only this lesson's tries count as the item's latest result.
                        retryRobotRound.Remove(TeachItem.Value);
                    }
                    robotRoundAttempts = 0;
                    robotRoundSuccesses = 0;
                    StartSpawning(IsRetry ? TeachItem : null);
                    if (robot != null)
                    {
                        robot.ResetRobot();
                        robot.PickZoneStart = robotPickZoneStart;
                        robot.Active = true;
                    }
                    break;

                case GameState.TeachMe:
                    StopEverything();
                    TeachItem = PickTeachItem();
                    goodGraspsTaught = 0;
                    lessonsGiven++;
                    lessons.Remove(TeachItem.Value);
                    lessons.Add(TeachItem.Value);
                    StartSpawning(TeachItem);
                    break;

                case GameState.Results:
                    StopEverything();
                    break;
            }
        }

        private void StartSpawning(ItemType? onlyType)
        {
            spawner.OnlyType = onlyType;
            spawner.Spawning = true;
            if (robot != null)
            {
                robot.Active = false;
            }
        }

        private void StopEverything()
        {
            spawner.Spawning = false;
            spawner.OnlyType = null;
            if (robot != null)
            {
                robot.Active = false;
                robot.ResetRobot();
            }
            spawner.ClearItems();
        }

        private float CurrentDuration()
        {
            switch (State)
            {
                case GameState.Intro: return introSeconds;
                case GameState.HumanRound: return humanRoundSeconds;
                case GameState.Training: return trainingSeconds;
                case GameState.RobotRound: return IsRetry ? retrySeconds : robotRoundSeconds;
                case GameState.TeachMe: return teachMeSeconds;
                case GameState.Results: return resultsSeconds;
                default: return 0f;
            }
        }

        // ---------- Event tracking ----------

        private void HandleRecordAdded(GraspRecord record)
        {
            if (record.source != GraspRecord.SourceHuman || !record.IsGood)
            {
                return;
            }
            if (State == GameState.HumanRound)
            {
                goodGraspsThisRound++;
                ItemType type = ParseItemType(record.item_type);
                goodGraspsByType.TryGetValue(type, out int count);
                goodGraspsByType[type] = count + 1;
            }
            else if (State == GameState.TeachMe && TeachItem.HasValue &&
                     record.item_type == TrashTypes.ItemId(TeachItem.Value))
            {
                goodGraspsTaught++;
                goodGraspsTaughtTotal++;
            }
        }

        private void HandlePracticed(GraspRecord original, int tried, int kept)
        {
            variationsTried += tried;
            variationsKept += kept;
        }

        private void HandleRobotAttempt(RobotGripper.Attempt attempt)
        {
            if (State != GameState.RobotRound || attempt.Item == null)
            {
                return;
            }
            robotRoundAttempts++;
            if (attempt.Success)
            {
                robotRoundSuccesses++;
            }
            Dictionary<ItemType, Tally> tallies = IsRetry ? retryRobotRound : firstRobotRound;
            if (!tallies.TryGetValue(attempt.Item.ItemType, out Tally tally))
            {
                tally = new Tally();
                tallies[attempt.Item.ItemType] = tally;
            }
            tally.Attempts++;
            if (attempt.Success)
            {
                tally.Successes++;
            }
        }

        /// <summary>
        /// After the robot's first round or a retry: the next lesson, or Results once every item is mastered,
        /// the lesson limit is reached, or someone chose to finish.
        /// </summary>
        private GameState NextAfterRobotRound()
        {
            if (AllMastered())
            {
                Certified = true;
                return GameState.Results;
            }
            if (finishRequested || (maxLessons > 0 && lessonsGiven >= maxLessons))
            {
                return GameState.Results;
            }
            return GameState.TeachMe;
        }

        /// <summary>The robot's most recent result on an item: its latest lesson's retry, or else its first round.</summary>
        private Tally LatestTally(ItemType type)
        {
            if (retryRobotRound.TryGetValue(type, out Tally retry) && retry.Attempts > 0)
            {
                return retry;
            }
            return firstRobotRound.TryGetValue(type, out Tally first) ? first : null;
        }

        private bool IsMastered(ItemType type)
        {
            Tally tally = LatestTally(type);
            return tally != null && tally.Attempts >= masteryMinAttempts &&
                   (float)tally.Successes / tally.Attempts >= masteryRate;
        }

        private bool AllMastered()
        {
            return CountMastered() == ItemCount;
        }

        private int CountMastered()
        {
            int count = 0;
            foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (IsMastered(type))
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// The next item to teach: not yet mastered, preferring items not taught yet in this game, then the
        /// lowest latest success rate (items the robot hasn't tried count as 50%), then fewer good grasps.
        /// </summary>
        private ItemType PickTeachItem()
        {
            ItemType best = ItemType.AluminumCan;
            bool found = false;
            bool bestTaught = true;
            float bestRate = float.MaxValue;
            int bestGood = int.MaxValue;
            foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (IsMastered(type))
                {
                    continue;
                }
                bool taught = lessons.Contains(type);
                Tally tally = LatestTally(type);
                float rate = tally != null && tally.Attempts > 0 ? (float)tally.Successes / tally.Attempts : 0.5f;
                int good = dataset != null ? dataset.CountGood(type) : 0;
                bool better = !found ||
                              (bestTaught && !taught) ||
                              (taught == bestTaught && (rate < bestRate - 1e-4f ||
                                                        (Mathf.Abs(rate - bestRate) <= 1e-4f && good < bestGood)));
                if (better)
                {
                    best = type;
                    found = true;
                    bestTaught = taught;
                    bestRate = rate;
                    bestGood = good;
                }
            }
            return best;
        }

        private static ItemType ParseItemType(string id)
        {
            foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (TrashTypes.ItemId(type) == id)
                {
                    return type;
                }
            }
            return ItemType.AluminumCan;
        }

        // ---------- Status text ----------

        private void RefreshStatus()
        {
            nextStatusRefresh = Time.time + 0.2f;
            if (statusText == null)
            {
                return;
            }

            string time = FormatTime(Remaining);
            var body = new StringBuilder();
            string title;
            switch (State)
            {
                case GameState.Intro:
                    title = "SORTQUEST";
                    body.AppendLine("Sort the trash into the right bins with your hands.");
                    body.AppendLine("Every good grab teaches the recycling robot.");
                    body.Append($"Starting in {Mathf.CeilToInt(Remaining)}");
                    break;

                case GameState.HumanRound:
                    title = $"YOUR TURN  {time}";
                    body.Append($"Good grasps taught: {goodGraspsThisRound}");
                    break;

                case GameState.Training:
                    title = "TRAINING THE ROBOT";
                    body.AppendLine($"You taught it {goodGraspsThisRound} good grasps this round.");
                    if (variationsTried > 0)
                    {
                        body.AppendLine($"It practiced {variationsTried} variations of them; {variationsKept} worked.");
                    }
                    foreach (KeyValuePair<ItemType, int> pair in goodGraspsByType)
                    {
                        body.AppendLine($"{TrashTypes.DisplayName(pair.Key)}: {pair.Value}");
                    }
                    break;

                case GameState.RobotRound:
                    title = IsRetry && TeachItem.HasValue
                        ? $"ROBOT TRIES AGAIN  {time}"
                        : $"ROBOT'S TURN  {time}";
                    body.AppendLine(IsRetry && TeachItem.HasValue
                        ? $"Can it grab the {TrashTypes.DisplayName(TeachItem.Value).ToLowerInvariant()} now?"
                        : "Watch it use what you taught it.");
                    body.AppendLine($"{robotRoundSuccesses} of {robotRoundAttempts} grasps worked");
                    body.Append(IsRetry
                        ? $"Mastered once {masteryRate * 100f:F0}% of its grasps work"
                        : $"Mastered {CountMastered()} of {ItemCount} items so far");
                    break;

                case GameState.TeachMe:
                    string item = TeachItem.HasValue ? TrashTypes.DisplayName(TeachItem.Value).ToLowerInvariant() : "item";
                    Tally latest = TeachItem.HasValue ? LatestTally(TeachItem.Value) : null;
                    string limit = maxLessons > 0 ? $" of up to {maxLessons}" : "";
                    title = $"TEACH ME  {time}";
                    body.AppendLine($"Lesson {lessonsGiven}{limit}. Robot has mastered {CountMastered()} of {ItemCount} items.");
                    body.AppendLine(latest == null || latest.Attempts == 0
                        ? $"The robot hasn't tried the {item} yet."
                        : $"The robot struggles with the {item}.");
                    body.Append($"Show it how! {goodGraspsTaught} of {teachTarget} good grasps");
                    break;

                default:
                    title = Certified ? "ROBOT CERTIFIED!" : "RESULTS";
                    AppendResults(body);
                    break;
            }
            statusText.text = $"{title}\n<size=55%>{body.ToString().TrimEnd()}</size>";
        }

        private void AppendResults(StringBuilder body)
        {
            if (scoreBoard != null)
            {
                body.AppendLine($"Your score: {scoreBoard.Score} ({scoreBoard.Correct} correct, " +
                                $"{scoreBoard.Wrong} wrong, {scoreBoard.Missed} missed)");
            }
            body.AppendLine($"Good grasps you taught: {goodGraspsThisRound + goodGraspsTaughtTotal}");
            if (variationsTried > 0)
            {
                body.AppendLine($"Variations the robot practiced: {variationsKept} of {variationsTried} worked");
            }

            int attempts = 0;
            int successes = 0;
            foreach (Tally tally in firstRobotRound.Values)
            {
                attempts += tally.Attempts;
                successes += tally.Successes;
            }
            body.AppendLine($"Robot's turn: {successes} of {attempts} grasps worked");

            body.AppendLine($"Robot mastered {CountMastered()} of {ItemCount} items after {lessonsGiven} lessons");
            foreach (ItemType taught in lessons)
            {
                firstRobotRound.TryGetValue(taught, out Tally before);
                retryRobotRound.TryGetValue(taught, out Tally after);
                body.AppendLine($"{TrashTypes.DisplayName(taught)}: {FormatTally(before)} before your lesson, " +
                                $"{FormatTally(after)} after{(IsMastered(taught) ? " (mastered)" : "")}");
            }
        }

        private static int ItemCount => Enum.GetValues(typeof(ItemType)).Length;

        private static string FormatTally(Tally tally)
        {
            return tally == null || tally.Attempts == 0 ? "no tries" : $"{tally.Successes} of {tally.Attempts}";
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.CeilToInt(seconds);
            return $"{total / 60}:{total % 60:00}";
        }
    }
}
