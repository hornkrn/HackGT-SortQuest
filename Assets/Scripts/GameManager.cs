using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    public enum GameState
    {
        Menu,
        Intro,
        HumanRound,
        Training,
        RobotRound,
        TeachMe,
        Results,
        Tutorial,
        TutorialChoice
    }

    /// <summary>
    /// Runs the game: Menu (pick a gripper, grab START), Tutorial (guided six-item onboarding),
    /// HumanRound (the player sorts and teaches),
    /// Training (recap), RobotRound (the robot sorts alone), then lessons: TeachMe (the player demonstrates the
    /// robot's weakest item) followed by a short robot retry on that item, repeated until the robot has mastered
    /// every item or the lesson limit is reached. Then Results, where the player can keep improving or go back
    /// to the menu. Picking the weakest item each time is active learning: new data goes where the robot is worst.
    /// Without a MainMenu in the scene, the game starts at Tutorial and loops back to it.
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
        [SerializeField] private MainMenu menu;
        [SerializeField] private GripperCatalog catalog;
        [SerializeField] private GripperProgress progress;
        private GuidedTutorial guidedTutorial;

        [Tooltip("Big text in front of the player that shows the current state and timer.")]
        [SerializeField] private TMP_Text statusText;

        [Header("Timing (seconds)")]
        [SerializeField] private float introSeconds = 10f;
        [SerializeField] private float humanRoundSeconds = 60f;
        [SerializeField] private float trainingSeconds = 6f;
        [SerializeField] private float robotRoundSeconds = 45f;
        [SerializeField] private float teachMeSeconds = 45f;
        [SerializeField] private float retrySeconds = 25f;
        [Tooltip("Results stays up this long if nobody chooses, then returns to the menu (or Intro without one).")]
        [SerializeField] private float resultsSeconds = 30f;

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

        [Tooltip("KEEP IMPROVING after the robot is certified raises the mastery bar by this much (up to 95%).")]
        [SerializeField, Range(0f, 0.5f)] private float keepImprovingBoost = 0.2f;

        [Tooltip("Without a menu: after Results, start again at Intro for the next player.")]
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
        private int lessonLimit;
        private float activeMasteryRate;
        private bool progressRecorded;
        private string tutorialStatus;

        // Items taught in this game, in order, each listed once (for Results).
        private readonly List<ItemType> lessons = new List<ItemType>();
        private int robotRoundAttempts;
        private int robotRoundSuccesses;

        // Items that finished their trip down the belt during the robot's turn (landed in a bin or were missed),
        // and how many of them the robot put in the correct bin. Failed grabs and items it never reached both count.
        private int robotRoundItems;
        private int robotRoundItemsSorted;
        private int firstRoundItems;
        private int firstRoundItemsSorted;
        private readonly HashSet<TrashItem> takenByPlayer = new HashSet<TrashItem>();
        private int variationsTried;
        private int variationsKept;
        private readonly Dictionary<ItemType, Tally> firstRobotRound = new Dictionary<ItemType, Tally>();
        private readonly Dictionary<ItemType, Tally> retryRobotRound = new Dictionary<ItemType, Tally>();
        private readonly Dictionary<ItemType, int> goodGraspsByType = new Dictionary<ItemType, int>();

        private float Remaining => Mathf.Max(0f, CurrentDuration() - (Time.time - stateStartTime));
        private bool HasMenu => menu != null && menu.Ready;
        private GripperProfile Gripper => GripperCatalog.CurrentOrStandard(catalog);

        private void Awake()
        {
            if (spawner == null) spawner = FindAnyObjectByType<TrashSpawner>();
            if (robot == null) robot = FindAnyObjectByType<RobotGripper>();
            if (scoreBoard == null) scoreBoard = FindAnyObjectByType<ScoreBoard>();
            if (dataset == null) dataset = FindAnyObjectByType<GraspDataset>();
            if (augmenter == null) augmenter = FindAnyObjectByType<GraspAugmenter>();
            if (menu == null) menu = FindAnyObjectByType<MainMenu>();
            if (catalog == null) catalog = FindAnyObjectByType<GripperCatalog>();
            if (progress == null) progress = FindAnyObjectByType<GripperProgress>();
            guidedTutorial = GetComponent<GuidedTutorial>();
            if (guidedTutorial == null) guidedTutorial = gameObject.AddComponent<GuidedTutorial>();
            guidedTutorial.Initialize(this, spawner);
            activeMasteryRate = masteryRate;
            lessonLimit = maxLessons;
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
            if (catalog != null) catalog.Changed += HandleGripperChanged;
            if (spawner != null) spawner.ItemSpawned += HandleItemSpawned;
        }

        private void OnDisable()
        {
            if (dataset != null) dataset.RecordAdded -= HandleRecordAdded;
            if (robot != null) robot.AttemptFinished -= HandleRobotAttempt;
            if (augmenter != null) augmenter.Practiced -= HandlePracticed;
            if (catalog != null) catalog.Changed -= HandleGripperChanged;
            if (spawner != null) spawner.ItemSpawned -= HandleItemSpawned;
        }

        private void HandleGripperChanged(GripperProfile profile)
        {
            RefreshStatus();
        }

        private void Start()
        {
            SetState(HasMenu ? GameState.Menu : GameState.Tutorial);
        }

        private void Update()
        {
            bool timeUp = Remaining <= 0f;
            switch (State)
            {
                case GameState.Menu:
                    break; // Waits for START.
                case GameState.TutorialChoice:
                case GameState.Tutorial:
                    break; // GuidedTutorial advances after each item is sorted.
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
                    if (timeUp)
                    {
                        if (HasMenu) SetState(GameState.Menu);
                        else if (loopForNextPlayer) SetState(GameState.Tutorial);
                    }
                    break;
            }

            if (Time.time >= nextStatusRefresh)
            {
                RefreshStatus();
            }
        }

        /// <summary>From the menu: start a game with the selected gripper.</summary>
        [ContextMenu("Start game (from the menu)")]
        public void StartGame()
        {
            if (State == GameState.Menu)
            {
                SetState(HasMenu ? GameState.TutorialChoice : GameState.Tutorial);
            }
        }

        /// <summary>Answer the tutorial prompt once; duplicate grabs cannot restart a round.</summary>
        public void ChooseTutorial(bool wantsTutorial)
        {
            if (State != GameState.TutorialChoice) return;
            if (wantsTutorial) SetState(GameState.Tutorial);
            else
            {
                spawner.Belt.Running = true;
                SetState(GameState.HumanRound);
            }
        }

        /// <summary>
        /// From Results: more lessons with the same gripper. If the robot was certified, the mastery bar goes up,
        /// so the extra lessons still have something to improve.
        /// </summary>
        [ContextMenu("Keep improving (from Results)")]
        public void KeepImproving()
        {
            if (State != GameState.Results)
            {
                return;
            }
            if (Certified)
            {
                activeMasteryRate = Mathf.Min(0.95f, activeMasteryRate + keepImprovingBoost);
            }
            Certified = false;
            finishRequested = false;
            lessonLimit = lessonsGiven + Mathf.Max(1, maxLessons);
            IsRetry = true;
            SetState(GameState.TeachMe);
        }

        /// <summary>From Results (or anytime): back to the menu to pick a gripper.</summary>
        [ContextMenu("Back to menu")]
        public void BackToMenu()
        {
            if (!HasMenu)
            {
                return;
            }
            if (State != GameState.Menu && State != GameState.Results)
            {
                // Leaving mid-game keeps the data clean (StopEverything): the robot's attempt in progress is saved as
                // "aborted" (never scored or learned from); items already released but not yet landed are removed and
                // their grabs discarded rather than labeled failures; an item still in a player's hand keeps recording
                // until it lands. Practice runs on hidden copies and saves whole jobs only, so it is unaffected.
                // Unfinished games aren't counted in gripper progress, which is recorded at Results.
                Debug.Log($"[SortQuest] Game ended early from {State}; back to the menu.");
            }
            SetState(GameState.Menu);
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
            if (State == GameState.Tutorial && state != GameState.Tutorial) guidedTutorial.Cancel();
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
                case GameState.Menu:
                    StopEverything();
                    break;

                case GameState.Intro:
                case GameState.TutorialChoice:
                case GameState.Tutorial:
                    StopEverything();
                    ResetRun();
                    if (state == GameState.Tutorial) guidedTutorial.Begin();
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
                    robotRoundItems = 0;
                    robotRoundItemsSorted = 0;
                    takenByPlayer.Clear();
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
                    if (progress != null)
                    {
                        // One game per trip through Intro; extra lessons after KEEP IMPROVING update the same game.
                        progress.Record(Gripper.id, Certified, CountMastered(), ItemCount, !progressRecorded);
                        progressRecorded = true;
                    }
                    break;
            }
        }

        private void ResetRun()
        {
            activeMasteryRate = masteryRate;
            lessonLimit = maxLessons;
            progressRecorded = false;
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
            firstRoundItems = 0;
            firstRoundItemsSorted = 0;
            if (scoreBoard != null) scoreBoard.ResetScore();
            if (robot != null) robot.ResetStats();
        }

        public void SetTutorialStatus(string message)
        {
            tutorialStatus = message;
            if (State == GameState.Tutorial) RefreshStatus();
        }

        public void CompleteTutorial()
        {
            if (State == GameState.Tutorial) SetState(GameState.HumanRound);
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
                case GameState.Tutorial: return 0f;
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

        private void HandleItemSpawned(TrashItem item)
        {
            item.Grabbed += HandleItemGrabbed;
            item.Sorted += HandleItemSorted;
            item.Missed += HandleItemMissed;
        }

        private void HandleItemGrabbed(TrashItem item)
        {
            if (State == GameState.RobotRound) takenByPlayer.Add(item);
        }

        private void HandleItemSorted(TrashItem item, SortingBin bin, bool correct)
        {
            CountRobotRoundItem(item, correct && item.LastHeldByRobot);
        }

        private void HandleItemMissed(TrashItem item)
        {
            CountRobotRoundItem(item, false);
        }

        private void CountRobotRoundItem(TrashItem item, bool sortedByRobot)
        {
            // An item a person grabbed during the robot's turn wasn't the robot's to sort.
            if (State != GameState.RobotRound || takenByPlayer.Contains(item))
            {
                return;
            }
            robotRoundItems++;
            if (sortedByRobot) robotRoundItemsSorted++;
            if (!IsRetry)
            {
                firstRoundItems++;
                if (sortedByRobot) firstRoundItemsSorted++;
            }
        }

        private void HandlePracticed(GraspRecord original, int tried, int kept)
        {
            variationsTried += tried;
            variationsKept += kept;
        }

        private void HandleRobotAttempt(RobotGripper.Attempt attempt)
        {
            if (State != GameState.RobotRound || attempt.Item == null || !attempt.Counts)
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
            if (finishRequested || (lessonLimit > 0 && lessonsGiven >= lessonLimit))
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
                   (float)tally.Successes / tally.Attempts >= activeMasteryRate;
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
        /// If everything is mastered (KEEP IMPROVING), the weakest item overall.
        /// </summary>
        private ItemType PickTeachItem()
        {
            ItemType best = PickTeachItem(skipMastered: true, out bool found);
            return found ? best : PickTeachItem(skipMastered: false, out _);
        }

        private ItemType PickTeachItem(bool skipMastered, out bool found)
        {
            ItemType best = ItemType.AluminumCan;
            found = false;
            bool bestTaught = true;
            float bestRate = float.MaxValue;
            int bestGood = int.MaxValue;
            foreach (ItemType type in (ItemType[])Enum.GetValues(typeof(ItemType)))
            {
                if (skipMastered && IsMastered(type))
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
                case GameState.Menu:
                    title = "CHOOSE A GRIPPER";
                    body.AppendLine("Choose a tool at the console below.");
                    body.AppendLine($"Ready: {Gripper.displayName}");
                    if (progress != null)
                    {
                        body.AppendLine($"This gripper: {progress.Badge(Gripper.id)}");
                    }
                    if (augmenter != null && augmenter.PendingGrasps > 0)
                    {
                        body.Append($"The robot is studying {augmenter.PendingGrasps} grasps for this gripper...");
                    }
                    break;

                case GameState.TutorialChoice:
                    title = "WOULD YOU LIKE A TUTORIAL?";
                    body.AppendLine("Grab YES to learn, or NO to start playing.");
                    break;

                case GameState.Tutorial:
                    title = "SORTQUEST TUTORIAL";
                    body.AppendLine(tutorialStatus ?? "Listen to the guide and sort each item into the highlighted bin.");
                    break;

                case GameState.Intro:
                    title = "SORTQUEST";
                    body.AppendLine("Sort the trash into the right bins with your hands.");
                    body.AppendLine($"Every good grab teaches the robot's {Gripper.displayName.ToLowerInvariant()} gripper.");
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
                    body.AppendLine("Learning details appear on the left display.");
                    break;

                case GameState.RobotRound:
                    title = IsRetry && TeachItem.HasValue
                        ? $"ROBOT TRIES AGAIN  {time}"
                        : $"ROBOT'S TURN  {time}";
                    body.AppendLine(IsRetry && TeachItem.HasValue
                        ? $"Can it grab the {TrashTypes.DisplayName(TeachItem.Value).ToLowerInvariant()} now?"
                        : "Watch it use what you taught it.");
                    body.AppendLine($"{robotRoundSuccesses} of {robotRoundAttempts} grasps worked");
                    body.AppendLine($"Sorted {robotRoundItemsSorted} of {robotRoundItems} items that came down the belt");
                    body.Append(IsRetry
                        ? $"Mastered once {activeMasteryRate * 100f:F0}% of its grasps work"
                        : $"Mastered {CountMastered()} of {ItemCount} items so far");
                    break;

                case GameState.TeachMe:
                    string item = TeachItem.HasValue ? TrashTypes.DisplayName(TeachItem.Value).ToLowerInvariant() : "item";
                    Tally latest = TeachItem.HasValue ? LatestTally(TeachItem.Value) : null;
                    string limit = lessonLimit > 0 ? $" of up to {lessonLimit}" : "";
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
                    if (HasMenu)
                    {
                        body.AppendLine(Certified
                            ? "KEEP IMPROVING to raise the bar • MENU to change tools"
                            : "KEEP IMPROVING for more lessons • MENU to finish");
                    }
                    break;
            }
            statusText.text = $"{title}\n<size=55%>{body.ToString().TrimEnd()}</size>";
        }

        private void AppendResults(StringBuilder body)
        {
            body.AppendLine($"{goodGraspsThisRound + goodGraspsTaughtTotal} grasps taught   •   {variationsKept} successful practice variations");
            int attempts = 0, successes = 0;
            foreach (Tally tally in firstRobotRound.Values) { attempts += tally.Attempts; successes += tally.Successes; }
            body.AppendLine($"Robot: {successes}/{attempts} grasps   •   {firstRoundItemsSorted}/{firstRoundItems} items sorted");
            body.AppendLine($"{CountMastered()}/{ItemCount} materials mastered   •   {lessonsGiven} lessons");
        }

        /// <summary>A short spoken version of the Results screen, for the results voice.</summary>
        public string SpokenResults()
        {
            int attempts = 0, successes = 0;
            foreach (Tally tally in firstRobotRound.Values) { attempts += tally.Attempts; successes += tally.Successes; }
            var text = new StringBuilder(Certified ? "Robot certified! " : "Here are your results. ");
            text.Append($"You taught the robot {Plural(goodGraspsThisRound + goodGraspsTaughtTotal, "grasp")}. ");
            if (attempts > 0 || firstRoundItems > 0)
            {
                text.Append($"On its own turn, it sorted {firstRoundItemsSorted} of {Plural(firstRoundItems, "item")}, " +
                            $"and {successes} of its {Plural(attempts, "grab")} worked. ");
            }
            text.Append($"It has mastered {CountMastered()} of {ItemCount} materials after {Plural(lessonsGiven, "lesson")}.");
            if (HasMenu)
            {
                text.Append(Certified ? " Keep improving to raise the bar, or head back to the menu."
                                      : " Keep teaching to help it improve, or head back to the menu.");
            }
            return text.ToString();
        }

        private static string Plural(int count, string word) => count + " " + word + (count == 1 ? "" : "s");

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
