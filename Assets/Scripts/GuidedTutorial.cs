using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortQuest
{
    /// <summary>
    /// Narrated, one-item-at-a-time onboarding. The player must sort every item into its highlighted bin;
    /// a mistake fades to black and replays only the current item.
    /// </summary>
    public class GuidedTutorial : MonoBehaviour
    {
        private enum AttemptResult { Waiting, Correct, Retry }

        private static readonly ItemType[] TutorialItems =
        {
            ItemType.AluminumCan,
            ItemType.PlasticBottle,
            ItemType.CardboardBox,
            ItemType.CrumpledPaper,
            ItemType.BatteryAA,
            ItemType.PowerBank
        };

        private GameManager game;
        private TrashSpawner spawner;
        private AudioSource voice;
        private Camera fadeCamera;
        private Transform fadeQuad;
        private MeshRenderer fadeRenderer;
        private Material fadeMaterial;
        private readonly List<SortingBin> bins = new List<SortingBin>();
        private TrashItem currentItem;
        private SortingBin highlightedBin;
        private AttemptResult result;
        private bool originalBeltRunning;
        private bool active;

        [SerializeField, Range(0.1f, 0.8f)] private float stoppingProgress = 0.46f;
        [SerializeField, Min(0.1f)] private float fadeSeconds = 0.25f;
        private Coroutine releaseCheck;

        [SerializeField, Min(0.1f)] private float releaseGraceSeconds = 0.45f;

        public void Initialize(GameManager manager, TrashSpawner itemSpawner)
        {
            game = manager;
            spawner = itemSpawner;
        }

        public void Begin()
        {
            Cancel();
            if (spawner == null || spawner.Belt == null)
            {
                Debug.LogError("[SortQuest] Guided tutorial needs a TrashSpawner with a configured conveyor.", this);
                game?.SetTutorialStatus("Tutorial setup is incomplete. Ask a team member for help.");
                return;
            }

            active = true;
            originalBeltRunning = spawner.Belt.Running;
            spawner.Belt.Running = false;
            spawner.Spawning = false;
            spawner.ClearItems();
            bins.Clear();
            foreach (SortingBin bin in FindObjectsByType<SortingBin>(FindObjectsSortMode.None))
            {
                if (bin.gameObject.scene == gameObject.scene) bins.Add(bin);
            }
            EnsureAudioSource();
            EnsureFadeCanvas();
            StartCoroutine(RunTutorial());
        }

        private IEnumerator RunTutorial()
        {
            game.SetTutorialStatus("Welcome! Listen to the guide before your first item arrives.");
            yield return Speak("welcome");
            for (int i = 0; i < TutorialItems.Length; i++)
            {
                ItemType type = TutorialItems[i];
                SortingBin target = FindBin(TrashTypes.CorrectBin(type));
                if (target == null)
                {
                    game.SetTutorialStatus("Missing tutorial bin. Check the scene setup.");
                    Debug.LogError($"[SortQuest] Missing tutorial bin for {type}.", this);
                    yield break;
                }
                SetHighlightedBin(target);
                bool retry = false;
                do
                {
                    if (!SpawnAndTrackItem(type, retry ? stoppingProgress : Mathf.Max(0f, stoppingProgress - 0.12f)))
                    {
                        yield return Fade(1f, 0f, fadeSeconds);
                        game.SetTutorialStatus($"Missing tutorial prefab: {type}. Check TrashSpawner.");
                        yield break;
                    }
                    if (retry)
                    {
                        yield return Fade(1f, 0f, fadeSeconds);
                        yield return Speak("retry");
                    }
                    else
                    {
                        spawner.Belt.Running = true;
                        while (currentItem != null && currentItem.State == TrashItemState.OnBelt &&
                               spawner.Belt.Progress(currentItem.Body.position) < stoppingProgress)
                            yield return null;
                        spawner.Belt.Running = false;
                    }
                    game.SetTutorialStatus($"ITEM {i + 1} OF {TutorialItems.Length}\n{TrashTypes.DisplayName(type)} → {target.BinType}\nGrab it and place it in the glowing bin.");
                    // Outcomes are already subscribed: an early sort is remembered, but the voice
                    // finishes before moving to the next step. No overlapping prompts.
                    yield return Speak("items/" + TrashTypes.ItemId(type));
                    while (result == AttemptResult.Waiting)
                    {
                        if (currentItem == null) result = AttemptResult.Retry;
                        yield return null;
                    }
                    retry = result != AttemptResult.Correct;
                    if (retry)
                    {
                        game.SetTutorialStatus("Try this item again. Your completed steps are saved.");
                        yield return Fade(0f, 1f, fadeSeconds);
                    }
                    else
                    {
                        game.SetTutorialStatus("Correct! Watch for the next item.");
                        yield return new WaitForSecondsRealtime(0.8f);
                    }
                    if (retry && currentItem != null) currentItem.MarkTutorialMiss();
                    // Reset only after the screen is black on failure.
                    UntrackCurrentItem();
                    spawner.ClearItems();
                    yield return null;
                } while (retry);
            }
            SetHighlightedBin(null);
            game.SetTutorialStatus("Tutorial complete! Your main round starts next.");
            yield return Speak("complete");
            yield return Fade(0f, 1f, fadeSeconds);
            // Start the normal conveyor even if an earlier editor session left it stopped.
            spawner.Belt.Running = true;
            yield return Fade(1f, 0f, fadeSeconds);
            active = false;
            game.CompleteTutorial();
        }

        private bool SpawnAndTrackItem(ItemType type, float progress)
        {
            spawner.ClearItems();
            currentItem = spawner.SpawnTutorialItem(type, progress);
            if (currentItem == null) return false;

            currentItem.TutorialOwned = true;
            currentItem.Sorted += HandleSorted;
            currentItem.Released += HandleReleased;
            currentItem.Grabbed += HandleGrabbed;
            currentItem.Missed += HandleMissed;
            result = AttemptResult.Waiting;
            return true;
        }

        private IEnumerator Speak(string clipName)
        {
            AudioClip clip = LoadClip(clipName);
            if (clip == null)
            {
                Debug.LogWarning($"[SortQuest] Missing tutorial voice clip: {clipName}.", this);
                yield return new WaitForSecondsRealtime(0.8f);
                yield break;
            }
            voice.Stop();
            clip.LoadAudioData();
            float deadline = Time.realtimeSinceStartup + 5f;
            while (clip.loadState == AudioDataLoadState.Loading && Time.realtimeSinceStartup < deadline)
                yield return null;
            if (clip.loadState != AudioDataLoadState.Loaded)
            {
                Debug.LogError($"[SortQuest] Could not load tutorial audio: {clipName}.", this);
                yield break;
            }
            voice.clip = clip;
            voice.Play();
            Debug.Log($"[SortQuest] Tutorial narration: {clipName} ({clip.length:F1}s)", this);
            yield return new WaitForSecondsRealtime(clip.length);
        }

        private static AudioClip LoadClip(string clipName)
        {
            return Resources.Load<AudioClip>("TutorialNarration/" + clipName);
        }

        private void HandleSorted(TrashItem item, SortingBin bin, bool correct)
        {
            if (item != currentItem) return;
            if (result == AttemptResult.Waiting)
                result = correct && !item.WasDropped ? AttemptResult.Correct : AttemptResult.Retry;
        }

        private void HandleMissed(TrashItem item)
        {
            if (item == currentItem && result == AttemptResult.Waiting) result = AttemptResult.Retry;
        }

        private void HandleGrabbed(TrashItem item)
        {
            if (item == currentItem && releaseCheck != null)
            {
                StopCoroutine(releaseCheck);
                releaseCheck = null;
            }
        }

        private void HandleReleased(TrashItem item)
        {
            if (item != currentItem) return;
            if (releaseCheck != null) StopCoroutine(releaseCheck);
            releaseCheck = StartCoroutine(CheckReleasedItem(item));
        }

        private IEnumerator CheckReleasedItem(TrashItem item)
        {
            yield return new WaitForSeconds(releaseGraceSeconds);
            // Wait until the drop actually contacts the belt/floor. This avoids failing a valid throw
            // while it is still flying toward the target. TrashItem marks WasDropped on that first
            // outside-bin contact; bin entry sorts immediately, and falling below the floor emits Missed.
            while (item == currentItem && result == AttemptResult.Waiting && item != null &&
                   item.State != TrashItemState.Sorted && item.State != TrashItemState.Missed && !item.WasDropped)
            {
                yield return new WaitForSeconds(0.04f);
            }

            releaseCheck = null;
            if (item == currentItem && result == AttemptResult.Waiting && item != null &&
                item.State != TrashItemState.Sorted && item.WasDropped)
                result = AttemptResult.Retry;
        }

        private SortingBin FindBin(BinType type)
        {
            foreach (SortingBin bin in bins)
                if (bin != null && bin.BinType == type) return bin;
            return null;
        }

        private void SetHighlightedBin(SortingBin bin)
        {
            if (highlightedBin != null) highlightedBin.SetTutorialHighlighted(false);
            highlightedBin = bin;
            if (highlightedBin != null) highlightedBin.SetTutorialHighlighted(true);
        }

        private void UntrackCurrentItem()
        {
            if (currentItem != null)
            {
                currentItem.Sorted -= HandleSorted;
                currentItem.Released -= HandleReleased;
                currentItem.Grabbed -= HandleGrabbed;
                currentItem.Missed -= HandleMissed;
            }
            if (releaseCheck != null)
            {
                StopCoroutine(releaseCheck);
                releaseCheck = null;
            }
            if (currentItem != null)
            {
                currentItem.TutorialOwned = false;
                if (currentItem.State == TrashItemState.Sorted || currentItem.State == TrashItemState.Missed)
                    Destroy(currentItem.gameObject);
            }
            currentItem = null;
        }

        private void EnsureAudioSource()
        {
            if (voice == null)
            {
                var narrator = new GameObject("Tutorial narrator");
                narrator.transform.SetParent(transform, false);
                voice = narrator.AddComponent<AudioSource>();
            }
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            voice.volume = 1f;
            voice.mute = false;
            voice.priority = 0;
        }

        private void EnsureFadeCanvas()
        {
            if (fadeRenderer != null) return;
            fadeCamera = Camera.main;
            if (fadeCamera == null) return;

            var overlay = new GameObject("Tutorial full-view fade");
            fadeQuad = overlay.transform;
            fadeQuad.SetParent(fadeCamera.transform, false);
            fadeQuad.localRotation = Quaternion.identity;
            overlay.AddComponent<MeshFilter>().sharedMesh = VizUtil.QuadMesh;
            fadeRenderer = overlay.AddComponent<MeshRenderer>();
            fadeRenderer.shadowCastingMode = ShadowCastingMode.Off;
            fadeRenderer.receiveShadows = false;
            fadeRenderer.sortingOrder = short.MaxValue;
            Shader shader = Resources.Load<Shader>("TutorialOverlay");
            if (shader == null)
            {
                Debug.LogError("[SortQuest] Could not find the transparent sprite shader for the tutorial fade.", this);
                fadeRenderer.enabled = false;
                return;
            }
            fadeMaterial = new Material(shader);
            fadeMaterial.color = new Color(0f, 0f, 0f, 0f);
            fadeMaterial.renderQueue = (int)RenderQueue.Overlay;
            fadeRenderer.sharedMaterial = fadeMaterial;
            fadeRenderer.enabled = false;
            FitFadeQuadToBothEyes();
        }

        private IEnumerator Fade(float from, float to, float duration)
        {
            if (fadeRenderer == null) EnsureFadeCanvas();
            if (fadeRenderer == null || fadeMaterial == null)
            {
                yield return new WaitForSeconds(duration);
                yield break;
            }
            FitFadeQuadToBothEyes();
            fadeRenderer.enabled = true;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                Color color = fadeMaterial.color;
                color.a = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                fadeMaterial.color = color;
                yield return null;
            }
            Color finalColor = fadeMaterial.color;
            finalColor.a = to;
            fadeMaterial.color = finalColor;
            fadeRenderer.enabled = to > 0f;
        }

        private void FitFadeQuadToBothEyes()
        {
            if (fadeCamera == null || fadeQuad == null) return;
            float distance = fadeCamera.nearClipPlane + 0.03f;
            float halfWidth = 0f;
            float halfHeight = 0f;
            var corners = new Vector3[4];
            Camera.MonoOrStereoscopicEye[] eyes = fadeCamera.stereoEnabled
                ? new[] { Camera.MonoOrStereoscopicEye.Left, Camera.MonoOrStereoscopicEye.Right }
                : new[] { Camera.MonoOrStereoscopicEye.Mono };
            foreach (Camera.MonoOrStereoscopicEye eye in eyes)
            {
                fadeCamera.CalculateFrustumCorners(new Rect(0f, 0f, 1f, 1f), distance, eye, corners);
                for (int i = 0; i < corners.Length; i++)
                {
                    halfWidth = Mathf.Max(halfWidth, Mathf.Abs(corners[i].x));
                    halfHeight = Mathf.Max(halfHeight, Mathf.Abs(corners[i].y));
                }
            }

            // Some editor/simulator camera configurations return zero corners before XR has
            // initialized. Keep a usable full-view overlay in that case.
            if (halfWidth < 0.001f || halfHeight < 0.001f)
            {
                halfHeight = distance * Mathf.Tan(fadeCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                halfWidth = halfHeight * fadeCamera.aspect;
            }

            fadeQuad.localPosition = Vector3.forward * distance;
            // The small overscan prevents visible seams if the runtime changes its eye projection slightly.
            fadeQuad.localScale = new Vector3((halfWidth + 0.05f) * 2.12f, halfHeight * 2.12f, 1f);
        }

        public void Cancel()
        {
            StopAllCoroutines();
            UntrackCurrentItem();
            SetHighlightedBin(null);
            if (voice != null) voice.Stop();
            if (active && spawner != null && spawner.Belt != null)
                spawner.Belt.Running = originalBeltRunning;
            active = false;
            if (fadeRenderer != null) fadeRenderer.enabled = false;
        }

        private void OnDisable() => Cancel();

        private void OnDestroy()
        {
            if (fadeQuad != null) Destroy(fadeQuad.gameObject);
            if (fadeMaterial != null) Destroy(fadeMaterial);
        }
    }
}
