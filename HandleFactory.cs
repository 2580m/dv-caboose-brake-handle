using System;
using System.Linq;
using DV;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.ThingTypes;
using DV.Shops;
using DV.InventorySystem;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DVCabooseBreak
{
    internal static class HandleFactory
    {
        internal const string NativeInteriorName = "LocoDE2_Interior";
        internal const string NativeControlPath = "RightCluster/BrakeIndependent/C_BrakeIndependent";
        private static Transform nativeControl;
        internal const string AudioInteriorName = "LocoS060_Interior";
        internal const string AudioControlPath = "Right/IndependentBrake/C_IndependentBrake";
        private static Lever audioControl;

        internal static void CopyS060BrakeAudio(Lever target)
        {
            if (audioControl == null)
            {
                var interior = Globals.G.Types.Liveries.Select(l => l.interiorPrefab)
                    .FirstOrDefault(p => p != null && p.name == AudioInteriorName);
                var source = interior != null ? interior.transform.Find(AudioControlPath) : null;
                audioControl = source != null ? source.GetComponent<Lever>() : null;
                if (audioControl == null)
                    throw new InvalidOperationException("The S060 independent brake audio source was not found.");
                Main.Entry.Logger.Log("Using S060 independent brake audio at " + AudioControlPath);
            }
            // LeverBase creates native LeverAudio, including movement/stop
            // detection, positional playback and its startup mute period.
            target.notch = audioControl.notch;
            target.drag = audioControl.drag;
            target.limitHit = audioControl.limitHit;
            target.limitVibration = audioControl.limitVibration;
        }

        internal static GameObject GetPrefab()
        {
            if (Main.HandlePrefab != null) return Main.HandlePrefab;
            var root = new GameObject("Caboose brake valve handle");
            root.transform.SetParent(Main.Stage.transform, false);
            var model = AddModel(root.transform);
            var worldItemLayer = LayerMask.NameToLayer("World_Item");
            foreach (var tr in root.GetComponentsInChildren<Transform>(true))
                tr.gameObject.layer = worldItemLayer;
            var anchor = new GameObject("[anchor]");
            anchor.transform.SetParent(root.transform, false);
            anchor.transform.localPosition = new Vector3(0, 0.13f, 0.016f);
            var spec = root.AddComponent<InventoryItemSpec>();
            AccessTools.Field(typeof(InventoryItemSpec), "itemPrefabName").SetValue(spec, Main.PrefabId);
            spec.NameOverride = "守车制动拉杆 / Caboose brake handle";
            spec.IsEssential = false;
            spec.PreviewPrefab = model;
            spec.PreviewBounds = new Bounds(new Vector3(0, 0.0655f, 0.0145f), new Vector3(0.059f, 0.237f, 0.033f));
            // The icon is made locally; no redistributed game assets or asset bundle.
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color[4096];
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                    pixels[y * 64 + x] = x >= 27 && x <= 37 && y >= 8 && y <= 57
                        ? (y < 29 ? new Color(0.16f, 0.17f, 0.18f) : new Color(0.62f, 0.48f, 0.32f)) : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply();
            var icon = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));
            spec.ItemIconSprite = spec.ItemIconSpriteSimple = spec.ItemIconSpriteDropped = icon;
            root.AddComponent<HandleDescription>();
            root.AddComponent<ItemSaveData>();
            var item = root.AddComponent<Item>();
            item.colliderGameObjects = new[] { model };
            item.rigidbodyMass = 0.8f;
            item.itemUseApproach = ItemUseApproach.OneShot;
            item.allowedSnapPointTypes = 0;
            item.itemType = ItemType.SmallItem;
            root.AddComponent<ShopRestocker>();
            root.AddComponent<LooseHandle>();
            Main.HandlePrefab = root;
            return root;
        }

        internal static GameObject AddModel(Transform parent)
        {
            var source = GetNativeControl();
            var model = new GameObject("DE2 independent brake handle model");
            model.transform.SetParent(parent, false);
            // Native mesh points along -X. Rotate the entire model about its
            // ORIGINAL spindle pivot so the released handle points straight up.
            // Keep scale 1:1, the square bore, UVs and shared materials intact.
            model.transform.localRotation = Quaternion.Euler(0, 0, -90f);
            foreach (var sourceFilter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                var sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
                if (sourceFilter.sharedMesh == null || sourceRenderer == null) continue;
                var visual = new GameObject(sourceFilter.name);
                visual.transform.SetParent(model.transform, false);
                visual.transform.localPosition = source.InverseTransformPoint(sourceFilter.transform.position);
                visual.transform.localRotation = Quaternion.Inverse(source.rotation) * sourceFilter.transform.rotation;
                visual.transform.localScale = sourceFilter.transform.lossyScale;
                visual.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
                var renderer = visual.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = sourceRenderer.sharedMaterials;
                renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
                renderer.receiveShadows = sourceRenderer.receiveShadows;
                renderer.lightProbeUsage = sourceRenderer.lightProbeUsage;
                renderer.reflectionProbeUsage = sourceRenderer.reflectionProbeUsage;
            }
            // Recreate the DE2's native interaction capsule in control space.
            // Do not clone its controller scripts, port feeders or rigidbodies.
            var nativeCollider = source.GetComponentInChildren<CapsuleCollider>(true);
            if (nativeCollider == null) throw new InvalidOperationException("DE2 brake handle capsule was not found.");
            var collider = model.AddComponent<CapsuleCollider>();
            collider.center = source.InverseTransformPoint(nativeCollider.transform.TransformPoint(nativeCollider.center));
            collider.direction = 0; // Native capsule Z rotated -90 degrees about Y = control X.
            collider.radius = nativeCollider.radius;
            collider.height = nativeCollider.height;
            collider.sharedMaterial = nativeCollider.sharedMaterial;
            return model;
        }

        private static Transform GetNativeControl()
        {
            if (nativeControl != null) return nativeControl;
            // Read the asset reference directly; no DE2 has to be spawned nearby.
            var interior = Globals.G.Types.Liveries.Select(l => l.interiorPrefab)
                .FirstOrDefault(p => p != null && p.name == NativeInteriorName);
            if (interior == null) throw new InvalidOperationException("The original DE2 interior prefab was not found.");
            nativeControl = interior.transform.Find(NativeControlPath);
            if (nativeControl == null || nativeControl.GetComponentInChildren<MeshFilter>(true) == null)
                throw new InvalidOperationException("The original DE2 independent brake handle was not found.");
            Main.Entry.Logger.Log("Using original DE2 brake handle mesh/materials at " + NativeControlPath + " (scale 1:1).");
            return nativeControl;
        }
    }

    public sealed class HandleDescription : MonoBehaviour, IInventoryItemLocalizer
    {
        private void Awake() => GetComponent<InventoryItemSpec>().NameOverride = "守车制动拉杆 / Caboose brake handle";
        public string GetNameParam() => "";
        public string GetCustomDescription() => "安装在守车压力表旁缺少拉杆的制动阀轴上。手持瞄准阀轴，按 F 或使用键安装。拖动/滚轮控制本车空气制动，需要副风缸有气。完全缓解后 Ctrl+F 可拆卸。";
    }

    public sealed class LooseHandle : MonoBehaviour
    {
        private ItemBase item;
        private bool installing;
        private void Start()
        {
            item = GetComponent<ItemBase>();
            if (item != null) item.Used += Install;
        }
        private void Update()
        {
            if (item != null && item.IsGrabbed() && Input.GetKeyDown(KeyCode.F)) Install();
        }
        private void Install()
        {
            if (installing || item == null || !item.IsGrabbed() || !item.InventorySpecs.BelongsToPlayer) return;
            var target = CabooseBrake.AimedSocket();
            if (target == null || target.HasHandle) return;
            if (!target.Install()) return;
            installing = true;
            var restocker = GetComponent<ShopRestocker>();
            if (restocker != null) restocker.restockOnItemDestroyed = false;
            item.ForceEndInteraction();
            Inventory.Instance.PurgeFromInventory(gameObject);
            StorageController.Instance.RemoveItemFromStorageItemList(item);
            Object.Destroy(gameObject);
        }
        private void OnDestroy()
        {
            if (item != null) item.Used -= Install;
        }
    }
}
