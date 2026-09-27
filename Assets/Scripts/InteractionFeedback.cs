using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortQuest
{
    /// <summary>Presentation-only event listeners. No physics, scoring, or XR input changes.</summary>
    public sealed class InteractionFeedback : MonoBehaviour
    {
        [SerializeField, Range(0, 1)] private float volume = .35f;
        private readonly HashSet<TrashItem> items = new HashSet<TrashItem>();
        private TrashSpawner[] spawners;
        private AudioSource[] voices;
        private AudioClip[] clips;
        private Material panelMaterial;
        private int voice;
        private float lastGrab = -1, lastRelease = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            SceneManager.sceneLoaded -= Install;
            SceneManager.sceneLoaded += Install;
        }

        private static void Install(Scene scene, LoadSceneMode mode)
        {
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<InteractionFeedback>(true) != null) return;
            bool hasGame = false;
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<TrashSpawner>(true) != null) hasGame = true;
            if (!hasGame) return;
            var go = new GameObject("Signs and interaction audio");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.AddComponent<InteractionFeedback>();
        }

        private void Start()
        {
            panelMaterial = VizUtil.FallbackMaterial();
            foreach (var bin in FindObjectsByType<SortingBin>(FindObjectsSortMode.None))
            {
                if (bin.gameObject.scene != gameObject.scene) continue;
                foreach (var text in bin.GetComponentsInChildren<TMP_Text>())
                    PlaceBinLabel(bin, text);
            }
            // Item names live on the guide board to the player's left, not above each item on the belt.
            var guide = new GameObject("Trash guide");
            guide.transform.SetParent(transform, false);
            guide.AddComponent<TrashGuide>();
            foreach (var held in FindObjectsByType<HeldItemLabel>(FindObjectsSortMode.None))
                if (held.gameObject.scene == gameObject.scene)
                    ReadableSign.Apply(held.GetComponent<TMP_Text>(), new Vector2(.4f, .105f), .55f, new Color(.36f, .68f, .7f), panelMaterial);

            voices = new AudioSource[8];
            for (int i = 0; i < voices.Length; i++)
            {
                var child = new GameObject("Feedback voice " + i);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = .85f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = .4f;
                source.maxDistance = 7f;
                source.dopplerLevel = 0;
                voices[i] = source;
            }
            // Short mono cues are synthesized once, shared by all items and never generated per frame.
            clips = new[] { Tone("Pickup", .08f, 520, 780), Tone("Release", .065f, 410, 260),
                Tone("Correct sort", .3f, 660, 990), Tone("Wrong sort", .22f, 230, 170),
                Tone("Robot pickup", .12f, 300, 580), Tone("Missed item", .16f, 260, 190) };
            spawners = FindObjectsByType<TrashSpawner>(FindObjectsSortMode.None);
            foreach (var spawner in spawners)
                if (spawner.gameObject.scene == gameObject.scene) spawner.ItemSpawned += Track;
            foreach (var item in FindObjectsByType<TrashItem>(FindObjectsSortMode.None))
                if (item.gameObject.scene == gameObject.scene) Track(item);
        }

        private void Track(TrashItem item)
        {
            items.RemoveWhere(existing => existing == null);
            if (!items.Add(item)) return;
            item.Grabbed += Grab;
            item.Released += Release;
            item.Sorted += Sort;
            item.PickedByRobot += Robot;
            item.Missed += Miss;
        }

        /// <summary>
        /// Prints the bin's sign on the sloped label along the bin's top front edge (scene art), like a real label:
        /// no billboarding and no floating card. The label is opaque, so the text can't be hidden behind it.
        /// </summary>
        private static void PlaceBinLabel(SortingBin bin, TMP_Text text)
        {
            var billboard = text.GetComponent<Billboard>();
            if (billboard != null) Dispose(billboard);
            Transform label = text.transform;
            label.SetParent(bin.transform, false);
            label.localPosition = TrashGuideLayout.BinLabelCenter + TrashGuideLayout.BinLabelNormal * TrashGuideLayout.BinLabelTextLift;
            label.localRotation = TrashGuideLayout.BinLabelRotation;
            text.rectTransform.sizeDelta = TrashGuideLayout.BinLabelSize - new Vector2(.05f, .035f);
            text.color = Color.white;
            text.fontStyle = FontStyles.Bold;
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMax = .42f;
            text.fontSizeMin = .2f;
            text.margin = Vector4.zero;
            text.text = TrashTypes.BinSignText(bin.BinType);
        }

        private void Grab(TrashItem item)
        {
            if (Time.unscaledTime - lastGrab < .06f) return;
            lastGrab = Time.unscaledTime;
            Play(0, item.transform.position, .65f, 1 + (int)item.ItemType * .035f);
        }
        private void Release(TrashItem item)
        {
            if (Time.unscaledTime - lastRelease < .06f) return;
            lastRelease = Time.unscaledTime;
            Play(1, item.transform.position, .35f);
        }
        private void Sort(TrashItem item, SortingBin bin, bool correct) => Play(correct ? 2 : 3, bin.transform.position, .85f);
        private void Robot(TrashItem item) => Play(4, item.transform.position, .5f);
        private void Miss(TrashItem item) => Play(5, item.transform.position, .35f);

        private void Play(int cue, Vector3 position, float gain, float pitch = 1)
        {
            if (voices == null || volume <= 0) return;
            var source = voices[voice++ % voices.Length];
            source.Stop();
            source.transform.position = position;
            source.clip = clips[cue];
            source.volume = volume * gain;
            source.pitch = pitch;
            source.Play();
        }

        public static AudioClip Tone(string name, float duration, float from, float to)
        {
            const int rate = 22050;
            var samples = new float[Mathf.CeilToInt(rate * duration)];
            float phase = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float t = (float)i / (samples.Length - 1);
                phase += 2 * Mathf.PI * Mathf.Lerp(from, to, t) / rate;
                float envelope = Mathf.Min(1, t * 20) * Mathf.Pow(1 - t, 2);
                samples[i] = .55f * envelope * (Mathf.Sin(phase) + .15f * Mathf.Sin(phase * 2));
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void Dispose(Object value)
        {
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void OnDestroy()
        {
            if (spawners != null) foreach (var spawner in spawners) if (spawner != null) spawner.ItemSpawned -= Track;
            foreach (var item in items)
            {
                if (item == null) continue;
                item.Grabbed -= Grab; item.Released -= Release; item.Sorted -= Sort;
                item.PickedByRobot -= Robot; item.Missed -= Miss;
            }
            if (clips != null) foreach (var clip in clips) if (clip != null) Dispose(clip);
            if (panelMaterial != null) Dispose(panelMaterial);
        }
    }
}
