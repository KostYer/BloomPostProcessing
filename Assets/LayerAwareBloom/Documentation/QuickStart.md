# Quick start

## Requirements

Use Unity 6000.3.14f1 with URP 17.3.0, the Universal Renderer, Render Graph, Linear color space, HDR enabled, and SDR display output. The initial validation target is Windows/DX12. Install URP and uGUI through Unity Package Manager before importing the package. Unity Collections and SRP Core are URP dependencies. The package does not install or modify dependencies automatically.

## Add selective bloom to your project

1. Select the URP asset used by your current Quality level (or Graphics settings when there is no Quality override). Enable HDR.
2. Open that asset's Universal Renderer. Select **Add Renderer Feature > Rendering Layer Bloom**.
3. Enable **Allow HDR** on the base camera. Enable camera Post Processing for Unity Tonemapping.
4. Leave the feature's Settings Source at **Scene Volumes**. Add a Global Volume in the scene, create a profile, and add **Layer Aware Bloom / Bloom**. Expand Group 1, check the override boxes for Rendering Layers and Intensity, select a layer, and use intensity 1. Threshold 0.9, scatter 0.7, and Half downscale are starting defaults.
5. Add the selected Rendering Layer to your object's Renderer. Keep the layers needed for its lighting. For a VFX hierarchy, use **Rendering Layer Assigner > Apply to Root and Children**.
6. Use a bright HDR emissive material. Threshold is applied to the rendered brightness, not just a material's emission field.
7. Unity **Tonemapping** is added automatically with **Neutral** mode. Existing settings are preserved; you can switch to ACES, disable it, or remove it. Initial setup enables camera Post Processing and HDR color grading.

Use a separate profile in each scene. Without an active matching Volume, these effects are off. For an existing setup, the renderer feature's **Create Scene Volume from Existing Renderer Settings** button copies its six groups and grading into a scene profile and adds Unity Tonemapping. Save the scene afterward. Camera Volume Layer Mask, Volume weight, priority, and local colliders work normally. Bloom works with camera Post Processing off; Unity Tonemapping requires it on. Additively loaded Global Volumes can affect the same camera unless separated by its Volume Layer Mask.

Groups own sources in inspector order: the first matching active group wins. Disabled or zero-output groups do not claim objects. Later groups can independently control other sources.

## Optional additions and reset

Expand **Optional Additions** to expose Soft Knee, Radius, and Stop NaN / Infinity. Defaults (0.5, 1, off) reproduce the original Unity-style filtering. Collapsing the foldout hides controls without disabling their effects. In a Volume, **Reset Group to Defaults** clears that group's overrides and returns its mask/intensity to zero. The legacy Renderer Asset inspector retains its original defaults and reset behavior. Undo restores the previous settings.

## Getting Started sample

Open `Samples/GettingStarted/GettingStarted.unity`. Assign its `DemoPipeline.asset` in a disposable project's Graphics settings and clear or replace any Quality pipeline override. The cyan and amber cubes use separate bloom groups; the white cube is unselected. The sample uses Unity primitives and URP Unlit materials and needs no imported artwork, font, model, or VFX pack. Its smoke-check component is inert unless explicitly launched with the release-test command-line flag.

## UI

Add **UI Bloom Source** to an Image, RawImage, legacy Text, or parent Canvas. Match a bloom group's Rendering Layers and use Emission Multiplier to raise bloom-source brightness. Use Screen Space Camera with its camera assigned, or World Space. Overlay canvases, TextMeshPro/SDF selection, UI Toolkit, and custom UI shader capture are unsupported. See README for masking and transparency details.

## Setup warnings

The bloom inspector warns about missing HDR, missing shader references, empty enabled layer masks, and unsupported Gamma color space. UI Bloom Source warns about unsupported UI configurations.

When a rendered camera has both Unity's Volume Bloom and this feature active, an inspector warning and a Console warning explain the combined effect. The check uses the camera's evaluated Volume stack and Post Processing state. Disabled overrides, intensity zero, zero-weight/excluded Volumes, and local Volumes outside the camera's influence do not count. The Console warns once per continuous conflict, not every frame. Turning off one effect clears the conflict; settings are never changed automatically. Camera diagnostics appear after that camera renders and expire when it stops rendering. These diagnostics are editor-only.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Nothing glows | Active renderer feature, group enabled, nonempty mask, HDR, positive intensity/clamp, nonblack tint, source above threshold. |
| Everything glows | Unity Bloom may also be enabled, or the group selects the Default layer used by many objects. |
| One object misses a later group | First active matching group owns it. |
| UI does not glow | Supported canvas mode, assigned camera, supported Graphic/material, and matching UI Bloom Source layers. |
| VFX parent is selected but particles do not glow | Assign layers to child renderers, not just the parent GameObject. |
| Lights stop affecting an object | Restore its original lighting Rendering Layers as well as the bloom selection layer. |
| Glow differs in a build | Confirm all feature shader references are serialized and check the Console/player log for missing shader variants. |

For diagnostics run **Tools > Layer Aware Bloom > Validate Setup**. This runs all validation groups and reports specific failures in the Console. The checks use the active URP asset's default renderer and require an enabled Rendering Layer Bloom feature. The optional imported-fire check runs only if that development asset is present.
