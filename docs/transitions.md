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

Projects save as version 8 and read versions 1–8; older projects retain linear fades. Audio keeps its existing linear fades.

## Vegas Pro Transition Catalog and Spatial Reveals

Open the Transitions tab to view all 18 Vegas Pro OFX video transition plug-ins organized into categories:
- **All Plug-ins**: Complete collection of all 18 plug-ins.
- **Favorites**: User-selected favorite plug-ins (toggled with the ★ star icon, persisted in `transitions.json`).
- **Fades**: `Dissolve` (23 presets including Crossfade, Additive, Fade Through Color, Color Bleed), `Flash` (Hard/Soft Flash).
- **Wipes**: `Linear Wipe` (12 presets, angle/feather parameters), `Clock Wipe` (6 presets), `Venetian Blinds` (6 presets), `Star Wipe` (18 presets).
- **Reveals**: `Barn Door` (10 presets, horizontal/vertical, in/out, borders), `Iris` (14 presets, circle/square/diamond), `Split` (8 presets).
- **Transforms**: `Push` (12 presets, 4 directions, push off), `Slide` (10 presets), `Zoom` (14 presets), `Swap` (8 presets), `Squeeze` (14 presets), `Cross Effect` (5 presets).
- **3D and Peels**: `Page Loop` (4 presets), `Page Peel` (4 presets), `Page Roll` (4 presets).

Each plug-in lists its preset cards with vector thumbnails, preset name, `+` add button, and drag-and-drop support to cuts and existing overlaps.

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

## Current limits

All 18 Vegas Pro transition plug-in types, categories, and presets are modeled and evaluated with spatial masks, feathering, and border compositing. Highly complex non-linear 3D mesh deformation shaders (such as realistic 3D page curl specular physics) use smooth 2D gradient curl approximations in software rendering.

Legacy bounded events reject missing source handles. Explicit video Loop/Hold modes permit source extensions; audio looping and default-loop preferences are not implemented. Same-start overlaps at project zero, event-level locks and exact-gap displacement remain limited. Initial Editing preferences use 0.25 seconds centered; factory-default matching has not been established. Source-duration probing and arbitrary VFR/source-rate equivalence remain limited. Exact reference-editor pixel math, color management and professional-scale throughput remain unverified.
