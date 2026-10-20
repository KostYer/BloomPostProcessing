# Layer Aware Bloom — Project Brief and Development Plan

Status: three independent opaque Rendering Layer bloom groups are implemented, including HDR source extraction, separate URP Gaussian pyramids, additive scene composition before post-processing, and combined/individual previews.

## Goal

Build a Unity 6 URP Renderer Feature using Render Graph, intended to become an Asset Store package. Mesh Rendering Layers select independent glow groups whose colored halos can overlap without losing their individual settings.

Current project baseline, read from the project files:

- Unity: `6000.3.14f1`.
- URP: `17.3.0`.
- Initial scope: opaque meshes.
- Initial group count: three; planned maximum: twelve.

These versions are the development baseline, not a promise of compatibility with every Unity 6 or URP release.

## Required behavior

### Independence from built-in post-processing

- The feature owns its settings, `LayerAwareBloom.BloomDownscale` enum, materials, filter passes, and composition. It does not read Volume settings or invoke Unity's built-in bloom, color grading, or tonemapping passes.
- Bloom works with camera post-processing disabled. `BeforeRenderingPostProcessing` / `AfterRenderingPostProcessing` are renderer scheduling points, not calls into the post-processing stack. Other effects can optionally process the composed image afterward.
- The filter math remains locally implemented and based on the previously verified URP Gaussian reference. Shared URP/SRP Core rendering APIs and shader utilities (RenderGraph, camera resources, blitting, sampling, color-space conversion) remain infrastructure dependencies.
- Quality uses the feature-local `_LAYER_BLOOM_HQ` keyword. There is no dependency on the post-stack `_ENABLE_ALPHA_OUTPUT` keyword; extracted sources already contain selected RGB with alpha 1.
- Downscale values remain Half=0 and Quarter=1 to preserve existing serialized settings. Unity's HDR grading pipeline setting is optional. The feature now provides its own optional HDR grading controls, independent of that setting and Volume components.

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

## Current step: three independent HDR bloom groups

`RenderingLayerBloomFeature.cs` extracts the visible selected opaque surfaces' HDR scene color independently for each of three groups, then processes each with its own URP Gaussian bloom pyramid. It remains attached to `Assets/Settings/PC_Renderer.asset`; the original script and shader GUIDs are preserved.

Processing order:

1. After opaques, draw each active group's Rendering Layer selection into its own white-on-black mask against scene depth. Every extraction reads the same camera color, before any group bloom is composed.
2. Point-sample camera color and selection into a separate full-resolution RGBA16F source. Selection happens before any downsampling, so unselected neighbours cannot enter the prefilter through bilinear samples. The renderer requests an intermediate camera color texture for this read.
3. Apply URP's prefilter: gamma-space Threshold converted to linear, fixed soft knee of half that linear threshold, and Clamp. Preserve HDR RGB magnitude and color.
4. Run a separate Gaussian downsample/upsample pyramid per group. Halos spread freely without further depth masking.
5. In one final pass before post-processing, sum the three reconstructed RGB bloom textures, each multiplied by its own linear tint and intensity, and add that sum to the current scene color. Write a separate destination texture and assign it to `UniversalResourceData.cameraColor`, so subsequent URP passes consume the combined image. Preserve scene alpha.
6. Optional feature-owned HDR grading adjusts the combined scene and bloom in that same composite pass. Exposure and color filter multiply linear RGB, contrast adjusts luminance around 18% gray while retaining RGB ratios, and saturation blends toward luminance. Alpha is preserved. There is no extra fullscreen pass and no tonemapping in this feature.
7. Any separately enabled URP post-processing follows. The feature's grading also works when camera post-processing is disabled; built-in grading, if enabled, would apply an additional adjustment.

HDR Color Grading controls are global, independent of the three bloom groups: Enable Color Grading (off by default), Exposure in stops (-10 to 10), Contrast (-100 to 100), Saturation (-100 to 100), and Color Filter. Neutral is 0/0/0/white. Exposure +1 doubles scene and glow, saturation -100 gives grayscale, and the filter is converted from the picker to linear RGB once. Contrast uses a power curve on luminance relative to 0.18, preserving black. HDR values above 1 remain intact; grading bounds output only to nonnegative finite half-float values. This is a small analytic grading implementation, not Unity's grading/LUT pipeline.

Grading runs even when Enable Bloom is off, all bloom groups are disabled, or their masks are empty. These cases skip the bloom masks and pyramids. Objects outside the bloom selection still receive global grading and remain in the scene. Both fullscreen debug previews bypass grading. Disable both grading and bloom to return to the untouched scene.

Grading verification passed 61 GPU checks in Unity 6000.3.14f1 / URP 17.3 / DX12 / Linear at 256x128: the 45 existing bloom checks plus 16 checks for neutral grading, positive/negative exposure, HDR and alpha preservation, grayscale, linear color filter conversion, bloom disabled, both effects disabled, empty selection, both debug previews, contrast pivot, black, HDR contrast, and finite output at extreme settings. Grading checks run with camera post-processing disabled; the existing optional ACES integration checks still pass. The fixture restores feature settings and closes its temporary scene. Script and report are archived in ignored `Temp/BloomGradingVerification.*`. Gamma color space and other graphics backends remain unverified.

Controls: Enable Bloom, Threshold (default 0.9), Intensity (default 1), Clamp (default 65472), Scatter (0.7), Max Iterations (default 6, range 2–8), Half/Quarter Downscale, and High Quality Filtering. Actual level count depends on resolution. HQ uses a 13-sample prefilter and bicubic upsampling, not 13 pyramid levels. Existing single-group values migrate into Group 1. Groups 2 and 3 start disabled, with Rendering Layer masks 4 and 8. The existing RenderingLayers scripting property addresses Group 1; Groups exposes all three settings objects. Hidden legacy fields are retained solely for asset migration.

Normal output is the scene plus selective bloom and optional grading. Click **Show Bloom Fullscreen** / **Hide Bloom Fullscreen** to toggle the bloom-only debug output. Disable **Enable Bloom** to remove glow, or combine it with the fullscreen preview to view the selected HDR source without thresholding, filtering, or intensity scaling. Grading has its own independent switch. The Preview selector isolates a group or displays their sum. With Enable Bloom off, source preview shows the selected group (All defaults to Group 1), without tint or intensity. Disabled/empty groups preview as black. Frame Debugger names identify each group's mask, source, and pyramid. The old Blur Mask field migrates to Enable Bloom.

This step uses the rendered surface color, including lighting, textures, and emission, rather than directly reading a material property. HDR must be enabled on the URP asset and camera to preserve source values above 1. Increasing material emission/HDR brightness now increases bloom, subject to threshold and clamp. Intensity scales the finished bloom independently of threshold.

The optional fullscreen preview still replaces the camera output after post-processing, without tone mapping; values above the display range can clip. Preview mode bypasses scene composition. Normal composition happens after transparent rendering, but only the selected opaque surfaces captured earlier generate bloom. Built-in URP Bloom is independent and would add another bloom effect if enabled; it remains disabled in the sample profile. Transparency selection, custom vertex deformation, alpha-clipped geometry, camera stacking, XR, MSAA edge behavior, and dynamic resolution remain outside the verified scope. Selection still uses the existing opaque override-material mask.

Multi-group membership is inclusive: any overlap between a renderer's Rendering Layer mask and a group's mask selects that renderer for that group. Matching several groups intentionally contributes to each, with additive overlap rather than deduplication. Tint is a direct RGB multiplier (converted from the color picker's sRGB to linear), applied after thresholding/filtering, and never replaces source colors. Disabled groups, zero-intensity groups in bloom mode, and empty masks do not record masks or pyramids. Isolated previews process only the selected group. Frame-data and pooled pass slots reset inactive textures and weights to prevent stale contributions.

High Quality Filtering now also controls each group's final reconstruction into full-resolution scene/preview output: bicubic for HQ, bilinear otherwise, matching URP's final bloom sampling. Previously, HQ only affected prefiltering and the pyramid's upsample stages. Texture sizes and quality flags are recorded separately per group and reset for invalid/disabled slots.

A same-camera comparison against the installed Unity bloom uses one effect at a time, all visible opaque renderers selected, white tint, matching threshold/scatter/downscale/iterations/HQ, and the same enabled post-processing. At 640x480 on the sample scene, four comparisons (HQ off/on, 3/6 levels) showed mean maximum-channel differences of roughly 0.0015–0.0025 and ours adding about 3.6–5.8% more summed RGB after post-processing. HQ final sampling reduced the largest HQ per-pixel differences from about 0.057–0.059 to 0.031–0.033; it did not eliminate the residual strength difference. This is an end-to-end scene comparison, not a claim of exact equality or a measurement of physical light energy. Fixture/report: ignored `Temp/BloomParityVerification.*`; the fixture restores camera/feature settings and removes its temporary volume.

Three-group verification passed 45 GPU checks at 256x128 in Unity 6000.3.14f1 / URP 17.3 / DX12 / Linear color space. It covers mixed HQ/downscale settings, independent intensity/threshold/scatter/tint, additive overlap and scene composition, overlapping membership, multicolor sources, occlusion, disabling/re-enabling/zero/empty groups, subsequent empty renders, and the earlier HDR/tonemapping checks. Combined preview versus isolated sums differed by at most 0.001951 RGB; scene-plus-groups by 0.002930. A separate serialization check confirmed preservation of legacy settings, disabled extra-group defaults, and all three groups surviving serialization. Reports and repeatable editor fixtures are archived in ignored `Temp/BloomGroupsVerification.*` and `Temp/BloomGroupMigrationVerification.*`. The older composition fixture below targets the previous single-group fields; use the new groups fixture for current code.

Scene composition verification (Unity 6000.3.14f1, URP 17.3, DX12, Linear color space) passed 22 GPU checks at 256x128. Both quality modes verified scene-plus-preview equivalence, visible halos, HDR preservation, intensity scaling/zero, disabled bloom, selected-source preview, and empty selection. Maximum RGB addition error was 0.00293; intensity scaling error was 0.00391. A uniform HDR source with bloom followed by ACES and exposure matched an independently rendered brighter source with the same post-processing within 0.00098. Shader compilation and rendering reported no errors. Report: ignored `Temp/BloomCompositeVerification.txt`; repeatable editor fixture: `Temp/BloomCompositeVerification.cs` (copy to the Editor folder and use its Tools menu). The fixture restores feature settings and closes its temporary additive scene.

Previous mask/blur verification covered visibility and occlusion, grayscale edges/spread, empty selection, and preview off. The original URP shader comparison at 384x192 in both quality modes and iteration limits 2/6/8 gave red-channel differences below 0.017 maximum and 0.00081 mean. Current HDR validation in Unity 6000.3.14f1 / URP 17.3.0 passed 26 GPU render/readback checks at 256x128: HDR source value 4 preserved, selection, adjacent HDR rejection, RGB color retention, source-brightness response, intensity scaling/zero, threshold rejection, dim sources, clamp/zero, full/partial occlusion, halo spread across occluders, empty selection, and preview off. Both HQ modes matched an original URP bloom shader reference on the captured selected HDR source with no measured RGB difference in this fixture (four pyramid levels). This is a controlled fixture result, not a general bit-identical claim. The report is in ignored `Temp/HdrBloomVerification.txt`; the temporary verification script is archived in `Temp/HdrBloomVerification.cs`.
