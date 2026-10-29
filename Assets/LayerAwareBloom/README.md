# Layer Aware Bloom

Start with [Quick Start](Documentation/QuickStart.md), [Compatibility](Documentation/Compatibility.md), and [Release Preparation](Documentation/ReleasePreparation.md). Changes are listed in [CHANGELOG](CHANGELOG.md).

The release candidate includes the primitive-based `Samples/GettingStarted` demo. The broader sample scenes/art described in the project layout below belong to the development project and are excluded from the explicit release export.

This package contains two independent URP renderer features:

- **Rendering Layer Bloom** selects opaque surfaces and transparent renderers by Rendering Layer, builds up to six independent bloom groups, and optionally grades the combined image.
- **HDR Tonemapping** maps the whole HDR image to SDR with **Neutral** or **ACES**. **None**, or disabling the feature, bypasses the pass.

Add them from your Universal Renderer asset's **Add Renderer Feature** menu. Their default **Settings Source** is **Scene Volumes**. Configure **Layer Aware Bloom / Bloom** and **Layer Aware Bloom / Tonemapping** as separate overrides in a scene Volume Profile. Tonemapping works without the bloom feature, and both work with the camera's Post Processing checkbox off.

## Project layout

The bloom package and its examples live under `Assets/LayerAwareBloom/`:

```text
LayerAwareBloom/
  Runtime/                 Renderer features and source components
  Shaders/                 Bloom, tonemapping, and UI capture shaders
  Editor/Inspectors/       Custom inspectors and authoring tools
  Editor/Verification/     Explicitly invoked rendering checks
  Samples/
    Scenes/                BloomGroups and BloomSources demos
    Scripts/               Demo movement, labels, and bloom cycling
    Materials/             Named demo materials, GlowingCubes, and Sky
    Shaders/               EmissionOnly demo shader
    Models/, Sprites/, Fonts/
```

Project-wide pipeline assets are in `Assets/Settings/Rendering/` (DesktopRenderer, DesktopPipeline, MobileRenderer, MobilePipeline). Volume profiles and input actions are under `Assets/Settings/Volumes/` and `Assets/Settings/Input/`. The imported fire effects are in `Assets/ThirdParty/VFX/`. Unity's TextMesh Pro and tutorial folders retain their standard layouts.

## Standalone setup

1. Enable HDR on the URP asset and camera.
2. Add Rendering Layer Bloom. Create a Global Volume in the scene with a unique profile, add **Layer Aware Bloom / Bloom**, and override a group's Rendering Layers and Intensity (start at 1).
3. Keep any lighting layers on the selected renderers as well. Rendering Layers also control which lights illuminate a surface.
4. Add HDR Tonemapping and add **Layer Aware Bloom / Tonemapping** to the scene profile. Override Mode to Neutral or ACES.
5. The camera's Post Processing option can remain off.

### Scene settings and migration

With **Scene Volumes**, an absent, disabled, zero-weight, or camera-excluded override contributes no effect. Numeric parameters blend using Volume weight, priority, and local collider distance. Rendering Layer masks, enabled flags, iteration counts, and enum modes switch discretely. New Volume groups have empty masks and intensity zero; enable the override checkboxes for values a profile should control. HDR grading stays in the Bloom override and works even with Enable Bloom off.

Use **Create Scene Volume from Existing Renderer Settings** on the bloom feature (also under Tools / Layer Aware Bloom) to copy all six groups, grading, and the renderer's tonemapping mode into a new scene profile. Save the scene after creating the Volume. Existing profiles are selected rather than overwritten. The original Renderer settings remain available through the explicit **Renderer Asset** legacy mode; they are not a fallback when Scene Volumes has no matching profile.

A Global Volume is a scene object, not an automatic scene boundary: additively loaded scenes can contribute to the same camera, just like Unity post-processing. Use unique profiles for independent scene settings, and the camera's **Volume Layer Mask** to separate cameras/loaded scenes. This is the GameObject layer of the Volume, separate from the Rendering Layers selecting glowing objects. Via Scripting Volume update mode requires the normal camera Volume-stack update API. Custom SingleCameraRequest callers must evaluate the intended stack themselves; StandardRequest runs URP's normal per-camera evaluation.

**Tools / Layer Aware Bloom / Validate Setup** checks settings isolation, numeric blending, override checkboxes, local/global Volumes, disabling/unloading, standalone tonemapping, grading, and scene-instance demo cycling. The current DX12/Linear fixture passes 23 checks when the development sample is installed. The transparent/VFX regression also passes with explicit legacy test settings. Reports are written under ignored `Temp/`; the fixture restores renderer state and removes its temporary scenes.

The package runs in this order regardless of the order of these two features in the inspector:

**HDR scene → selective bloom → optional grading → tonemapping → display**

Bloom-only and source debug previews intentionally replace the output afterward and remain untonemapped.

New slots start disabled with no Rendering Layers assigned. Existing slot settings are preserved when upgrading from three slots. Disabled slots, empty layer masks, zero intensity, zero clamp, and black tint skip mask/source/pyramid rendering. Active results are packed for the final blend, which stops sampling at the active count. Raw source previews ignore intensity, clamp, and tint. An enabled layer mask with no visible matching objects is still rendered; no mask readback is used to detect empty groups.

Each renderer contributes to only the **first matching active group**, in inspector order. For example, a renderer on Light Layer 1 and Light Layer 2 belongs to Group 1 if Group 1 selects Light Layer 1, even when Group 2 selects Light Layer 2. Disabled and zero-output bloom groups do not claim renderers. Isolating a group in the preview preserves this assignment; raw source preview ignores output settings as described above.

With multiple groups, the feature checks renderer membership once per camera. A later group with overlapping renderers gets a separate culling result that excludes those already assigned. Renderer visibility is restored immediately, before drawing; materials and Rendering Layer Masks are not changed. This preserves normal scene lighting and transparent material behavior, at the cost of additional CPU culling for groups that overlap. Single-group rendering uses the normal camera culling result.

## Integration

### Optional bloom additions

Each group has a collapsible **Optional Additions** inspector section, initially closed. Its open/closed state is remembered during the editor session; hiding the controls does not disable their effects. Existing and new groups default to the original Unity-style filtering:

- **Soft Knee: 0.5**. Transition width relative to the linear threshold; 0 gives a hard threshold and 1 a wider transition. The default reproduces the original fixed knee.
- **Radius: 1**. Multiplies horizontal and vertical blur sample spacing, with a conservative range of 0.5–1.5. Lower values tighten the glow; higher values widen it. Scatter, downscale, and pyramid iterations still affect spread. The default retains the original taps and weights; this does not replace the Gaussian pyramid. At resolutions with only one pyramid level there are no Gaussian blur passes, so radius has no effect.
- **Stop NaN / Infinity: off**. When enabled, one extra full-resolution pass removes pixels containing invalid RGB from the selected source after transparent/UI capture and before filtering. Valid source pixels are preserved. This protects bloom, not the original scene color or the raw source debug preview. It adds a full-resolution HDR texture and draw per enabled group.

**Reset Group to Defaults** sits outside the optional foldout. In a Volume it clears that group's overrides and resets the mask/intensity to zero. In the legacy Renderer Asset inspector it restores every setting, including the name: Group 1 is enabled on Default, and Groups 2–6 are disabled with empty masks. Other groups are retained. Both support the normal Inspector Undo/Redo workflow.

### Assigning a VFX hierarchy

Add **Rendering Layer Assigner** to the effect root, select its **Rendering Layers**, and press **Apply to Root and Children**. This replaces the mask on every Renderer in the hierarchy, including inactive children, particle renderers, VFX Graph renderers, and meshes. Include any lighting layers you want to retain in the selected mask. The button supports Undo and prefab instance overrides. It runs only when pressed; newly added children require another Apply. GameObject layers and Light components are unaffected.

### Canvas UI

Add **UI Bloom Source** (Add Component > Rendering) to an Image, Raw Image, legacy Text, or parent Canvas. Choose the same **Rendering Layers** as a bloom group. **Include Children** applies the selection to descendants; the closest applicable component wins. A child component with an empty mask, or a disabled component, opts its subtree out of inherited bloom. **Emission Multiplier** increases brightness only in the HDR bloom source, leaving the normal UI unchanged. Start with 2–4 for a white PNG and a group threshold of 1.

Supported render modes are **Screen Space - Camera** (with its Render Camera assigned) and **World Space**, on supported base cameras. **Screen Space - Overlay is ignored.** UI uses the same first-active-group ownership as meshes, including preview behavior and fallback when an earlier group is disabled or has zero output.

The UI capture supports **UI/Default** Image, RawImage, and legacy Text geometry, including sprite alpha, filled/sliced images, renderer/Graphic/CanvasGroup alpha, RectMask2D clipping and softness, and nested sprite-alpha Masks. Unsupported graphics (including TextMeshPro/SDF text and custom UI shaders) do not support UI bloom selection or occlusion; their native rendering can still contribute to a group selecting the Default rendering layer. The component inspector warns about these. UI Toolkit is outside this uGUI integration.

Each group replays supported UI back-to-front into its HDR source before thresholding/blur. Unselected UI draws zero RGB with its original alpha, attenuating sources behind it. Masks use a private stencil attachment and do not modify the camera stencil or original materials. Opaque camera depth occludes UI; transparent scene renderers are not interleaved with this UI replay. For World Space UI intersecting transparent meshes/particles, that ordering remains a limitation. Blur can extend beyond a mask boundary after the source has been clipped, as with other bloom sources.

The native Canvas draw uses an internal material wrapper that reuses Unity's **UI/Default** pass. It is installed after uGUI rebuilds materials, before Canvas batching, and prevents native UI from leaking into the scene source or being counted twice on the Default rendering layer. User materials and Graphic material assignments remain unchanged. CanvasRenderer bindings are restored when the feature is disabled or disposed.

UI capture adds Canvas material/batch updates, CPU geometry/property collection once per camera, extra UI draws for each active group, and mask push/pop draws for each masked element. Keep large UI hierarchies and many active bloom groups in mind when profiling. The serialized **UI Bloom Shader** and **UI Native Shader** references on the renderer feature keep both shaders included in builds.

Run **Tools > Layer Aware Bloom > Validate Setup** for the UI render regression. It uses a temporary preview scene, restores settings, and writes reports/images under `Temp/`.

Transparent particles and meshes are drawn into each group's HDR source using their original materials, back-to-front, with camera depth testing and depth writes disabled. Texture alpha, additive blending, vertex animation, and emission are preserved. Assign the group's Rendering Layer to every relevant child Particle System Renderer or Mesh Renderer; assigning it only to an empty VFX parent does not select the visible children. Soft particles and distortion have access to URP's depth and opaque-color textures.

Opaque selection is captured before normal transparency rendering, so unselected particles cannot contaminate selected opaque colors. Only selected transparent renderers participate in the source's transparency blending; unselected transparent foregrounds do not attenuate bloom, and refractive shaders can include background color from the camera opaque texture. Opaque objects still occlude transparent sources. Each active group adds a transparent renderer-list draw in addition to its existing mask and blur passes.

Run **Tools > Layer Aware Bloom > Validate Setup** to check HDR particles, alpha, additive blending, layer exclusion, opaque occlusion, bloom spread, and the imported fire prefab when present. The check discovers the active URP asset's default renderer, uses a temporary preview scene, restores feature settings, and writes its report and captures under `Temp/`.

Tonemapping has its own Volume override, material, shader, and Render Graph pass. It uses SRP Core's Volume framework and color math, without Unity's built-in Bloom/Tonemapping overrides, post-processing passes, or grading LUT. The shader reference remains serialized in the renderer feature for builds.

Use one tonemapper. Leave Unity's Tonemapping override disabled when using HDR Tonemapping. If Unity post-processing is enabled, it runs before HDR Tonemapping, so Unity bloom also receives HDR values. Use HDR Color Grading mode on the URP asset for this mixed setup. For the standalone setup, camera Post Processing can remain off.

The tonemapper preserves alpha and writes a separate color texture. Neutral and ACES compress highlights and bound RGB to the SDR range. None performs no transformation or extra pass.

Currently verified: Unity 6000.3.14f1, URP 17.3, Render Graph, DX12, Linear color space, base Game/Scene View cameras, SDR output. Gamma, camera stacking, XR, MSAA, dynamic resolution, HDR-display output, and the compatibility renderer path are not yet validated. Opaque selection supports ordinary meshes; transparent selection uses the original forward material passes.
