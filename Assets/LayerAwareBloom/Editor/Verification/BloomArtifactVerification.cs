using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using LayerAwareBloom;

[InitializeOnLoad]
public static class BloomArtifactVerification
{
    static BloomArtifactVerification(){EditorApplication.delayCall+=Run;}
    static Color[] Read(RenderTexture rt)
    {
        RenderTexture.active=rt;
        var t=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
        t.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);t.Apply();
        var c=t.GetPixels();UnityEngine.Object.DestroyImmediate(t);return c;
    }
    static void Save(string path,Color[] pixels,int w,int h,bool linear)
    {
        var t=new Texture2D(w,h,TextureFormat.RGBA32,false,true);
        t.SetPixels(pixels.Select(c=>{var p=linear?c.gamma:c;p.a=1;return p;}).ToArray());t.Apply();
        File.WriteAllBytes(path,t.EncodeToPNG());UnityEngine.Object.DestroyImmediate(t);
    }
    static void Run()
    {
        const string marker="Temp/RunBloomArtifactVerification";
        if(!File.Exists(marker))return;
        if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=Run;return;}
        string label=File.ReadAllText(marker).Trim();File.Delete(marker);
        string report="Temp/BloomArtifact-"+label+".txt";
        var previous=RenderTexture.active;
        try
        {
            var camera=Camera.main;
            var rt=new RenderTexture(new RenderTextureDescriptor(1280,720,GraphicsFormat.R16G16B16A16_SFloat,24));rt.Create();
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest{destination=rt});
                Save("Temp/BloomScene-"+label+".png",Read(rt),rt.width,rt.height,true);
            }
            finally{RenderTexture.active=previous;UnityEngine.Object.DestroyImmediate(rt);}
            // Actual tone shader, identical smooth yellow HDR input, only output storage differs.
            const int w=1024,h=256;
            var input=new Texture2D(w,h,TextureFormat.RGBAFloat,false,true);input.filterMode=FilterMode.Point;
            var pixels=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {float v=.02f+2f*x/(w-1);pixels[y*w+x]=new Color(v,v*.85f,.04f,.37f);}
            input.SetPixels(pixels);input.Apply();
            var mat=new Material(Shader.Find("Hidden/LayerAwareBloom/HdrTonemapping"));
            var formats=new[]{GraphicsFormat.B10G11R11_UFloatPack32,GraphicsFormat.R16G16B16A16_SFloat,GraphicsFormat.R32G32B32A32_SFloat};
            File.WriteAllText(report,"Scene captured; synthetic actual-shader tone precision test\n");
            try
            {
                foreach(int mode in new[]{1,2})
                {
                    var outputs=new Color[3][];
                    for(int i=0;i<formats.Length;i++)
                    {
                        var target=new RenderTexture(new RenderTextureDescriptor(w,h,formats[i],0));target.Create();
                        var cmd=new CommandBuffer();
                        try
                        {
                            var p=new MaterialPropertyBlock();p.SetTexture("_BlitTexture",input);
                            p.SetVector("_BlitScaleBias",new Vector4(1,1,0,0));p.SetFloat("_LayerTonemappingMode",mode);
                            cmd.SetRenderTarget(target);cmd.SetViewport(new Rect(0,0,w,h));
                            cmd.DrawProcedural(Matrix4x4.identity,mat,0,MeshTopology.Triangles,3,1,p);Graphics.ExecuteCommandBuffer(cmd);
                            outputs[i]=Read(target);
                            Save($"Temp/ToneGradient-{mode}-{i}.png",outputs[i],w,h,true);
                        }
                        finally{RenderTexture.active=previous;cmd.Release();UnityEngine.Object.DestroyImmediate(target);}
                    }
                    for(int i=0;i<2;i++)
                    {
                        float max=outputs[i].Zip(outputs[2],(a,b)=>Mathf.Max(Mathf.Abs(a.r-b.r),Mathf.Abs(a.g-b.g),Mathf.Abs(a.b-b.b))).Max();
                        int distinct=outputs[i].Take(w).Select(c=>c.r).Distinct().Count();
                        File.AppendAllText(report,$"Mode {mode}, {formats[i]}: max RGB error {max}, distinct red values {distinct}/{w}\n");
                    }
                    if(outputs[1].Any(c=>float.IsNaN(c.r)||c.r<0||c.r>1||Mathf.Abs(c.a-.37f)>.001f))throw new Exception("Invalid FP16 tone output or alpha");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(mat);UnityEngine.Object.DestroyImmediate(input);}
            if(ShaderUtil.ShaderHasError(Shader.Find("Hidden/LayerAwareBloom/HdrTonemapping")))throw new Exception("Tone shader error");
            File.AppendAllText(report,"PASS\n");
        }
        catch(Exception e){File.AppendAllText(report,"FAIL "+e);}
        finally{RenderTexture.active=previous;SceneView.RepaintAll();}
    }
}
