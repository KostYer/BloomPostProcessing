# Pre-profiling readiness report

Validated 2026-10-28. Candidate: `LayerAwareBloom-1.0.0-rc1.unitypackage` (74,147 bytes).

SHA-256: `05bd40df57c3ba6d67e54fa00bb4a4704c7b61042921229c28772319b0e5502f`

## Completed

- Added camera-specific Unity Bloom conflict diagnostics to the inspector and Console. Uses evaluated Volume blending, camera post-processing and this feature's enabled/output state. Logs only when a conflict begins. Does not modify either effect. Diagnostics are excluded from player compilation.
- Added inspector setup checks for HDR, shader references, empty group masks, zero-output groups and Gamma color space.
- Isolated runtime, editor, development sample and Getting Started sample assemblies. The player contains the runtime and Getting Started assemblies; the editor assembly is excluded.
- Added Quick Start, compatibility/limitations, changelog, third-party notices and offline Unity license texts, plus a store listing draft.
- Added a primitive-based demo and an export command with an explicit allowlist and a check for dependencies on excluded Assets.
- Export contains 40 files, each byte-checked against the workspace. Development artwork, VFX, fonts, existing demo scenes, ProjectSettings and temporary automation are excluded.
- Removed two shader compiler warnings by making sampling/compositing control flow explicit. Filtering mathematics is unchanged; GPU comparison against the pre-additions shader passed for both filtering quality modes, preview/compositing with zero through six groups, and all four default filter passes (comparison tolerance 0.000001).

## Verification

Baseline: Unity 6000.3.14f1, URP 17.3.0, uGUI 2.0.0, Windows, Linear, Render Graph, Direct3D 12.

| Check | Result | Evidence |
| --- | --- | --- |
| Fresh project import with only Unity Registry dependencies | PASS | Local `CleanImport.log` and final `CleanReimport.log` |
| Default behavior, full-group reset, optional controls and shader comparison in the development project | PASS | [GPU and serialization report](BloomAdditionsVerification.txt) |
| Clean-project optional controls and integration | PASS | [Clean additions report](CleanBloomAdditionsVerification.txt) |
| Per-camera conflict, repeated-frame suppression, inactive/zero-weight/excluded/local Volumes, disabled camera post-processing and disabled custom bloom | PASS | [Clean warning report](CleanBloomConflictVerification.txt) |
| Supported UI and masks | PASS | [Clean UI report](CleanBloomUIVerification.txt) |
| Transparent sources and group ownership | PASS | [Clean transparency report](CleanBloomTransparencyVerification.txt) |
| Windows standalone build | PASS, 0 errors and 0 warnings | [Build report](CleanBuild.txt) |
| Final DX12 player: shader inclusion, finite nonzero bloom, both quality modes, optional additions and disabled groups | PASS, exit code 0 | [Player report](PlayerSmokeD3D12.txt), [bloom preview capture](PlayerSmokeD3D12.png) |

The clean-project comparison intentionally skips the historical shader snapshot, which is a development-only file. That comparison ran in the original project; the actual rendering/integration checks ran in both projects.

An earlier development candidate passed a DX11 player smoke test. The final validation build includes DX12 only; forcing DX11 on that binary fails initialization because DX11 shaders are excluded. This is not counted as final DX11 coverage. Other API/platform support remains unclaimed.

The disposable validation project and built player are under `Temp/ReleaseValidation`. Unity may remove Temp; durable reports and the export are retained here. Machine-specific raw logs and generated packages are gitignored. No original project pipeline settings were changed by the clean-project build.

## Still required before publication

1. Profile representative scenes and record GPU time, memory and scaling with group count, UI and optional protection. No performance claims have been made.
2. Expand the compatibility matrix only for platforms/features actually tested; current limitations are in `Assets/LayerAwareBloom/Documentation/Compatibility.md`.
3. Review the store listing, publisher/support details, pricing, final marketing screenshots/video, Unity-derived code/license disclosures and AI disclosure.
4. Run the current Asset Store submission validator and complete publisher review before upload/submission. Neither validation through the Publisher Portal nor publication was performed.

Export again through **Tools > Layer Aware Bloom > Release > Export Release Candidate** after any shipped content changes. This report and hash apply to the candidate identified above.
