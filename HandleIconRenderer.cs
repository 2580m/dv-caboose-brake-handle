using UnityEngine;

namespace DVCabooseBreak
{
    internal static class HandleIconRenderer
    {
        internal static Sprite Render()
        {
            const int size = 256;
            // As with the native item preview pipeline, render the actual mesh
            // and original materials into a transparent texture. No drawn proxy.
            var scene = new GameObject("[Caboose handle icon render]");
            scene.transform.position = new Vector3(0, -10000, 0);
            var texture = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            try
            {
                var model = HandleFactory.AddModel(scene.transform);
                model.GetComponent<Collider>().enabled = false;
                int layer = LayerMask.NameToLayer("Render_Elements");
                foreach (var tr in model.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
                var bounds = new Bounds();
                bool first = true;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>())
                {
                    if (first) { bounds = renderer.bounds; first = false; }
                    else bounds.Encapsulate(renderer.bounds);
                }
                var cameraObject = new GameObject("Icon camera");
                cameraObject.transform.SetParent(scene.transform, false);
                var camera = cameraObject.AddComponent<Camera>();
                camera.enabled = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = 1 << layer;
                camera.orthographic = true;
                camera.orthographicSize = bounds.size.magnitude * 0.57f;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = 2f;
                camera.allowHDR = false;
                camera.targetTexture = texture;
                cameraObject.transform.position = bounds.center + new Vector3(0.12f, 0.045f, 0.5f);
                cameraObject.transform.LookAt(bounds.center);
                AddLight(scene.transform, new Vector3(30, -145, 0), 1.8f, layer);
                AddLight(scene.transform, new Vector3(-30, 35, 0), 0.9f, layer);
                camera.Render();
                RenderTexture.active = texture;
                var pixels = new Texture2D(size, size, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                pixels.Apply();
                pixels.name = "DE2 brake handle actual model icon";
                return Sprite.Create(pixels, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            }
            finally
            {
                RenderTexture.active = previous;
                texture.Release();
                Object.Destroy(texture);
                // Disable immediately so temporary lights cannot affect another
                // render before deferred Unity destruction runs.
                scene.SetActive(false);
                Object.Destroy(scene);
            }
        }

        private static void AddLight(Transform parent, Vector3 rotation, float intensity, int layer)
        {
            var go = new GameObject("Icon light");
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(rotation);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = intensity;
            light.cullingMask = 1 << layer;
            light.shadows = LightShadows.None;
        }
    }
}
