# Compatibility and support scope

Initial baseline: Unity 6000.3.14f1, URP 17.3.0, uGUI 2.0.0, Windows, Direct3D 12, Linear color space, Render Graph, base Game/Scene View cameras, SDR output.

Supported features within that baseline: opaque meshes; transparent particles/meshes using their original forward materials; up to six Rendering Layer groups; Image/RawImage/legacy Text capture on Camera/World Space uGUI; RectMask2D and nested Masks; optional grading and standalone Neutral/ACES tonemapping.

Not validated or not supported: Built-in pipeline, HDRP, compatibility mode, XR, camera stacking, dynamic resolution, MSAA, Gamma, HDR displays, other graphics APIs/platforms/Unity versions. Do not advertise support for these until separately tested. Opaque custom vertex deformation is unsupported. Overlay UI, TMP/SDF selection, custom UI shader selection, and UI Toolkit are unsupported.

Transparent unselected scene foregrounds do not attenuate selected bloom sources. World Space UI is not interleaved with transparent meshes. Blur can extend beyond source mask/occlusion boundaries. Rendering Layers also affect lighting; retain required lighting memberships.

The feature intentionally does not implement Unity's Volume controls. Built-in Bloom can be combined, but increases glow and can illuminate excluded sources; the editor warns when both are active for a camera.

Performance is not yet profiled. No FPS, GPU-time, memory, mobile, or VR performance claims are made. Stop NaN adds a full-resolution HDR texture and pass per active protected group. Multiple groups and UI replay add work; see README.
