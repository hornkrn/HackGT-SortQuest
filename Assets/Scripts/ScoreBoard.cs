using System;
using TMPro;
using UnityEngine;

namespace SortQuest
{
    /// <summary>
    /// Keeps the running score and shows it on an optional TextMeshPro label.
    /// </summary>
    public class ScoreBoard : MonoBehaviour
    {
        [SerializeField] private int pointsCorrect = 10;
        [SerializeField] private int pointsWrong = -5;
        [SerializeField] private int pointsMissed = 0;

        [Tooltip("Optional world-space text that shows the score.")]
        [SerializeField] private TMP_Text scoreText;

        public event Action<ScoreBoard> Changed;

        public int Score { get; private set; }
        public int Correct { get; private set; }
        public int Wrong { get; private set; }
        public int Missed { get; private set; }

        private void Start()
        {
            Refresh();
        }

        public void RegisterSort(TrashItem item, SortingBin bin, bool correct)
        {
            if (correct)
            {
                Correct++;
                Score += pointsCorrect;
            }
            else
            {
                Wrong++;
                Score += pointsWrong;
            }
            Debug.Log($"[SortQuest] {item.ItemType} -> {bin.BinType} bin: {(correct ? "correct" : "wrong")}" +
                      $" (dropped: {item.WasDropped}, held {item.HoldSeconds:F1}s). Score {Score}");
            Refresh();
        }

        public void RegisterMiss(TrashItem item)
        {
            Missed++;
            Score += pointsMissed;
            Debug.Log($"[SortQuest] {item.ItemType} missed. Score {Score}");
            Refresh();
        }

        public void ResetScore()
        {
            Score = 0;
            Correct = 0;
            Wrong = 0;
            Missed = 0;
            Refresh();
        }

        private void Refresh()
        {
            if (scoreText != null)
            {
                scoreText.text = $"Score: {Score}\nCorrect: {Correct}   Wrong: {Wrong}   Missed: {Missed}";
            }
            Changed?.Invoke(this);
        }
    }
}
