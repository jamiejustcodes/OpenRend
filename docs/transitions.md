# Default Dissolve and crossfades

Open the Transitions tab and select Dissolve > Default. A click selects the preset without editing the project. Choose Apply, double-click the preset, or drag it to a video overlap. Two selected video events target their own pair, and a single selected event can affect existing adjacent overlaps.

The star beside Dissolve adds or removes it from Favorites. The choice survives relaunch and is stored in `transitions.json` under the application data directory, or `OPENREND_DATA_DIR`. Search combines with the selected category and matches the supported plug-in, preset and category names. These browser choices do not edit the project.

## Create an overlap

With one video event selected and no existing fades, applying Default can move it earlier to overlap the nearest preceding event. Options > Preferences > Editing sets the overlap amount. A small gap reduces the resulting overlap by that gap's length.

With Ripple off, source and crop offsets move with the event's left edge, so source content already on the timeline retains its timing. This requires sufficient source handles. With Ripple on, source offsets stay unchanged and later events on the same track move by the same amount. Events on other tracks stay in place. The complete operation is one undo step.

Dragging Default to an existing cut instead extends the events' source handles. Before cut, Centered on cut and After cut determine where the overlap lies. These extensions retain the source timing of existing timeline content.

## Add a transition at a clip edge

Drag a preset to the start or end of a video event, or onto a fade already at that edge. A new fade uses the Editing overlap length, limited by the available frames. An existing fade keeps its length. The effect reveals the lower tracks as the event enters or leaves, and the event stays in place.

Use the properties button on either transition strip to edit that edge. Hold Shift while dropping the preset to apply it without opening the properties window. Fade-in transitions support the Transition Progress editor. Fade-out transitions advance over their full duration.

## Remove the plug-in

Click the properties icon on the overlap strip, or choose Transition Properties from the incoming event's options menu. The strip shows the effect name and its tooltip includes the preset. Locked events disable the properties button. Remove Selected Plug-In clears the effect and leaves an ordinary crossfade. Undo and Redo are available in the properties window.

## Animate transition progress

Choose **Transition Progress** in the transition properties window or under **Insert > Video Envelopes**. The incoming event's **Insert/Remove Envelope** menu also opens the editor. Ordinary crossfades and transition effects both support progress curves.

The curve starts at 0% and ends at 100%. Add points at whole-frame offsets inside the overlap, drag their handles, or enter an offset and a progress percentage in the fields. Each point sets the shape of the following segment. Hold keeps its value until the next point. Decreasing the progress reverses the effect. Click the graph to seek within the overlap. Release applies a drag in one edit. Escape cancels it.

The timeline shows the saved curve on the incoming fade. Shortening the overlap keeps keys beyond its new end available if the overlap is extended again. Reset Curve restores regular progress. Splits share the original fade coordinates, and editing a split transition updates the pieces that still share its fade.

## Convert an overlap to a cut

Focus the timeline, activate the video track and place the cursor inside a complete two-event crossfade. Press Ctrl + numeric keypad /. The saved Editing alignment chooses the cut: Before cut uses the overlap end, After cut uses its start, and Centered on cut uses its midpoint. The overlap amount preference does not set the cut position.

Conversion trims the outgoing tail and incoming head, advances the incoming source/crop offsets, and removes the two overlap fades and their plug-ins. Outer event bounds, independent outer fades, later events and other tracks retain their timing. The complete edit is one undo step; it also works after removing the Dissolve plug-in.

For an odd overlap, Centered uses the earlier whole project frame. Split envelopes, nested or three-way overlaps, and locked events or tracks are outside this command's supported cases.

## Video source looping

For a finite video event, open its options menu and choose Switches > Loop. Checked repeats the source when an event extends beyond it. Unchecked holds the last decoded frame. Linked audio has its own Loop switch, which chooses between repetition and silence beyond source end.

Older events keep bounded playback until you explicitly select a source mode. Loop phase is stored in integer project frames and reduced against the probed source duration at evaluation time. Turning Loop off preserves its starting source position when it can be represented in whole project frames; otherwise the command reports the fractional-offset limitation without editing. Crop animation keeps its event-relative timing.

## Rendering and preservation

Preview and export use the same paired interpolation of source color and alpha coverage. Event opacity contributes to each participant's weight; track opacity is applied once to the resulting layer. Transparent pixels, alpha effects, letterboxing and crop boundaries reveal lower tracks. The mixture currently runs on the CPU before final CPU/D3D composition.

Project save/reopen, undo/redo, split envelopes and tested clipboard rate conversions preserve transition decisions. Unsplit crossfade lengths follow later moves and trims, including after removing the plug-in. Split pieces retain their original envelope.

## Video fade curves and Fade Type crossfades

The timeline model stores Linear, Fast, Slow, Smooth and Sharp video fade-in/out curves alongside reciprocal curves (`FadeInReciprocalCurve` and `FadeOutReciprocalCurve`). Isolated fades use these shapes in the shared preview/export evaluator. Fade-out complements the shape output ($1 - \text{Evaluate}(\text{curve}, progress)$), and split pieces retain their original envelope position.

In a plain crossfade (without a transition plug-in), the incoming event's `FadeInCurve` controls the incoming event weight, while the incoming event's `FadeInReciprocalCurve` controls the outgoing event weight ($1 - \text{Evaluate}(\text{FadeInReciprocalCurve}, progress)$). The outgoing event's fade-out curve is inert during the crossfade.

Timeline video events provide context menu controls:
- **Fade In Curve** / **Fade Out Curve**: submenus with Linear, Fast, Slow, Smooth and Sharp items.
- **Fade Type**: a 5×5 thumbnail grid of 25 buttons matching Vegas Pro layout:
  - Columns represent the incoming curve (Linear, Fast, Slow, Smooth, Sharp).
  - Rows represent the outgoing reciprocal curve (Linear, Fast, Slow, Smooth, Sharp).
  - Each button displays a vector preview with falling blue outgoing and rising cyan incoming curves, and the active curve pair is highlighted with a gold outline.
  - Selecting any grid cell clears the transition plug-in and sets the reciprocal crossfade curve pair.

Projects save as version 10 and read versions 1–10. Older projects without saved curve settings use linear fades. Audio fades use linear interpolation.

## Transition catalog and spatial reveals

The Transitions tab groups 18 entries by effect type. All Plug-ins shows the full catalog. Favorites shows the entries marked with the star and saves that list in `transitions.json`.

- **Fades** includes Dissolve and Flash.
- **Wipes** includes Linear Wipe, Clock Wipe, Venetian Blinds, and Star Wipe.
- **Reveals** includes Barn Door, Iris, and Split.
- **Transforms** includes Push, Slide, Zoom, Swap, Squeeze, and Cross Effect.
- **3D and Peels** includes Page Loop, Page Peel, and Page Roll.

Choose an entry to see its presets. Each thumbnail shows the midpoint of that preset. Hover to play the transition between two sample patterns when Windows animations are enabled. Use Apply, press Enter, or double-click a preset to apply it to selected events. Drag a preset to a cut or existing overlap to place it directly.

### Transition Properties Window

Clicking the transition strip on an overlap, or choosing **Transition Properties...** from the clip context menu, opens the dedicated properties dialog:
- **Preset dropdown**: Select from factory presets or custom configurations.
- **Dynamic Parameter Controls**:
  - Angle slider (0° to 360°) and Feather softness slider (0.0 to 1.0) for Wipes.
  - Direction and Orientation selectors (Left/Right/Up/Down, In/Out) for Reveals and Transforms.
  - Border Size and Border Color pickers for bordered reveals and wipes.
  - Push Off checkbox for Push transitions.
- **Remove Selected Plug-In**: Reverts the transition to a canonical crossfade while preserving overlap timing and fades.
- **Undo / Redo**: Immediate history navigation with real-time preview updates.

Dragging a parameter slider previews the setting and commits it in one edit on release. Escape restores the previous setting and preview. The arrow keys adjust the focused slider by one displayed unit.

Squeeze rescales both pictures into regions that change size over the overlap. Choose an edge or center origin. Clear **Squeeze previous image** to keep the outgoing picture at its original scale. The border controls set the width, feather, and color at the moving seam.

Swap moves the outgoing picture in the chosen direction and brings the incoming picture to the foreground. Both pictures use the border settings, and the area between them reveals lower tracks.

Cross Effect gradually zooms, pixelates, or blurs either shot or both. The effect is strongest at the midpoint. Fade Range determines how much of the overlap is used to mix the two pictures. A range of 1 uses the full overlap, and 0 switches images at the midpoint. Zoom centers, maximum block dimensions and the blur maximum are editable in the properties window.

Page Loop moves the incoming picture over the outgoing picture. Loop Radius and Loop Position control the bend in the moving picture. Page Peel and Page Roll bend the outgoing picture to reveal the next one. Slide Amount lets the opposite edge move with the fold. All three offer angle, folded-edge opacity, perspective, and reflected light color. A radius of zero produces a flat edge. Existing corner presets supply the starting angle.

## Current limits

The page effects use an analytic projection with bilinear picture sampling and palette-based light colors. Cross Effect uses a box blur and two-dimensional zoom. Several parameters in the other effects remain unimplemented.

Legacy bounded events require source handles. Video Loop or Hold and audio Loop or silence padding permit extensions. The Editing preference sets the Loop default for new finite events. Same-start overlaps at project zero and exact-gap displacement remain limited. OpenRend initializes the cut overlap to 0.25 seconds, centered. Source-duration probing and arbitrary VFR behavior remain limited.
