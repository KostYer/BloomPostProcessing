# Changelog

## 1.0.0-rc1 — Unreleased

- Scene Volume Profiles control bloom/grading through a custom override and tonemapping through Unity's standard override; renderer features default to Scene Volumes, with explicit legacy Renderer Asset mode and a scene-profile migration button.

- Six independent Rendering Layer bloom groups with first-active-group ownership.
- Unity-style Gaussian bloom with per-group threshold, intensity, tint, clamp, scatter, iterations, downscale and quality.
- Optional soft knee, radius, and NaN/Infinity protection; collapsible additions and full-group reset with Undo.
- Transparent source capture and supported uGUI source capture/masking.
- Fixed transparent Simple Lit HDR emission capture: exclude G-buffer passes so albedo cannot overwrite the forward-lit source. Added camera/source RGB regression checks.
- Optional HDR color grading and Unity Neutral/ACES tonemapping integration.
- Editor setup diagnostics, including per-camera warnings for simultaneous Unity Bloom.
- Separate runtime/editor/sample assemblies and portable verification tools.
- Explicit release export with a primitive-based Getting Started demo and offline documentation.

Release candidate only. Profiling and publisher submission review remain outstanding.
