# 3D LUT effects

Add LUT Filter to a video event's effect chain, then Browse to import a 3D `.cube` file. Strength blends the mapped result with the incoming RGB values. Interpolation offers Trilinear and Tetrahedral. Clear removes the imported lattice; undo restores it. Bypass uses the event effect's enable control.

The validated lattice is embedded in version 6 projects. Reopening does not depend on the original LUT file. Split, clipboard and effect copies use independent data. Importing and processing do not modify the LUT file or source media.

Supported files use a red-fast 3D lattice of size 2–65, optional TITLE and DOMAIN_MIN/DOMAIN_MAX headers, and exactly the required number of finite RGB samples. Files are limited to 32 MiB. One-dimensional/combined LUTs and unsupported directives are rejected. Project manifests remain limited to 64 MiB; an oversized save preserves the existing destination.

The effect processes BGRA8 RGB values at its position in the ordered event chain, before Pan/Crop and composition. Alpha is preserved. HDR/log color management, complete grading wheels/curves and separate input/output grading slots are pending. This is OpenRend's LUT processing; native processing equivalence with other editors is not established.

Tests cover identity/channel mapping, all six tetrahedral regions, domains, strength/bypass, alpha/source-buffer preservation, independent clones and embedded persistence. CPU/D3D/encoded checks include lower-track composition. Desktop integration imports, clears, undoes, edits and saves a LUT, then reopens after the imported file is removed.
