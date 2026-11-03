# Layer Aware Bloom 1.0.0 package

Prepared 2026-11-03. Local Asset Store submission candidate; no upload or publication performed.

[Unity package](LayerAwareBloom-1.0.0.unitypackage): 4,214,191 bytes, 98 assets.

SHA-256: `ace4a8dc9d3ff98b76b08fb051814265b5b286b3aaec8d9aebab479f4b5b8a99`. [Checksum file](LayerAwareBloom-1.0.0.sha256).

## Included

Runtime, shaders, inspectors and explicit verification tools; offline documentation, changelog and third-party notices; GettingStarted, BloomGroups and BloomSources with their dependencies. All files are under `Assets/LayerAwareBloom/`. Native Package Manager metadata declares URP 17.3.0, uGUI 2.0.0 and Input System 1.19.0.

The sample font is Liberation Sans 2.1.5, with its unmodified TTF and SIL OFL 1.1 license. Its dynamic SDF atlas was regenerated while preserving the font, material and atlas asset identities. The old 1.05 font and its GPL license are excluded. FireBall and its two materials/two textures are self-contained under VFX/, with a provenance note. No Vefects Free Fire source is included.

The VFX export now includes its README. The transparency fixture uses FireBall, centers its authored offset within the test camera and checks its two materials. The native Simple Lit reference is warmed up before comparison: the first DX12 editor draw of a newly created material produced an invalid reference even with the bloom feature disabled; subsequent native renders agreed. This adjusts the test fixture and does not claim to fix native first-use rendering.

ProjectSettings, Unity template/development assets, recovery scenes, generated files, Asset Store Tools and disposable automation are excluded. [Contents](LayerAwareBloom-1.0.0-Contents.txt).

## Verification

The package was re-exported after the documentation wording update. The current archive audit passed, including byte-for-byte source/meta comparison and dependency metadata. The clean-import, rendering, validator and player results below describe the previous candidate (668faaf7e74652807bd0660168cfd43d0e195d5e518dfd69a5d724b6fa935c6e); those checks were not rerun for this archive.

Unity 6000.3.14f1, Windows, Direct3D 12, Linear, URP Render Graph. The isolated project uses the declared dependency versions and normal Unity template built-in modules. Registry resolution can choose newer transitive dependencies.

- Archive audit passed: every asset and metadata file matches the current source byte for byte; unique GUIDs and one root. [Audit](LayerAwareBloom-1.0.0-ArchiveAudit.json).
- Native import of the previous candidate into the isolated project succeeded, exit code 0. The archive was reimported after the final fixture update.
- All three scenes opened with no missing scripts and selected their dedicated pipelines. [Samples](LayerAwareBloom-1.0.0-Validation/Samples.txt).
- All five rendering validation groups passed: camera Bloom conflicts, scene Volumes, transparent sources including FireBall, uGUI/masks and optional filtering controls. The development-only historical shader comparison is explicitly skipped. [Rendering report](LayerAwareBloom-1.0.0-Validation/BloomSetupValidation.txt).
- Asset Store Tools 12.0.6, UnityPackage, Tools/Particles & Effects: 34 passed, 3 advisory warnings, 0 failed. Script/shader compilation passed. [Validator report](LayerAwareBloom-1.0.0-Validation/AssetStoreValidator.txt).
- Windows standalone player containing all three demos built successfully: 0 errors, 0 warnings. [Build report](LayerAwareBloom-1.0.0-Validation/Build.txt).
- Direct3D 12 player smoke passed: finite, nonzero bloom, both filtering quality modes, optional additions, disabled groups and shader inclusion. [Player report](LayerAwareBloom-1.0.0-Validation/PlayerSmokeD3D12.txt), [bloom preview](LayerAwareBloom-1.0.0-Validation/PlayerSmokeD3D12.png).

## Validator advisories

1. Mesh prefab association: WireframeCube.fbx is used directly by a demo scene.
2. SRP shader classification: this advisory remains visible; compilation and supported transparency/UI rendering checks passed. It does not establish support for every shader configuration.
3. Static fields/events: publisher lifecycle review remains appropriate, including disabled domain reload; the recorded rendering checks do not establish that lifecycle configuration.

## Before submission

- Review final publisher identity, support contact, pricing, screenshots/video, AI disclosure and supported-version/platform selections. [Listing draft](StoreListingDraft.md).
- Review sample-art provenance and the notices. [Unity's publisher-demo list](https://assetstore.unity.com/lists/free-to-use-assets-for-publisher-demos-243675) includes Particle Pack and permits used demonstration content (checked 2026-11-03). The package includes only FireBall and its used resources. [Font release](https://github.com/liberationfonts/liberation-fonts/releases/tag/2.1.5).
- Review Unity-derived shader disclosures and the advisory warnings against [Unity's submission guidelines](https://assetstore.unity.com/publishing/submission-guidelines).
- Submit through the Publisher Portal after publisher review. Local validation is separate from Unity's acceptance review.

No platform expansion or performance benchmark was performed. Earlier candidate reports apply only to their identified archives. Re-export after any shipped changes through **Tools > Layer Aware Bloom > Release > Export Release Candidate**, then rerun validation and regenerate the contents, audit and checksum.
