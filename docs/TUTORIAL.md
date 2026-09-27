# Guided tutorial

Start from the VR menu, or use **SortQuest > Tutorial > Start narrated tutorial** in
Unity with SampleScene open. The Editor command bypasses the grabbable START block;
it does not simulate hands or complete the steps for you.

GameManager adds GuidedTutorial automatically. No scene edits are required. To tune
it, select GameManager outside Play mode, add the GuidedTutorial component, and set
Stopping Progress (default 0.46), Fade Seconds (0.25), and Release Grace Seconds
(0.45). References are connected by GameManager at runtime.

The welcome finishes before an item arrives. Each of the six types moves a short
way down the belt, stops near its middle, and gets an instruction plus a pulsing
outline around the correct bin. Early outcomes are remembered while the current
voice finishes. Incorrect bins, missed items, or drops onto scenery repeat only
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
- Sort early: the next prompt waits for the current narration to finish.
- Drop an item or use a wrong bin: see black, then retry the same item.
- Fail a later step: earlier completed types must not restart.
- Sort all six: hear completion, then see normal spawning and a moving conveyor.
- Exit/re-enter Play mode and repeat; inspect both eyes on Quest for complete fades.

Current verification: Unity imported and compiled the implementation; the final
runtime and Editor sources also passed Unity’s C# compiler using its generated
references, with output directed to /tmp. All nine MP3s match the original ElevenLabs
downloads byte for byte and have readable durations. The
playback attempt was blocked in Meta XR Simulator session teardown before the
tutorial began. End-to-end interaction, audible playback, and Quest rendering are
not yet verified. Skip and voice interruption are intentionally deferred.
