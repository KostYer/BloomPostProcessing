# Layer Aware Bloom — Project Brief and Development Plan

Status: visible opaque Rendering Layer silhouettes and a URP Gaussian bloom blur of that mask are implemented, with a fullscreen grayscale preview. Colored HDR source extraction and scene bloom composition remain planned. Implementation proceeds one visible milestone at a time.

## Goal

Build a Unity 6 URP Renderer Feature using Render Graph, intended to become an Asset Store package. Mesh Rendering Layers select independent glow groups whose colored halos can overlap without losing their individual settings.

Current project baseline, read from the project files:

- Unity: `6000.3.14f1`.
- URP: `17.3.0`.
- Initial scope: opaque meshes.
- Initial group count: three; planned maximum: twelve.

These versions are the development baseline, not a promise of compatibility with every Unity 6 or URP release.

## Required behavior

### Selection and group controls

- Select meshes through their **Rendering Layers**, not GameObject Layers.
- Each group has independent intensity, tint, and spread/scatter controls.
- Preserve source HDR brightness and use the installed URP bloom's clamp, threshold, and soft-knee behavior. Brighter source pixels contribute more bloom, as in built-in bloom; group intensity scales that contribution.
- Preserve multicolored source contributions within each group. A group must not collapse into a single flat color or scalar mask before filtering.
- Preserve the rendered source RGB without brightness normalization. The effect adds selective group membership and independent controls to URP-style bloom.

### Visibility and halo overlap

- Fully occluded meshes produce no bloom.
- Only visible portions of selected meshes contribute bloom source pixels.
- Once generated, halos spread freely into surrounding pixels, including across occluder edges.
- Establish visibility when extracting the source. Do not clip the completed halo against scene depth or the original mesh silhouette.
- Combine group halos additively, retaining each group's colors, intensity, tint, and spread/scatter.

### Filtering

- Each group has a separate RGB bloom pyramid.
- Preserve RGB contributions through downsampling and upsampling.
- Match the installed URP bloom's downsample and upsample filters. Inspect its actual shader and pass implementation before implementing these stages.
- Match URP's HDR prefilter as well as its downsample and upsample filters. Rendering Layer selection determines which visible sources are eligible to contribute.
- Decide supported quality settings and pyramid sizing when implementing the filter stages.

### Composition

- Default: add bloom to the HDR scene before tone mapping.
- Optional artistic mode: compose bloom after tone mapping.
- Confirm the actual URP Render Graph integration points before choosing pass placement. The intended visual ordering is the requirement; an assumed render event is not yet an implementation decision.
- Define the optional mode's color space, intensity interpretation, and output-range behavior before implementing it.

## Open decisions

Resolve these with focused visual comparisons as their milestones approach:

1. **Selective HDR source extraction.** Determine how to obtain the selected visible surfaces' rendered HDR RGB while excluding unselected surfaces. Preserve shading and emission as represented in the reference scene color. Apply selection before spatial filtering so unselected pixels cannot leak into a group's source. Validate selection boundaries against the reference prefilter; masking and filtering order matters.
2. **Material support.** Define the first supported opaque shader/material path and how source extraction obtains its color. Arbitrary custom shaders are not assumed to work automatically.
3. **Rendering Layer membership.** Decide whether a mesh may contribute to several groups, how overlapping masks behave, and how masks are assigned without interfering with other Rendering Layer uses.
4. **Spread/scatter semantics.** Determine whether one control is sufficient and how it maps to the URP pyramid. Avoid promising independent radius and scatter controls before their behavior is defined.
5. **URP bloom coexistence.** Decide whether built-in bloom should be disabled, supported alongside this effect, or explicitly configured. Avoid unintended additional bloom when both are enabled.
6. **Compatibility targets.** Define supported renderer paths, cameras, graphics APIs, and quality options after the core result works.
7. **Threshold and clamp controls.** Preserve URP's behavior and use matching settings for the first comparison. Decide later whether these settings are shared across groups or independently configurable per group.

## Proposed processing flow

This is a conceptual flow; resource formats and Render Graph pass structure remain implementation choices.

1. Determine visible selected opaque surface pixels for each group using Rendering Layers and scene visibility.
2. Extract the selected HDR RGB contributions and apply URP's prefilter, including clamp, threshold, and soft knee, with selection established before spatial filtering.
3. Build and reconstruct a separate RGB bloom pyramid for each active group using the URP filters.
4. Apply each group's controls at appropriate stages; spread/scatter affects its own pyramid reconstruction.
5. Add group results together and compose at the selected point in the camera pipeline.

Keep group contributions separate until their independent filtering and controls have been applied. Shared shaders and scheduling optimizations are possible later, provided they preserve this behavior.

## Incremental milestones

At each milestone, show a visible result in a small test scene, record known limitations, and review the result before expanding scope.

### 0. Bloom filter comparison — first debug experiment

Start with a Render Graph Renderer Feature that reproduces the installed URP bloom's HDR source processing and filtering. Compare this reproduction with built-in bloom before adding Rendering Layer selection.

- Use URP 17.3.0's default Gaussian filter mode as the reference. Its other filter modes are outside this first comparison.
- First verify the reproduction using the same HDR input, prefilter, pyramid sizes, iteration count, quality, scatter, intensity, and tint as built-in bloom. Disable lens dirt and unrelated post effects for this controlled test.
- Preserve source HDR magnitude and reproduce URP's clamp, threshold, and soft knee. No color-normalized branch is required.
- Initially use a controlled opaque scene with a black background and colored unlit meshes. Reading camera color includes the visible scene, so this experiment does not yet implement mesh selection or material-level color extraction.
- Provide built-in versus custom A/B comparison under identical camera and post-processing settings, plus a custom bloom-only debug view. A split view can follow if useful; compute complete images before splitting the display to avoid a filtering seam.
- Do not add built-in bloom on top of a custom bloom composite. Enable only the bloom implementation being compared for each A/B capture, using the same downstream tone mapping.

Render Graph approach: retain a ScriptableRendererFeature and ScriptableRenderPass, implement RecordRenderGraph, and record explicit raster passes for source preparation, downsample stages, upsample stages, and final comparison/composition. Declare every texture read and attachment write. Use graph-owned temporary textures and a separate composite destination that becomes the camera color for subsequent processing.

Acceptance:

- The custom implementation reproduces the reference bloom under matched settings.
- Changing source HDR magnitude changes bloom consistently with built-in bloom, including near the threshold.
- Both results can be inspected visibly with identical filter settings.
- Record any measured or visible discrepancies before proceeding to selection and occlusion.

### 1. Visible selection and occlusion

Extend the debug feature with a view of one selected opaque group's visible pixels. Choose and document a minimal supported material path for this experiment.

Acceptance:

- Rendering Layer membership controls selection.
- A fully hidden mesh contributes nothing.
- A partly hidden mesh contributes only its visible portion.
- An unselected foreground mesh occludes the source correctly.

Inspect selection independently of the already validated bloom pyramid.

### 2. Selective HDR source extraction

Connect the visible selection to HDR RGB source extraction for one group, preserving the reference source brightness and URP prefilter behavior.

Acceptance:

- Several colors on one mesh remain distinguishable in the source.
- Changing material HDR magnitude affects bloom source strength consistently with URP.
- Unselected and fully occluded surfaces contribute no source energy, including near selection boundaries during prefiltering.
- Dark and black source behavior follows URP's threshold and soft knee.
- The selected material path and any source-capture limitations are documented.

### 3. One complete bloom group

Feed the selected HDR source into the validated RGB pyramid, with independent intensity, tint, and spread/scatter. Compose in HDR before tone mapping.

Acceptance:

- A visible colored halo appears and each control has a clear effect.
- Halos extend beyond source silhouettes and across occluder edges.
- Fully hidden sources still produce no halo.
- Filtering is compared against the installed URP implementation using equivalent filter inputs and settings, separately from source extraction differences.

### 4. Three independent groups

Extend the working result to three independently configured groups and resolve multi-group membership behavior.

Acceptance:

- Changing one group's settings does not alter another group's contribution.
- Groups can have visibly different colors and spread/scatter.
- Overlap is additive in the HDR result before tone mapping.
- A multicolored source remains multicolored within its group.

### 5. Optional post-tone-mapping composition

Add the artistic composition mode after verifying the correct URP integration point and defining its color handling.

Acceptance:

- The default remains HDR composition before tone mapping.
- Switching modes produces the intended visual difference.
- Output behavior and the interaction with built-in post-processing are documented.

### 6. Scaling and performance

Expand toward twelve groups after the three-group result is stable.

Acceptance:

- Profile GPU time and memory at representative resolutions and active group counts.
- Avoid allocating or processing explicitly disabled groups.
- Validate that optimizations preserve independent RGB pyramids and additive results.
- Document practical performance limits based on measurements.

### 7. Asset Store package preparation

Once the core visual behavior is validated, prepare package structure, settings UI, demo scenes, documentation, defaults, and a tested compatibility matrix.

Broader rendering support is separate follow-up work. Transparency, camera stacking, XR, MSAA, dynamic resolution, alternate renderer paths, and arbitrary custom materials require explicit investigation and testing before being advertised as supported.

## Visual test scene

Build up a compact scene as the milestones require:

- Three selectable meshes assigned to separate glow groups.
- A mesh or texture with multiple saturated colors.
- An unselected opaque occluder covering part of a glowing mesh.
- A fully hidden selected mesh.
- Sources positioned so their halos overlap.
- Matching source examples with different HDR material magnitudes.
- Dark, black, and near-threshold source examples for verifying URP prefilter behavior.

Debug views should expose selection, source RGB, and individual group bloom as those stages become available. Compare additive behavior in HDR as well as the final image, because tone mapping can change the perceived overlap colors and brightness.

## Immediate next step

Current user-directed scope is **blurred silhouette debugging**, taking precedence over the earlier milestone order. The feature is attached to `Assets/Settings/PC_Renderer.asset`. Select its Rendering Layers and click **Show BW Fullscreen** to inspect the grayscale result; click **Hide BW Fullscreen** to restore the scene. Disable **Blur Silhouette** to compare the sharp source mask. The mask draws after opaques against scene depth, the blur follows without depth clipping, and the preview draws after post-processing.

The existing `SilhouettePass` in `RenderingLayerSilhouetteFeature.cs` records both the mask and blur. The existing `Silhouette.shader` contains the URP 17.3 Gaussian bloom filter calculations inline: prefilter, horizontal downsample blur, vertical blur, and upsample. Keep the original file, class, and shader names while comparing incremental changes in Git. The blur uses matching resolution/iteration calculations and scatter remapping. Threshold and knee are zero for this mask experiment so white silhouettes contribute directly. Controls are Scatter (default 0.7), Max Iterations (default 6, range 2–8), Half/Quarter Downscale, and High Quality Filtering. The actual level count is resolution-dependent. The HQ prefilter uses 13 samples; that is not 13 pyramid levels. The result remains a grayscale silhouette on black, without scene-color composition.

Verified in Unity with isolated render/readback checks: visible selection, partial and full occlusion, unselected objects, black background, an empty layer mask, changing the layer mask, and restoring normal rendering. Blur verification compared against the original URP shader sequence in both quality modes at iteration limits 2, 6, and 8 (384x192 input), and checked grayscale output, soft edges, spread outside the mask, empty selection, and preview off. Captured red-channel differences were below 0.017 maximum and 0.00081 mean; this is a close numerical comparison, not a bit-identical output claim. Initial support is standard opaque meshes; custom vertex deformation and broader rendering compatibility remain out of scope. Review this result before adding HDR source color or composition.
