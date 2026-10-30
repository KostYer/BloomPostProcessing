# Performance and quality

The main cost controls are active bloom groups, bloom resolution, filtering quality, and source complexity. Use the settings below to balance the desired look with your project's performance budget.

## Choosing settings

| Setting | Lower cost | Higher visual quality or control |
| --- | --- | --- |
| Active groups | Use one group where objects can share settings. | Add groups only when sources need independently controlled glow. |
| Downscale | Quarter reduces the blur resolution. | Half preserves finer detail. |
| High Quality Filtering | Off uses fewer texture samples. | On improves filtering and reconstruction at additional sampling cost. |
| Max Iterations | Start around 2–4 to reduce blur passes. | 6–8 allows broader glow; more iterations are useful only when the intended look needs that spread. |
| Transparent particles and UI | Reduce overlapping particles and simplify UI masks. | Complex effects and nested masks add capture and drawing work. |
| Stop NaN / Infinity | Leave off unless invalid source pixels are a problem. | Enable for source cleanup when needed; each protected group adds a full-resolution HDR texture and pass. |

For a lower-cost starting point, use one group, Quarter downscale, High Quality Filtering off, and 2–4 iterations. For higher quality, start with Half downscale and 6 iterations, enable High Quality Filtering, and increase iterations toward 8 only if a broader glow improves the result. Keep HDR enabled and tune source emission, Threshold, Soft Knee, Scatter, Radius, and Intensity for the desired appearance.

## What actually reduces work

- **Reduce active groups before reducing selected layer bits.** Several Rendering Layers can share one bloom group. Separate groups each require their own capture and blur work. More groups provide independent control rather than inherently better image quality.
- **Avoid unnecessary overlap between groups.** The first matching active group owns a renderer. Renderers matching later groups can introduce additional CPU culling to enforce that ownership.
- **Use Quarter downscale when fine detail allows it.** It processes roughly one-quarter as many pixels per corresponding blur level as Half. This does not make the whole effect four times faster: source capture and compositing still contribute.
- **Disable unused groups.** Raising Threshold or slightly lowering Intensity changes appearance without removing the rendering passes. An active group can still run even when nothing visibly glows. Zero-output groups are skipped during normal rendering.
- **Treat iterations and sampling quality as separate controls.** Max Iterations caps the number of blur pyramid levels; High Quality Filtering changes texture sampling. There is no separate adjustable sample-count setting.
- **Tune appearance without assuming every slider changes cost.** Radius changes sample spacing without adding samples. Scatter, Soft Knee, and nonzero Intensity mainly adjust appearance.
- **Limit expensive sources and extra cameras.** Transparent overlap, complex source materials, large UI hierarchies, nested masks, and additional cameras using bloom increase work. Keep Unity's separate Bloom disabled unless the combined effect is intentional.

## Platform considerations

- **Desktop:** start with Half downscale and 4–6 iterations, then enable High Quality Filtering where its improvement is visible and fits the frame budget.
- **Mobile and lower-end GPUs:** start with one group, Quarter downscale, High Quality Filtering off, and 2–4 iterations. Pay particular attention to transparent overlap, UI capture, and full-resolution passes. Unity also recommends disabling High Quality Filtering for mobile bloom; see [Unity's post-processing guidance](https://docs.unity.com/en-us/engine/6000.3/manual/post-processing-and-full-screen-effects/urp/integration-with-post-processing).
- **XR/VR:** currently unvalidated, as are MSAA, dynamic resolution, and camera stacking. Treat these as validation work before choosing quality settings.

