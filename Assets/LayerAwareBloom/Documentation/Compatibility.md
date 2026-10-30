# Compatibility

Layer Aware Bloom uses URP's Universal Renderer with Render Graph, Linear color space, HDR rendering, base Game/Scene View cameras, and SDR display output. Use the quality settings in [Performance and Quality](Performance.md) to configure desktop and mobile projects.

Development reference: Unity 6000.3.14f1, URP 17.3.0, uGUI 2.0.0, Windows/Direct3D 12.

Supported features: opaque meshes; transparent particles/meshes using their original forward materials; up to six Rendering Layer groups; Image/RawImage/legacy Text capture on Camera/World Space uGUI; RectMask2D and nested Masks; optional grading and integration with Unity Neutral/ACES tonemapping.

## Rendering requirements and limitations

The feature requires URP with Render Graph; Built-in, HDRP, and the compatibility renderer path are not implemented. Use Linear color space and SDR display output. Opaque custom vertex deformation is unsupported. Overlay UI, TMP/SDF selection, custom UI shader selection, and UI Toolkit are unsupported.

**XR/VR:** currently unvalidated, as are MSAA, dynamic resolution, and camera stacking. Treat these as validation work before choosing quality settings.

Transparent unselected scene foregrounds do not attenuate selected bloom sources. World Space UI is not interleaved with transparent meshes. Blur can extend beyond source mask/occlusion boundaries. Rendering Layers also affect lighting; retain required lighting memberships.

Bloom settings use a custom Volume override; tonemapping uses Unity's standard override and requires camera Post Processing. Built-in Bloom can be combined, but increases glow and can illuminate excluded sources; the editor warns when both are active for a camera.

See [Performance and Quality](Performance.md) for settings that reduce cost, higher-quality starting points, and platform considerations.
