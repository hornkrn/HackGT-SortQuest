# Guided tutorial

After START, the VR menu asks whether you want a tutorial: grab YES to learn, or
NO to begin HumanRound immediately with a fresh score and running conveyor. Use
**SortQuest > Tutorial > Show Yes-No choice** to open that prompt in the Editor.
To bypass the choice for tutorial review, use **SortQuest > Tutorial > Start narrated tutorial** in
Unity with SampleScene open. The Editor command bypasses the grabbable START block;
it does not simulate hands or complete the steps for you.

GameManager adds GuidedTutorial automatically. No scene edits are required. To tune
it, select GameManager outside Play mode, add the GuidedTutorial component, and set
Stopping Progress (default 0.46), Fade Seconds (0.25), and Release Grace Seconds
(0.45). References are connected by GameManager at runtime.

The welcome finishes before an item arrives. Each of the six types moves a short
way down the belt, stops near its middle, and gets an instruction plus a pulsing
outline around the correct bin. Correct sorts and failures interrupt the item or retry narration on the next
frame and advance to success feedback or the retry fade. An outcome during arrival
or fade-in also prevents an outdated instruction from starting. Welcome and
completion narration still play in full. Incorrect bins, missed items, or drops onto scenery repeat only
that type after a black fade. A correct sort advances to the next type. Completion
plays the final narration and starts HumanRound with a running belt and fresh score.
Tutorial grasps use the ordinary recording pipeline; tutorial sorts do not add
points to the main round. Normal recording still requires valid tracked hand poses.

## Audio

Nine ElevenLabs Roger MP3 files are bundled under Assets/Resources/TutorialNarration,
so there is no API key or network dependency. The mapping retains the previous
backup's file assignments: welcome; aluminum_can; plastic_bottle; cardboard_box;
crumpled_paper; battery_aa; power_bank; retry; complete. The original downloads are
unchanged. Spoken wording/mapping still needs a listening pass.

The narrator has its own full-volume, non-spatial AudioSource. If it is silent,
check Unity Game view's Mute Audio toggle, macOS output volume/device, and the
active camera's enabled AudioListener. Console messages identify the clip playing
or an audio-loading failure. No generated system voice is used.

## Review checklist

- Hear the full welcome, then the instruction for the can.
- Check all six instructions match the visible items and bin colors.
- Sort early: the current prompt stops and the next item follows.
- Fail early: the current prompt stops and the retry fade begins.
- Select NO: no tutorial voice or items; the main round begins immediately.
- Select YES: welcome narration and all six guided steps begin.
- Drop an item or use a wrong bin: see black, then retry the same item.
- Fail a later step: earlier completed types must not restart.
- Sort all six: hear completion, then see normal spawning and a moving conveyor.
- Exit/re-enter Play mode and repeat; inspect both eyes on Quest for complete fades.

## Checkpoint and verification

The user confirmed the original narrated tutorial works. That complete tracked
project state is saved locally as annotated tag `tutorial-working-v1` (commit
`9647289`), including the Unity-generated font and XR settings present at the time.
The tag has not been pushed.

To revisit that checkpoint without deleting newer commits, close Unity, save or
stash any uncommitted changes, then run:

```sh
git switch -c restore-tutorial-v1 tutorial-working-v1
```

This creates a separate branch at the known working version. Use a different branch
name if `restore-tutorial-v1` already exists. Ignored files (Library, local saves,
.env) are not part of a Git checkpoint.

The subsequent Yes/No prompt and voice interruption changes have not been
play-tested. No scene wiring changes are needed; MainMenu creates the two choices
from its existing ChoiceBlock prefab. They use the existing grab-to-select interaction.
