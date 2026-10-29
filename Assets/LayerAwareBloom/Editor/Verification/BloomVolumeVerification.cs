using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace LayerAwareBloom.Editor
{
    [InitializeOnLoad]
    public static class BloomVolumeVerification
    {
        const string Marker = "Temp/RunBloomVolumeVerification", Report = "Temp/BloomVolumeVerification.txt";
        static BloomVolumeVerification() => EditorApplication.delayCall += Requested;
        static void Requested()
        {
            if (!File.Exists(Marker)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += Requested; return; }
            File.Delete(Marker); Run();
        }

        public static void Run()
        {
            var feature = BloomVerificationContext.FindFeature();
            var tone = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(feature)).OfType<HdrTonemappingFeature>().Single();
            string featureJson = EditorJsonUtility.ToJson(feature), toneJson = EditorJsonUtility.ToJson(tone);
            bool featureDirty = EditorUtility.IsDirty(feature), toneDirty = EditorUtility.IsDirty(tone);
            var previousScene = SceneManager.GetActiveScene(); var previousRT = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            string temporaryScenePath = "Assets/LayerAwareBloom/Editor/Verification/VolumeCheck-"+Guid.NewGuid().ToString("N")+".unity";
            var allocations = new List<Object>(); var errors = new List<string>(); int count = 0;
            Application.LogCallback onLog = (m,s,t) => { if(t==LogType.Error || t==LogType.Exception || t==LogType.Assert) errors.Add(m); };
            Application.logMessageReceived += onLog;
            File.WriteAllText(Report,"Scene Volume GPU and blending verification\n");
            void Check(bool result, string label) { if(!result) throw new Exception(label); count++; File.AppendAllText(Report,"OK: "+label+"\n"); }
            try
            {
                feature.SettingsSource = BloomSettingsSource.SceneVolumes; feature.ShowFullscreen = false;
                tone.SettingsSource = BloomSettingsSource.SceneVolumes; tone.SetActive(true);
                var camera = new GameObject("Volume verification camera").AddComponent<Camera>(); camera.enabled=false;
                camera.transform.position = new Vector3(10000,10000,-10); camera.orthographic=true; camera.orthographicSize=2;
                camera.nearClipPlane=.1f; camera.farClipPlane=20; camera.cullingMask=1<<28;
                camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black;
                camera.allowHDR=true; camera.allowMSAA=false;
                var data=camera.GetUniversalAdditionalCameraData(); data.renderPostProcessing=false; data.volumeLayerMask=1<<30;
                data.volumeTrigger=camera.transform;
                var target=new RenderTexture(new RenderTextureDescriptor(64,64,GraphicsFormat.R16G16B16A16_SFloat,24)); target.Create(); allocations.Add(target);
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad); quad.layer=28;
                quad.transform.position=new Vector3(10000,10000,0); quad.transform.localScale=new Vector3(20,20,1);
                var mesh=quad.GetComponent<MeshRenderer>(); mesh.renderingLayerMask=2;
                var mat=new Material(Shader.Find("Universal Render Pipeline/Unlit")); allocations.Add(mat);
                mat.SetColor("_BaseColor",new Color(2,1,.25f).gamma); mesh.sharedMaterial=mat;
                Color Capture()
                {
                    Physics.SyncTransforms();
                    RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target});
                    RenderTexture.active=target;
                    var tex=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true); tex.ReadPixels(new Rect(0,0,64,64),0,0);tex.Apply();
                    var pixel=tex.GetPixel(32,32);Object.DestroyImmediate(tex);return pixel;
                }
                float Delta(Color a,Color b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b));
                var baseline=Capture(); Check(baseline.r>1.9f,"No custom Volume leaves scene HDR unchanged with camera PP off");
                var go=new GameObject("Test Volume");go.layer=30;var volume=go.AddComponent<Volume>();volume.isGlobal=true;volume.priority=10000;
                var profile=ScriptableObject.CreateInstance<VolumeProfile>();allocations.Add(profile);volume.sharedProfile=profile;
                var bloom=profile.Add<LayerBloomVolume>(true);allocations.Add(bloom);
                var mapping=profile.Add<LayerTonemappingVolume>(true);allocations.Add(mapping);
                for(int i=0;i<6;i++){var g=bloom.GetGroup(i);g.enabled.value=i==5;g.renderingLayers.value=2;g.threshold.value=0;g.intensity.value=1;}
                Check(bloom.parameters.Count==84,"All six nested groups and grading parameters discovered by Volume framework");
                var firstBloom = Capture(); var evaluated = VolumeManager.instance.stack.GetComponent<LayerBloomVolume>(); File.AppendAllText(Report, $"Diagnostic baseline {baseline}, output {firstBloom}, intensity {evaluated.group6.intensity.value}, mask {(uint)evaluated.group6.renderingLayers.value}, enabled {evaluated.group6.enabled.value}, bloom {evaluated.enableBloom.value}, source {feature.SettingsSource}\n");
                Check(Delta(firstBloom,baseline*2)<.04f,"Scene Volume slot 6 drives bloom with post-processing disabled");
                data.volumeLayerMask=1<<29;
                Check(Delta(Capture(),baseline)<.005f,"Camera Volume layer mask excludes other scene settings");
                data.volumeLayerMask=1<<30;
                Check(Delta(Capture(),baseline*2)<.04f,"Returning to first camera mask restores its settings without stale state");
                volume.weight=.5f;
                var blended = Capture();
                Check(Mathf.Abs(VolumeManager.instance.stack.GetComponent<LayerBloomVolume>().group6.intensity.value-.5f)<.001f && blended.r>baseline.r && blended.r<firstBloom.r,"Volume weight interpolates intensity and other numeric settings");
                volume.weight=0;
                Check(Delta(Capture(),baseline)<.005f,"Weight zero bypasses bloom");
                volume.weight=1;bloom.active=false;
                Check(Delta(Capture(),baseline)<.005f,"Disabling Bloom override bypasses effect");
                bloom.active=true;volume.enabled=false;
                Check(Delta(Capture(),baseline)<.005f,"Disabling Volume clears camera settings");
                volume.enabled=true;
                // Override checkbox must allow lower-priority/default intensity through.
                bloom.group6.intensity.overrideState=false;
                Check(Delta(Capture(),baseline)<.005f,"Unchecked intensity uses default zero");
                bloom.group6.intensity.overrideState=true;
                var highGO=new GameObject("Higher priority Volume");highGO.layer=30;
                var high=highGO.AddComponent<Volume>();high.isGlobal=true;high.priority=10001;
                var highProfile=ScriptableObject.CreateInstance<VolumeProfile>();allocations.Add(highProfile);high.sharedProfile=highProfile;
                var highBloom=highProfile.Add<LayerBloomVolume>();allocations.Add(highBloom);highBloom.group6.intensity.Override(2);
                Check(Delta(Capture(),baseline*3)<.06f,"Higher-priority Volume overrides only its selected parameter");
                high.enabled=false;
                volume.isGlobal=false;
                var box=go.AddComponent<BoxCollider>();box.size=Vector3.one*4;volume.UpdateColliders();go.transform.position=camera.transform.position;
                Check(Delta(Capture(),baseline*2)<.04f,"Local Volume applies inside collider");
                go.transform.position=Vector3.zero;
                Check(Delta(Capture(),baseline)<.005f,"Local Volume outside trigger contributes nothing");
                volume.isGlobal=true;
                foreach(var mode in new[]{HdrTonemappingMode.Neutral,HdrTonemappingMode.ACES})
                {
                    mapping.mode.value=mode;var mapped=Capture();
                    Check(mapped.r>0 && mapped.r<=1 && mapped.g<=1 && mapped.b<=1,$"Independent {mode} override maps scene after bloom");
                }
                mapping.mode.value=HdrTonemappingMode.None;
                Check(Capture().r>3.8f,"Tonemapping None restores HDR bloom output");
                bloom.enableBloom.value=false;bloom.enableColorGrading.value=true;bloom.exposure.value=1;
                Check(Delta(Capture(),baseline*2)<.02f,"Scene grading works without bloom");
                // Unity requires the first additive scene to have a path before opening another.
                if(!EditorSceneManager.SaveScene(scene,temporaryScenePath)) throw new Exception("Could not save temporary verification scene");
                // Move only the Volume to another loaded scene, then unload it.
                var volumeScene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                SceneManager.MoveGameObjectToScene(go,volumeScene);
                EditorSceneManager.CloseScene(volumeScene,true);
                Check(Delta(Capture(),baseline)<.005f,"Unloading the Volume scene removes settings");
                // The development sample animates a private scene profile, never the shared asset.
                var controllerType=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("BloomCycleDemo")).FirstOrDefault(t=>t!=null);
                if(controllerType!=null)
                {
                    SceneManager.SetActiveScene(scene);
                    var sampleGO=new GameObject("Sample cycle verification");
                    var sampleVolume=sampleGO.AddComponent<Volume>();sampleVolume.isGlobal=true;
                    var sampleProfile=ScriptableObject.CreateInstance<VolumeProfile>();allocations.Add(sampleProfile);
                    var sampleBloom=sampleProfile.Add<LayerBloomVolume>(true);allocations.Add(sampleBloom);
                    sampleBloom.group1.renderingLayers.value=2;sampleBloom.group1.intensity.value=1;
                    sampleVolume.sharedProfile=sampleProfile;
                    var controller=sampleGO.AddComponent(controllerType);
                    var binding=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
                    controllerType.GetField("_bloomFeature",binding).SetValue(controller,feature);
                    controllerType.GetMethod("StartBloomCycle",binding).Invoke(controller,null);
                    sampleVolume.profile.TryGet<LayerBloomVolume>(out var instanceBloom);
                    Check(!instanceBloom.group1.enabled.value,"Sample cycling changes scene-instance override");
                    Check(sampleBloom.group1.enabled.value,"Sample cycling leaves shared profile unchanged");
                    controllerType.GetMethod("StopBloomCycle",binding).Invoke(controller,null);
                    Check(instanceBloom.group1.enabled.value,"Stopping sample restores scene-instance settings");
                }
                Check(!ShaderUtil.ShaderHasError(Shader.Find("Hidden/LayerAwareBloom/RenderingLayerBloom")),"Bloom shader valid");
                Check(errors.Count==0,"No rendering errors: "+string.Join(" | ",errors));
                File.AppendAllText(Report,$"PASS: {count} checks\n");
            }
            catch(Exception e){File.AppendAllText(Report,"FAIL: "+e+"\n");}
            finally
            {
                EditorJsonUtility.FromJsonOverwrite(featureJson,feature);feature.Create();
                EditorJsonUtility.FromJsonOverwrite(toneJson,tone);tone.Create();
                if(!featureDirty)EditorUtility.ClearDirty(feature);if(!toneDirty)EditorUtility.ClearDirty(tone);
                RenderTexture.active=previousRT;SceneManager.SetActiveScene(previousScene);EditorSceneManager.CloseScene(scene,true);
                foreach(var item in allocations) if(item!=null)Object.DestroyImmediate(item);
                if(File.Exists(temporaryScenePath)) AssetDatabase.DeleteAsset(temporaryScenePath);
                Application.logMessageReceived-=onLog;SceneView.RepaintAll();
            }
        }
    }
}

