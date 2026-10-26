# Layer Aware Bloom

This package contains two independent URP renderer features:

- **Rendering Layer Bloom** selects opaque surfaces and transparent renderers by Rendering Layer, builds up to six independent bloom groups, and optionally grades the combined image.
- **HDR Tonemapping** maps the whole HDR image to SDR with **Neutral** or **ACES**. **None**, or disabling the feature, bypasses the pass.

Add them from your Universal Renderer asset's **Add Renderer Feature** menu. HDR Tonemapping works on its own and does not require the bloom feature, a Volume, or the camera's Post Processing checkbox.

## Standalone setup

1. Enable HDR on the URP asset and camera.
2. Add Rendering Layer Bloom and choose the groups' Rendering Layers.
3. Keep any lighting layers on the selected renderers as well. Rendering Layers also control which lights illuminate a surface.
4. Add HDR Tonemapping and start with Neutral. ACES gives a different, more filmic highlight/color response.
5. The camera's Post Processing option can remain off.

The package runs in this order regardless of the order of these two features in the inspector:

**HDR scene → selective bloom → optional grading → tonemapping → display**

Bloom-only and source debug previews intentionally replace the output afterward and remain untonemapped.

New slots start disabled with no Rendering Layers assigned. Existing slot settings are preserved when upgrading from three slots. Disabled slots, empty layer masks, zero intensity, zero clamp, and black tint skip mask/source/pyramid rendering. Active results are packed for the final blend, which stops sampling at the active count. Raw source previews ignore intensity, clamp, and tint. An enabled layer mask with no visible matching objects is still rendered; no mask readback is used to detect empty groups.

Each renderer contributes to only the **first matching active group**, in inspector order. For example, a renderer on Light Layer 1 and Light Layer 2 belongs to Group 1 if Group 1 selects Light Layer 1, even when Group 2 selects Light Layer 2. Disabled and zero-output bloom groups do not claim renderers. Isolating a group in the preview preserves this assignment; raw source preview ignores output settings as described above.

With multiple groups, the feature checks renderer membership once per camera. A later group with overlapping renderers gets a separate culling result that excludes those already assigned. Renderer visibility is restored immediately, before drawing; materials and Rendering Layer Masks are not changed. This preserves normal scene lighting and transparent material behavior, at the cost of additional CPU culling for groups that overlap. Single-group rendering uses the normal camera culling result.

## Integration

### Canvas UI

Add **UI Bloom Source** (Add Component > Rendering) to an Image, Raw Image, legacy Text, or parent Canvas. Choose the same **Rendering Layers** as a bloom group. **Include Children** applies the selection to descendants; the closest applicable component wins. A child component with an empty mask, or a disabled component, opts its subtree out of inherited bloom. **Emission Multiplier** increases brightness only in the HDR bloom source, leaving the normal UI unchanged. Start with 2–4 for a white PNG and a group threshold of 1.

Supported render modes are **Screen Space - Camera** (with its Render Camera assigned) and **World Space**, on supported base cameras. **Screen Space - Overlay is ignored.** UI uses the same first-active-group ownership as meshes, including preview behavior and fallback when an earlier group is disabled or has zero output.

The UI capture supports **UI/Default** Image, RawImage, and legacy Text geometry, including sprite alpha, filled/sliced images, renderer/Graphic/CanvasGroup alpha, RectMask2D clipping and softness, and nested sprite-alpha Masks. Unsupported graphics (including TextMeshPro/SDF text and custom UI shaders) do not support UI bloom selection or occlusion; their native rendering can still contribute to a group selecting the Default rendering layer. The component inspector warns about these. UI Toolkit is outside this uGUI integration.

Each group replays supported UI back-to-front into its HDR source before thresholding/blur. Unselected UI draws zero RGB with its original alpha, attenuating sources behind it. Masks use a private stencil attachment and do not modify the camera stencil or original materials. Opaque camera depth occludes UI; transparent scene renderers are not interleaved with this UI replay. For World Space UI intersecting transparent meshes/particles, that ordering remains a limitation. Blur can extend beyond a mask boundary after the source has been clipped, as with other bloom sources.

The native Canvas draw uses an internal material wrapper that reuses Unity's **UI/Default** pass. It is installed after uGUI rebuilds materials, before Canvas batching, and prevents native UI from leaking into the scene source or being counted twice on the Default rendering layer. User materials and Graphic material assignments remain unchanged. CanvasRenderer bindings are restored when the feature is disabled or disposed.

UI capture adds Canvas material/batch updates, CPU geometry/property collection once per camera, extra UI draws for each active group, and mask push/pop draws for each masked element. Keep large UI hierarchies and many active bloom groups in mind when profiling. The serialized **UI Bloom Shader** and **UI Native Shader** references on the renderer feature keep both shaders included in builds.

Run **Tools > Layer Aware Bloom > Verify UI Sources** for the UI render regression. It uses a temporary preview scene, restores settings, and writes reports/images under `Temp/`.

Transparent particles and meshes are drawn into each group's HDR source using their original materials, back-to-front, with camera depth testing and depth writes disabled. Texture alpha, additive blending, vertex animation, and emission are preserved. Assign the group's Rendering Layer to every relevant child Particle System Renderer or Mesh Renderer; assigning it only to an empty VFX parent does not select the visible children. Soft particles and distortion have access to URP's depth and opaque-color textures.

Opaque selection is captured before normal transparency rendering, so unselected particles cannot contaminate selected opaque colors. Only selected transparent renderers participate in the source's transparency blending; unselected transparent foregrounds do not attenuate bloom, and refractive shaders can include background color from the camera opaque texture. Opaque objects still occlude transparent sources. Each active group adds a transparent renderer-list draw in addition to its existing mask and blur passes.

Run **Tools > Layer Aware Bloom > Verify Transparent Sources** to check HDR particles, alpha, additive blending, layer exclusion, opaque occlusion, bloom spread, and the imported fire prefab when present. The check uses a temporary preview scene with the project's PC renderer, restores feature settings, and writes its report and captures under `Temp/`.

Tonemapping has its own C# settings, material, shader, and Render Graph pass. It uses shared SRP Core color-math functions, not Unity's post-processing passes, Volume settings, or grading LUT. The shader reference is serialized in the renderer feature so it can be included in builds.

Use one tonemapper. Leave Unity's Tonemapping override disabled when using HDR Tonemapping. If Unity post-processing is enabled, it runs before HDR Tonemapping, so Unity bloom also receives HDR values. Use HDR Color Grading mode on the URP asset for this mixed setup. For the standalone setup, camera Post Processing can remain off.

The tonemapper preserves alpha and writes a separate color texture. Neutral and ACES compress highlights and bound RGB to the SDR range. None performs no transformation or extra pass.

Currently verified: Unity 6000.3.14f1, URP 17.3, Render Graph, DX12, Linear color space, base Game/Scene View cameras, SDR output. Gamma, camera stacking, XR, MSAA, dynamic resolution, HDR-display output, and the compatibility renderer path are not yet validated. Opaque selection supports ordinary meshes; transparent selection uses the original forward material passes.
