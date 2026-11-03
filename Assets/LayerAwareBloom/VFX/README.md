# FireBall

`FireBall.prefab` is a standalone copy of the FireBall effect used by `Samples/Scenes/BloomSources.unity`.

Keep this VFX folder together when copying the effect:

- `Materials/FireBall.mat` and `Materials/Embers.mat`
- `Textures/Fireball.tif` and `Textures/FireBallEmission.tif`

The materials and textures have their own asset GUIDs. The prefab does not inherit from or reference `Assets/UnityTechnologies/ParticlePack`. It uses Unity particle systems, Unity's built-in particle texture for the embers, and the Universal Render Pipeline particle unlit shader. URP is still required. No Particle Pack scripts, assemblies, or other effects are required.

The prefab GUID, particle settings, texture importer settings, and material appearance are preserved, so the existing BloomSources scene instance continues to use this effect.

Source artwork and particle effect: Unity Technologies Particle Pack. Making the assets self-contained does not change their original licensing.
