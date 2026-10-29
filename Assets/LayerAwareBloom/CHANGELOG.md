# Changelog

## 1.0.0-rc1 — Unreleased

- Scene Volume Profiles control bloom/grading and tonemapping through separate custom overrides; renderer features default to Scene Volumes, with explicit legacy Renderer Asset mode and a scene-profile migration button.

- Six independent Rendering Layer bloom groups with first-active-group ownership.
- Unity-style Gaussian bloom with per-group threshold, intensity, tint, clamp, scatter, iterations, downscale and quality.
- Optional soft knee, radius, and NaN/Infinity protection; collapsible additions and full-group reset with Undo.
- Transparent source capture and supported uGUI source capture/masking.
- Optional HDR color grading and independent Neutral/ACES tonemapping.
- Editor setup diagnostics, including per-camera warnings for simultaneous Unity Bloom.
- Separate runtime/editor/sample assemblies and portable verification tools.
- Explicit release export with a primitive-based Getting Started demo and offline documentation.

Release candidate only. Profiling and publisher submission review remain outstanding.
