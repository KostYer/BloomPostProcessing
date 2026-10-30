# Release preparation

Use **Tools > Layer Aware Bloom > Release > Export Release Candidate**. The export uses an explicit file allowlist and rejects dependencies on excluded Assets. It includes Runtime, Shaders, Editor, offline documentation/notices, `Samples/Scenes/GettingStarted.unity`, the `GettingStarted` subfolders under Materials, Scripts, and Settings/Rendering, `Samples/Settings/Volumes/GettingStarted_LayerAwareBloom.asset`, and the shared `Samples/Materials/Sky/ProceduralSky.mat` used by the scene. It excludes the development demo scenes/art/fonts/VFX, project settings, recovery scenes, and generated build files. Unity Registry dependencies must be declared in the Publisher Portal/package metadata for the chosen delivery format; no UPM package manifest is claimed for this .unitypackage export.

Validation tools are explicit menu actions; they do not run automatically merely because a customer imports the package. Marker-triggered checks under Temp are for development automation only. Tests discover the active URP asset's default renderer. Test scenes/settings are restored afterward.

Before publication:

1. Import the exported candidate into a fresh project with the stated dependencies; run the sample and editor verification.
2. Build and run a Windows standalone player to check shader inclusion and runtime behavior. Keep the reports with the exact candidate used.
3. Choose sample quality settings using [Performance and Quality](Performance.md), including appropriate group counts, downscale, and filtering quality.
4. Review the rendering requirements, known limitations, third-party notices and any applicable submission licensing requirements. Include the Unity-derived shader disclosure in the listing.
5. Fill publisher identity, support contact, price, and final listing details. Review AI assistance disclosure, screenshots and video before submitting.
6. Run the current Asset Store submission validator and submit through the Publisher Portal only after publisher review.

Suggested AI description: "OpenAI Codex assisted with C# and shader implementation, custom inspectors, verification tooling, documentation and release preparation. The implementation was adapted for Rendering Layer selection, independently controlled bloom groups, transparency and uGUI capture, and tested through editor rendering checks and the recorded build validation. Unity-derived bloom filtering is identified separately in Third-Party Notices."

Update this description to match the final delivered contents and validation results. No publication, price selection, external upload, or support-account setup is performed by these tools.
