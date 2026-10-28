# Layer Aware Bloom — listing draft

Selective bloom for Unity 6 URP, with up to six independently controlled Rendering Layer groups. Give different objects different glow intensity, tint and spread while keeping unselected sources out of the bloom capture.

## Features

- Unity-style Gaussian bloom with per-group controls and first-active-group ownership.
- Optional soft knee, blur radius and NaN/Infinity protection.
- Transparent mesh and particle sources using their original materials.
- Supported Camera/World Space uGUI selection, masks and HDR source brightness controls.
- Independent Neutral/ACES tonemapping and optional HDR color grading.
- Collapsible additions, full-group reset with Undo, setup warnings and simultaneous Unity Bloom diagnostics.
- Getting Started demo using primitive geometry, offline instructions, and explicit verification tools.

## Requirements and limitations

Unity 6000.3.14f1, URP 17.3.0, uGUI 2.0.0, Universal Renderer with Render Graph, Linear color space and HDR. Initial target: Windows/DX12, base cameras, SDR output. No claim of mobile, XR, HDRP, Built-in pipeline, camera stacking, compatibility mode, dynamic resolution or MSAA support. Overlay UI, TMP/SDF selection, UI Toolkit, custom UI capture and opaque vertex-deformed masks are unsupported. See Compatibility.md for transparent/UI ordering limitations.

Performance profiling is pending. Do not add frame-rate or GPU-time claims yet.

## Third-party notice

Includes adapted Unity URP bloom filtering under the Unity Companion License for Unity-dependent projects. See Third-Party Notices.txt and Documentation/Licenses. Requires Unity Registry dependencies; imported development artwork and fire VFX are not included.

## AI description draft

OpenAI Codex assisted with C# and shader implementation, custom inspectors, verification tooling, documentation and release preparation. The implementation was adapted for Rendering Layer selection, independently controlled bloom groups, transparency and uGUI capture. Validation results are recorded separately. Unity-derived filtering is documented in Third-Party Notices.

## Publisher decisions still required

Publisher identity/support contact, final price, license/submission review, final screenshots and video, verified version/platform selection, and portal submission. Review this draft against the exact shipped release candidate. No upload or publication has been performed.
