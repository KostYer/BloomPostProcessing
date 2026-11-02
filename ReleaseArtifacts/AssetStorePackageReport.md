# Layer Aware Bloom 1.0.0 package

Prepared 2026-11-02. Local submission candidate; no upload or publication performed.

[Unity package](LayerAwareBloom-1.0.0.unitypackage): 4,333,799 bytes, 138 files.

SHA-256: `b1cceeac2778e3ad4976f828558079e574ce7b1651e1a12b78ddc73a94c1c0a6`. [Checksum file](LayerAwareBloom-1.0.0.sha256).

## Included

Runtime, shaders, custom inspectors and verification tools; offline documentation, changelog and third-party notices; GettingStarted, BloomGroups and BloomSources scenes with their dependencies. Native Unity dependency metadata declares URP 17.3.0, uGUI 2.0.0 and Input System 1.19.0.

All content is under `Assets/LayerAwareBloom/`. Bundled TMP and fire VFX resources were moved into `Samples/ThirdParty/`, preserving all 75 moved resource GUIDs. The four general sample scripts now use `LayerAwareBloom.Samples`, with MovedFrom attributes for existing components. Verification paths and sample-type lookup were updated. The exporter now writes the versioned 1.0.0 filename.

ProjectSettings, development/template assets, recovery scenes, generated files, Asset Store Tools and disposable packaging automation are excluded. [Contents](LayerAwareBloom-1.0.0-Contents.txt).

## Technical verification

Baseline: Unity 6000.3.14f1, Windows, Direct3D 12, Linear, URP Render Graph. The clean project resolved the declared dependency versions listed above. Registry resolution can select newer transitive dependencies.

- Archive opens; all 138 assets and metadata match the current source byte for byte; GUIDs are unique; one package root. [Archive audit](LayerAwareBloom-1.0.0-ArchiveAudit.json).
- Unity native import into an isolated project succeeded. The exact final archive was imported again before validation; Unity exited with code 0.
- All three sample scenes open with no missing scripts and select their dedicated pipelines. [Sample report](LayerAwareBloom-1.0.0-Validation/Samples.txt).
- The actual GPU/editor checks passed all five groups: camera Bloom conflicts, scene Volumes, transparent sources, uGUI capture/masks and optional filtering additions. The historical pre-additions shader comparison was skipped because its development snapshot is intentionally excluded. [Rendering report](LayerAwareBloom-1.0.0-Validation/BloomSetupValidation.txt).
- Asset Store Tools 12.0.6, UnityPackage validation, Tools/Particles & Effects: **34 passed, 3 advisory warnings, 0 failed**. Script and shader compilation passed. [Full validator output](LayerAwareBloom-1.0.0-Validation/AssetStoreValidator.txt).
- No new standalone player build, platform expansion or performance benchmark was run for this archive. Historical candidate reports apply only to their named candidates.

## Validator warnings

1. WireframeCube.fbx has no associated prefab. It is used directly in the sample scene.
2. SRP compatibility classification flags the procedural sky, TMP fonts/shaders/materials, optional TMP HDRP graphs, and the native UI bloom shader. Shader compilation passed, and the supported UI/transparent GPU integration checks passed. These results do not establish compatibility for every bundled TMP shader or every showcase material.
3. Static fields/events require lifecycle review in the renderer, setup diagnostics and verification guard. Keep this warning visible in publisher review; the recorded rendering checks are not a domain-reload-disabled lifecycle test.

## Before submitting

- The publisher identified the Arial source as https://font.download/font/arial and confirmed that the fire VFX came from a free Asset Store download. These establish provenance, but do not establish permission to redistribute either component in this package. Resolve the specific licensing points below and review the other sample-art provenance.
- Review Unity-derived shader licensing, the bundled third-party notices, the advisory validator warnings and the supported-version/platform claims.
- Complete publisher/support information, price, final screenshots/video and AI disclosure. [Listing draft](StoreListingDraft.md).
- Submit through the Publisher Portal after publisher review. Unity approval remains separate from these local technical checks.

## Source licensing follow-up

Checked 2026-11-02 after the publisher supplied the sources. The archive is unchanged, so its checksum and technical validation remain applicable.

The publisher confirmed that the fire is one effect from Vefects' Free Fire VFX - URP, Asset Store product 266226. The exported fire content consists of `VFX_Fire_Floor_01.prefab` and its dependencies, including the point-light prefab, referenced materials, shaders, textures and audio. This clarification identifies the exact source and limited scope of inclusion; the redistribution question below still applies to that effect.

- **Arial atlas:** [Font.download](https://font.download/font/arial) labels the font free for commercial use, but its detailed license identifies it as a Microsoft-supplied font and limits embedding to the applicable license permissions. That page does not provide a clear grant to redistribute a reusable TMP/SDF font atlas. [Microsoft's font redistribution FAQ](https://learn.microsoft.com/en-us/typography/fonts/font-faq) distinguishes rendered text images from reusable bitmap fonts; for Windows-supplied fonts it explicitly says conversion to a bitmap font does not permit inclusion in an app/game. The FAQ covers Windows-supplied fonts, not every separately licensed Arial distribution. Obtain applicable atlas redistribution rights or replace this sample font with the already bundled Liberation Sans under its included OFL terms.
- **Free Fire VFX URP:** [Vefects' listing](https://assetstore.unity.com/packages/vfx/particles/fire-explosions/free-fire-vfx-urp-266226) identifies the Standard Unity Asset Store EULA. Its free price does not add source redistribution rights. [Unity's EULA](https://unity.com/legal/as-terms), sections 2.2.1 and 2.2.1.1, distinguishes embedding in a licensed product from other distribution. The asset was not found on [Unity's publisher-demo permission list](https://assetstore.unity.com/lists/free-to-use-assets-for-publisher-demos-243675) when checked. Obtain express permission from Vefects for inclusion in a distributable Asset Store demo, or exclude its source and let customers obtain the optional effect separately. A built game/video demonstration is a different distribution format from this editable Unity package.

Current guidance: [Unity submission guidelines](https://assetstore.unity.com/publishing/submission-guidelines), especially sections 1.2, 1.6 and 2.1; [validation and upload workflow](https://docs.unity.com/en-us/asset-store/publishing/asset-packages/upload). Free Fire VFX URP is listed by [Vefects](https://assetstore.unity.com/packages/vfx/particles/fire-explosions/free-fire-vfx-urp-266226); its public free price alone does not document permission to redistribute its source in another Asset Store package.

Re-export with **Tools > Layer Aware Bloom > Release > Export Release Candidate** after modifying shipped files; rerun verification against the resulting archive. The output is `ReleaseArtifacts/LayerAwareBloom-1.0.0.unitypackage`.
