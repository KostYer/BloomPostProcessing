using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace LayerAwareBloom
{
    // Replays standard uGUI geometry without editing the user's UI materials.
    // A private stencil attachment keeps UI masks independent from the camera's stencil bits.
    internal sealed class UIBloomCapture : IDisposable
    {
        internal sealed class Frame : ContextItem
        {
            public List<Element> elements;
            public readonly List<Mesh> meshes = new();
            public override void Reset()
            {
                foreach (var mesh in meshes) CoreUtils.Destroy(mesh);
                meshes.Clear();
                elements = null;
            }
        }

        internal sealed class Element
        {
            public Mesh mesh;
            public Matrix4x4 matrix, worldToCanvas;
            public Texture texture, alphaTexture;
            public Color rendererColor, materialColor;
            public Vector4 sampleAdd, clipRect, softness;
            public bool clipped;
            public int owner = -1;
            public float emission;
            public readonly List<Element> masks = new();
        }

        private sealed class Draw
        {
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Material material;
            public MaterialPropertyBlock properties;
        }

        private sealed class PassData { public List<Draw> draws; }
        private readonly Shader shader;
        private readonly Shader nativeShader;
        private readonly Dictionary<int, Material> materials = new();
        internal const string MaterialTag = "LayerAwareBloomCanvas";
        private readonly Dictionary<Material, Material> nativeMaterials = new();
        private readonly List<(CanvasRenderer renderer, int index, bool pop, Material original, Material wrapper)> bindings = new();

        // Reuse the built-in UI pass in a tagged wrapper. Only the bloom source reads the tag.
        // Original materials are never edited. Restore bindings when disabled or disposed.
        public void PrepareNativeUI()
        {
            RestoreNativeMaterials();
            if (nativeShader == null) return;
            var usedMaterials = new HashSet<Material>();
            // Include prefab/preview scenes, but exclude assets and Overlay canvases.
            var renderers = Resources.FindObjectsOfTypeAll<CanvasRenderer>();
            foreach (var renderer in renderers)
            {
                if (!renderer.gameObject.scene.IsValid() || !renderer.gameObject.activeInHierarchy) continue;
                var canvas = renderer.GetComponentInParent<Canvas>();
                if (canvas == null || canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay) continue;
                for (int i = 0; i < renderer.materialCount; i++) Bind(renderer, i, false);
                for (int i = 0; i < renderer.popMaterialCount; i++) Bind(renderer, i, true);
            }
            // Stencil material instances come and go when masks/layouts change.
            var unused = new List<Material>();
            foreach (var pair in nativeMaterials)
                if (!usedMaterials.Contains(pair.Key)) unused.Add(pair.Key);
            foreach (var original in unused)
            { CoreUtils.Destroy(nativeMaterials[original]); nativeMaterials.Remove(original); }
            void Bind(CanvasRenderer renderer, int index, bool pop)
            {
                var original = pop ? renderer.GetPopMaterial(index) : renderer.GetMaterial(index);
                if (original == null || original.shader.name != "UI/Default") return;
                usedMaterials.Add(original);
                if (!nativeMaterials.TryGetValue(original, out var material))
                    nativeMaterials.Add(original, material = CoreUtils.CreateEngineMaterial(nativeShader));
                material.CopyPropertiesFromMaterial(original);
                bindings.Add((renderer, index, pop, original, material));
                if (pop) renderer.SetPopMaterial(material, index); else renderer.SetMaterial(material, index);
            }
        }

        public void RestoreNativeMaterials()
        {
            foreach (var binding in bindings)
                if (binding.renderer != null)
                {
                    // A uGUI rebuild or user edit may have replaced a slot since we last wrapped it.
                    if (binding.pop && binding.index < binding.renderer.popMaterialCount &&
                        binding.renderer.GetPopMaterial(binding.index) == binding.wrapper)
                        binding.renderer.SetPopMaterial(binding.original, binding.index);
                    else if (!binding.pop && binding.index < binding.renderer.materialCount &&
                        binding.renderer.GetMaterial(binding.index) == binding.wrapper)
                        binding.renderer.SetMaterial(binding.original, binding.index);
                }
            bindings.Clear();
        }

        public UIBloomCapture(Shader shader, Shader nativeShader)
        { this.shader = shader; this.nativeShader = nativeShader; }

        public void Dispose()
        {
            RestoreNativeMaterials();
            foreach (var material in materials.Values) CoreUtils.Destroy(material);
            materials.Clear();
            foreach (var material in nativeMaterials.Values) CoreUtils.Destroy(material);
            nativeMaterials.Clear();
        }

        private Material GetMaterial(int depth, bool mask, bool pop)
        {
            int key = depth * 4 + (mask ? 1 : 0) + (pop ? 2 : 0);
            if (materials.TryGetValue(key, out var material)) return material;
            material = CoreUtils.CreateEngineMaterial(shader);
            material.SetInt("_StencilRef", depth);
            material.SetInt("_StencilOp", (int)(mask ? (pop ? StencilOp.DecrementSaturate : StencilOp.IncrementSaturate) : StencilOp.Keep));
            material.SetInt("_ColorMask", mask ? 0 : 15);
            materials.Add(key, material);
            return material;
        }

        public void Record(RenderGraph graph, ContextContainer frameData, TextureHandle source,
            int groupIndex, IReadOnlyList<RenderingLayerBloomFeature.BloomGroupSettings> groups, bool bloomEnabled)
        {
            if (shader == null) return;
            var frame = frameData.GetOrCreate<Frame>();
            frame.elements ??= Collect(frameData.Get<UniversalCameraData>().camera, groups, bloomEnabled, frame.meshes);
            if (frame.elements.Count == 0) return;
            var draws = new List<Draw>();
            foreach (var element in frame.elements)
            {
                // Rebuild each element's mask chain. This also handles interleaved sorting canvases.
                for (int i = 0; i < element.masks.Count; i++)
                    draws.Add(CreateDraw(element.masks[i], GetMaterial(i, true, false), 0, true));
                draws.Add(CreateDraw(element, GetMaterial(element.masks.Count, false, false),
                    element.owner == groupIndex ? element.emission : 0, false));
                for (int i = element.masks.Count - 1; i >= 0; i--)
                    draws.Add(CreateDraw(element.masks[i], GetMaterial(i + 1, true, true), 0, true));
            }
            var descriptor = graph.GetTextureDesc(source);
            descriptor.name = $"Bloom UI Group {groupIndex + 1} Stencil";
            descriptor.colorFormat = GraphicsFormat.None;
            descriptor.depthBufferBits = DepthBits.Depth24;
            descriptor.clearBuffer = true;
            var stencil = graph.CreateTexture(descriptor);
            using var builder = graph.AddRasterRenderPass<PassData>($"Bloom Group {groupIndex + 1} UI Source", out var data);
            data.draws = draws;
            builder.SetRenderAttachment(source, 0, AccessFlags.ReadWrite);
            builder.SetRenderAttachmentDepth(stencil, AccessFlags.ReadWrite);
            builder.UseAllGlobalTextures(true);
            builder.UseTexture(frameData.Get<UniversalResourceData>().cameraDepthTexture, AccessFlags.Read);
            builder.SetRenderFunc(static (PassData pass, RasterGraphContext context) =>
            {
                foreach (var draw in pass.draws)
                    context.cmd.DrawMesh(draw.mesh, draw.matrix, draw.material, 0, 0, draw.properties);
            });
        }

        private static Draw CreateDraw(Element element, Material material, float emission, bool mask)
        {
            var properties = new MaterialPropertyBlock();
            properties.SetTexture("_MainTex", element.texture);
            properties.SetTexture("_AlphaTex", element.alphaTexture != null ? element.alphaTexture : Texture2D.whiteTexture);
            properties.SetFloat("_UseAlphaTex", element.alphaTexture != null ? 1 : 0);
            properties.SetColor("_RendererColor", element.rendererColor);
            properties.SetColor("_MaterialColor", element.materialColor);
            properties.SetVector("_TextureSampleAdd", element.sampleAdd);
            properties.SetFloat("_Emission", emission);
            properties.SetFloat("_IsMask", mask ? 1 : 0);
            properties.SetFloat("_UseClipRect", element.clipped ? 1 : 0);
            properties.SetVector("_ClipRect", element.clipRect);
            properties.SetVector("_ClipSoftness", element.softness);
            properties.SetMatrix("_WorldToCanvas", element.worldToCanvas);
            return new Draw { mesh = element.mesh, matrix = element.matrix, material = material, properties = properties };
        }

        private static Canvas SortCanvas(Graphic graphic)
        {
            var canvas = graphic.canvas;
            while (!canvas.isRootCanvas && !canvas.overrideSorting)
                canvas = canvas.transform.parent.GetComponentInParent<Canvas>();
            return canvas;
        }

        private static List<Element> Collect(Camera camera,
            IReadOnlyList<RenderingLayerBloomFeature.BloomGroupSettings> groups, bool bloomEnabled, List<Mesh> meshes)
        {
            var graphics = new List<Graphic>();
            if (camera.scene.IsValid())
                foreach (var root in camera.scene.GetRootGameObjects()) graphics.AddRange(root.GetComponentsInChildren<Graphic>());
            else graphics.AddRange(UnityEngine.Object.FindObjectsByType<Graphic>(FindObjectsSortMode.None));
            graphics.RemoveAll(g => !g.isActiveAndEnabled || g.canvas == null || !g.canvas.isActiveAndEnabled ||
                g.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ||
                (g.canvas.rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && g.canvas.rootCanvas.worldCamera != camera) ||
                (camera.cullingMask & (1 << g.gameObject.layer)) == 0 || g.canvasRenderer.cull ||
                !Supported(g));
            graphics.Sort((a, b) =>
            {
                var ac = SortCanvas(a); var bc = SortCanvas(b);
                int order = SortingLayer.GetLayerValueFromID(ac.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(bc.sortingLayerID));
                if (order == 0) order = ac.sortingOrder.CompareTo(bc.sortingOrder);
                if (order == 0 && ac != bc)
                {
                    float Distance(Canvas c) => camera.orthographic
                        ? Vector3.Dot(c.transform.position - camera.transform.position, camera.transform.forward)
                        : (c.transform.position - camera.transform.position).sqrMagnitude;
                    order = Distance(bc).CompareTo(Distance(ac));
                    if (order == 0) order = ac.renderOrder.CompareTo(bc.renderOrder);
                }
                if (order == 0) order = a.depth.CompareTo(b.depth);
                return order != 0 ? order : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            var result = new List<Element>();
            var cache = new Dictionary<Graphic, Element>();
            Element Snapshot(Graphic graphic)
            {
                if (!cache.TryGetValue(graphic, out var snapshot))
                    cache.Add(graphic, snapshot = CaptureElement(graphic, meshes));
                return snapshot;
            }
            foreach (var graphic in graphics)
            {
                if (graphic.TryGetComponent<Mask>(out var ownMask) && ownMask.IsActive() && !ownMask.showMaskGraphic) continue;
                var element = Snapshot(graphic);
                if (element == null) continue;
                var selection = UIBloomSource.Resolve(graphic.transform);
                if (selection != null && selection.isActiveAndEnabled)
                {
                    element.emission = Mathf.Max(0, selection.emissionMultiplier);
                    for (int i = 0; i < groups.Count; i++)
                    {
                        var group = groups[i];
                        if (!group.enabled || ((uint)group.renderingLayers & (uint)selection.renderingLayers) == 0 ||
                            (bloomEnabled && (group.intensity <= 0 || group.clamp <= 0 ||
                                (group.tint.r <= 0 && group.tint.g <= 0 && group.tint.b <= 0)))) continue;
                        element.owner = i; break;
                    }
                }
                bool invalidMask = false;
                if (graphic is MaskableGraphic maskable && maskable.maskable)
                {
                    var boundary = MaskUtilities.FindRootSortOverrideCanvas(graphic.transform);
                    for (var parent = boundary == graphic.transform ? null : graphic.transform.parent;
                        parent != null; parent = parent.parent)
                    {
                        if (parent.TryGetComponent<Mask>(out var mask) && mask.IsActive() && mask.graphic != null)
                        {
                            var maskElement = Supported(mask.graphic) ? Snapshot(mask.graphic) : null;
                            if (maskElement == null) { invalidMask = true; break; }
                            element.masks.Add(maskElement);
                        }
                        if (parent == boundary) break;
                    }
                    element.masks.Reverse();
                }
                if (!invalidMask) result.Add(element);
            }
            return result;
        }

        // Custom UI shader effects and SDF text require their own capture implementation.
        internal static bool Supported(Graphic graphic) =>
            (graphic is Image || graphic is RawImage || graphic is Text) &&
            graphic.material != null && graphic.material.shader.name == "UI/Default";

        private static Element CaptureElement(Graphic graphic, List<Mesh> meshes)
        {
            var renderer = graphic.canvasRenderer;
            var canvasMesh = renderer.GetMesh();
            if (canvasMesh == null || canvasMesh.vertexCount == 0) return null;
            // Native Canvas geometry has CPU data but is not a standalone GPU draw mesh.
            var mesh = new Mesh { name = "UI Bloom Capture", hideFlags = HideFlags.HideAndDontSave,
                vertices = canvasMesh.vertices, colors32 = canvasMesh.colors32,
                uv = canvasMesh.uv, triangles = canvasMesh.triangles };
            mesh.UploadMeshData(false);
            meshes.Add(mesh);
            var color = renderer.GetColor();
            color.a *= renderer.GetInheritedAlpha();
            var element = new Element
            {
                mesh = mesh, matrix = graphic.transform.localToWorldMatrix,
                worldToCanvas = graphic.canvas.rootCanvas.transform.worldToLocalMatrix,
                texture = graphic.mainTexture != null ? graphic.mainTexture : Texture2D.whiteTexture,
                rendererColor = color,
                materialColor = graphic.material.HasProperty("_Color") ? graphic.material.GetColor("_Color") : Color.white,
                sampleAdd = graphic.mainTexture is Texture2D texture && texture.format == TextureFormat.Alpha8 ? new Vector4(1, 1, 1, 0) : Vector4.zero
            };
            if (graphic is Image image)
            {
                var sprite = image.overrideSprite;
                if (sprite != null) element.alphaTexture = sprite.associatedAlphaSplitTexture;
            }
            if (graphic is MaskableGraphic maskable && maskable.maskable)
            {
                var clipper = MaskUtilities.GetRectMaskForClippable(maskable);
                if (clipper != null)
                {
                    var clippers = new List<RectMask2D>();
                    MaskUtilities.GetRectMasksForClip(clipper, clippers);
                    var rect = Clipping.FindCullAndClipWorldRect(clippers, out bool valid);
                    if (!valid) return null;
                    element.clipped = true;
                    element.clipRect = new Vector4(rect.xMin, rect.yMin, rect.xMax, rect.yMax);
                    element.softness = new Vector4(clipper.softness.x, clipper.softness.y, 0, 0);
                }
            }
            return element;
        }
    }
}
