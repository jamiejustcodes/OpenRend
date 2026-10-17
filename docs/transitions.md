# Default Dissolve and crossfades

Open the Transitions tab and select Dissolve > Default. A click selects the preset without editing the project. Use its + button, double-click it, or drag it to a video overlap. Two selected video events target their own pair; a single selected event can affect existing adjacent overlaps.

## Create an overlap

With one video event selected and no existing fades, applying Default can move it earlier to overlap the nearest preceding event. Options > Preferences > Editing sets the overlap amount. A small gap reduces the resulting overlap by that gap's length.

With Ripple off, source and crop offsets move with the event's left edge, so source content already on the timeline retains its timing. This requires sufficient source handles. With Ripple on, source offsets stay unchanged and later events on the same track move by the same amount. Events on other tracks stay in place. The complete operation is one undo step.

Dragging Default to an existing cut instead extends the events' source handles. Before cut, Centered on cut and After cut determine where the overlap lies. These extensions retain the source timing of existing timeline content.

## Remove the plug-in

Open the incoming event's options menu and choose Transition Properties. Remove Selected Plug-In clears the preset while preserving the overlap and fades. Undo and Redo are available in that window. The result remains an ordinary crossfade; removal does not convert it to a cut.

## Rendering and preservation

Preview and export use the same paired interpolation of source color and alpha coverage. Event opacity contributes to each participant's weight; track opacity is applied once to the resulting layer. Transparent pixels, alpha effects, letterboxing and crop boundaries reveal lower tracks. The mixture currently runs on the CPU before final CPU/D3D composition.

Project save/reopen, undo/redo, split envelopes and tested clipboard rate conversions preserve transition decisions. Unsplit crossfade lengths follow later moves and trims, including after removing the plug-in. Split pieces retain their original envelope.

## Current limits

Only Default Dissolve is supported. The broader transition catalog, favorites, progress curves and full browser/properties layout remain pending. The current properties window exposes plug-in removal and history controls.

Automatic placement rejects missing source handles rather than wrapping the source. Same-start overlaps at project zero, event-level locks and exact-gap displacement remain limited. Initial Editing preferences use 0.25 seconds centered; factory-default matching has not been established. Exact reference-editor pixel math, color management and professional-scale throughput remain unverified.
