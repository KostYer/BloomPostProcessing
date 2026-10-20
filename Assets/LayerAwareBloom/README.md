# Layer Aware Bloom

This package contains two independent URP renderer features:

- **Rendering Layer Bloom** selects opaque surfaces by Rendering Layer, builds up to six independent bloom groups, and optionally grades the combined image.
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

New slots start disabled with no Rendering Layers assigned. Existing slot settings are preserved when upgrading from three slots. Disabled slots, empty layer masks, zero intensity, zero clamp, and black tint skip mask/source/pyramid rendering. Active results are packed for the final blend, which stops sampling at the active count. Raw source previews ignore intensity, clamp, and tint. An enabled layer mask with no visible matching objects is still rendered; this optimization does not scan the scene or read back masks.

## Integration

Tonemapping has its own C# settings, material, shader, and Render Graph pass. It uses shared SRP Core color-math functions, not Unity's post-processing passes, Volume settings, or grading LUT. The shader reference is serialized in the renderer feature so it can be included in builds.

Use one tonemapper. Leave Unity's Tonemapping override disabled when using HDR Tonemapping. If Unity post-processing is enabled, it runs before HDR Tonemapping, so Unity bloom also receives HDR values. Use HDR Color Grading mode on the URP asset for this mixed setup. For the standalone setup, camera Post Processing can remain off.

The tonemapper preserves alpha and writes a separate color texture. Neutral and ACES compress highlights and bound RGB to the SDR range. None performs no transformation or extra pass.

Currently verified: Unity 6000.3.14f1, URP 17.3, Render Graph, DX12, Linear color space, base Game/Scene View cameras, SDR output. Gamma, camera stacking, XR, MSAA, dynamic resolution, HDR-display output, and the compatibility renderer path are not yet validated. Bloom currently captures ordinary opaque meshes.
