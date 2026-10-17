# Default Dissolve and crossfades

Open the Transitions tab and select Dissolve > Default. A click selects the preset without editing the project. Use its + button, double-click it, or drag it to a video overlap. Two selected video events target their own pair; a single selected event can affect existing adjacent overlaps.

The star beside Dissolve adds or removes it from Favorites. The choice survives relaunch and is stored in `transitions.json` under the application data directory, or `OPENREND_DATA_DIR`. Search combines with the selected category and matches the supported plug-in, preset and category names. These browser choices do not edit the project.

## Create an overlap

With one video event selected and no existing fades, applying Default can move it earlier to overlap the nearest preceding event. Options > Preferences > Editing sets the overlap amount. A small gap reduces the resulting overlap by that gap's length.

With Ripple off, source and crop offsets move with the event's left edge, so source content already on the timeline retains its timing. This requires sufficient source handles. With Ripple on, source offsets stay unchanged and later events on the same track move by the same amount. Events on other tracks stay in place. The complete operation is one undo step.

Dragging Default to an existing cut instead extends the events' source handles. Before cut, Centered on cut and After cut determine where the overlap lies. These extensions retain the source timing of existing timeline content.

## Remove the plug-in

Click the properties icon on the overlap's Dissolve strip, or open the incoming event's options menu and choose Transition Properties. Remove Selected Plug-In clears the preset while preserving the overlap and fades. Undo and Redo are available in that window. The result remains an ordinary crossfade; removal does not convert it to a cut.

## Convert an overlap to a cut

Focus the timeline, activate the video track and place the cursor inside a complete two-event crossfade. Press Ctrl + numeric keypad /. The saved Editing alignment chooses the cut: Before cut uses the overlap end, After cut uses its start, and Centered on cut uses its midpoint. The overlap amount preference does not set the cut position.

Conversion trims the outgoing tail and incoming head, advances the incoming source/crop offsets, and removes the two overlap fades and their plug-ins. Outer event bounds, independent outer fades, later events and other tracks retain their timing. The complete edit is one undo step; it also works after removing the Dissolve plug-in.

Centered conversion requires an even number of overlap frames. An odd overlap would require a half-frame cut, so OpenRend reports the limitation and leaves the project unchanged. Split envelopes, nested/contained or three-way overlaps and locked tracks are unsupported for this command.

## Video source looping

For a finite video event, open its options menu and choose Switches > Loop. Checked repeats the source when an event extends beyond it; unchecked explicitly uses its last decoded frame. Edge trimming and automatic overlap placement use the selected source mode. This setting affects video only; linked audio keeps its existing source bounds.

Older events keep bounded playback until you explicitly select a source mode. Loop phase is stored in integer project frames and reduced against the probed source duration at evaluation time. Turning Loop off preserves its starting source position when it can be represented in whole project frames; otherwise the command reports the fractional-offset limitation without editing. Crop animation keeps its event-relative timing.

## Rendering and preservation

Preview and export use the same paired interpolation of source color and alpha coverage. Event opacity contributes to each participant's weight; track opacity is applied once to the resulting layer. Transparent pixels, alpha effects, letterboxing and crop boundaries reveal lower tracks. The mixture currently runs on the CPU before final CPU/D3D composition.

Project save/reopen, undo/redo, split envelopes and tested clipboard rate conversions preserve transition decisions. Unsplit crossfade lengths follow later moves and trims, including after removing the plug-in. Split pieces retain their original envelope.

## Current limits

Only Default Dissolve is supported. The broader transition catalog, progress curves and full browser/properties layout remain pending. The current properties window exposes plug-in removal and history controls.

Legacy bounded events reject missing source handles. Explicit video Loop/Hold modes permit source extensions; audio looping and default-loop preferences are not implemented. Same-start overlaps at project zero, event-level locks and exact-gap displacement remain limited. Initial Editing preferences use 0.25 seconds centered; factory-default matching has not been established. Source-duration probing and arbitrary VFR/source-rate equivalence remain limited. Exact reference-editor pixel math, color management and professional-scale throughput remain unverified.
