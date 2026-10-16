using System;
using System.Collections.Generic;
using System.Linq;
using DV.Shops;
using DV.UserManagement;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DVCabooseBreak
{
    internal static class ShopIntegration
    {
        // x/z offset, layer height, and yaw in degrees. Fixed poses keep the
        // same hand-arranged pile on every load without consuming game RNG.
        internal static readonly float[,] DisplayPoses = {
            { -0.043f, 0.000f, -0.006f, -18f },
            {  0.034f, 0.010f,  0.012f,  16f },
            { -0.016f, 0.020f, -0.021f,  -4f },
            {  0.046f, 0.030f, -0.014f,  27f },
            { -0.031f, 0.040f,  0.019f, -29f }
        };

        internal static bool HideTemplateRenderer(bool isText, bool isNativeLabelPart)
            => !isText && !isNativeLabelPart;

        private static void ReplaceTemplateVisuals(GameObject display)
        {
            // Verified in the native Hammer_ShelfItem asset: ScanItem contains
            // Texts/Name, Texts/Price, NamePrice bg and the scanner collider.
            // Preserve this complete subtree, including non-TMP label meshes.
            var labelRoot = display.transform.Find("ScanItem");
            if (labelRoot == null) throw new InvalidOperationException("Native shelf ScanItem label was not found.");
            var hidden = new HashSet<Renderer>();
            foreach (var renderer in display.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool isLabel = renderer.transform.IsChildOf(labelRoot);
                if (!HideTemplateRenderer(renderer.GetComponent<TMP_Text>() != null, isLabel)) continue;
                renderer.enabled = false;
                hidden.Add(renderer);
            }
            // Otherwise the original product's LODGroup can turn its sample
            // meshes back on later, leaving old merchandise inside our pile.
            foreach (var lodGroup in display.GetComponentsInChildren<LODGroup>(true))
                if (lodGroup.GetLODs().Any(lod => lod.renderers.Any(r => hidden.Contains(r))))
                    lodGroup.enabled = false;
        }

        private static void AddDisplayPile(Transform display)
        {
            var pile = new GameObject("Five overlapping brake handles").transform;
            pile.SetParent(display, false);
            for (int i = 0; i < DisplayPoses.GetLength(0); i++)
            {
                var pose = new GameObject("Shelf handle " + (i + 1)).transform;
                pose.SetParent(pile, false);
                // AddModel points up along Y; lay it back onto the shelf,
                // then fan it around the shelf's vertical axis.
                pose.localRotation = Quaternion.Euler(0, DisplayPoses[i, 3], 0) * Quaternion.Euler(-90, 0, 0);
                var model = HandleFactory.AddModel(pose);
                model.GetComponent<Collider>().enabled = false;
                var bounds = LocalMeshBounds(pile, pose);
                pose.localPosition = new Vector3(DisplayPoses[i, 0], DisplayPoses[i, 1] - bounds.min.y, DisplayPoses[i, 2]);
            }
            var pileBounds = LocalMeshBounds(display, pile);
            // ShelfItem origin is at the front edge, with depth extending -Z.
            const float margin = 0.012f;
            pile.localPosition += new Vector3(-pileBounds.center.x, margin, -pileBounds.max.z - margin);
            var shelf = display.GetComponent<ShelfItem>();
            shelf.size = new Unity.Mathematics.float2(pileBounds.size.x + 2 * margin, pileBounds.size.z + 2 * margin);
            shelf.height = pileBounds.size.y + 2 * margin;
            // The cloned ShelfItem already has its Awake-created collider.
            // Update it as well, rather than keeping the template's footprint.
            foreach (var collider in display.GetComponents<BoxCollider>())
            {
                collider.center = new Vector3(0, shelf.height * 0.5f, -shelf.Depth * 0.5f);
                collider.size = new Vector3(shelf.Width, shelf.height, shelf.Depth);
            }
        }

        private static Bounds LocalMeshBounds(Transform frame, Transform visuals)
        {
            var result = new Bounds();
            bool first = true;
            foreach (var filter in visuals.GetComponentsInChildren<MeshFilter>(true))
            {
                var meshBounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    p = frame.InverseTransformPoint(filter.transform.TransformPoint(p));
                    if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                    else result.Encapsulate(p);
                }
            }
            return result;
        }

        internal static void AddCatalog(GlobalShopController controller)
        {
            if (controller.shopItemsData.Any(d => d.item != null && d.item.ItemPrefabName == Main.PrefabId)) return;
            var data = new ShopItemData {
                item = HandleFactory.GetPrefab().GetComponent<InventoryItemSpec>(),
                basePrice = Main.Price,
                isGlobal = true,
                soldOnlyAt = new System.Collections.Generic.List<Shop>()
            };
            AccessTools.Field(typeof(ShopItemData), "amount").SetValue(data, 20);
            controller.shopItemsData.Add(data);
        }

        internal static void AddDisplays(GlobalShopController controller)
        {
            foreach (var shop in controller.globalShopList)
            {
                if (shop == null || shop.cashRegister == null || shop.scanItemResourceModules == null) continue;
                var candidates = shop.scanItemResourceModules.Where(m => m != null && m.GetComponent<ShelfItem>() != null).ToArray();
                var template = candidates.FirstOrDefault(m => m.sellingItemSpec != null &&
                    m.sellingItemSpec.ItemPrefabName.IndexOf("hammer", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? candidates.FirstOrDefault();
                if (template == null || shop.scanItemResourceModules.Any(m => m != null && m.sellingItemSpec != null && m.sellingItemSpec.ItemPrefabName == Main.PrefabId)) continue;
                var display = Object.Instantiate(template.gameObject, Main.Stage.transform, false);
                display.name = "Caboose brake handle shop display";
                var module = display.GetComponent<ScanItemCashRegisterModule>();
                module.sellingItemSpec = Main.HandlePrefab.GetComponent<InventoryItemSpec>();
                ReplaceTemplateVisuals(display);
                AddDisplayPile(display.transform);
                display.transform.SetParent(template.transform.parent, false);
                display.transform.localPosition = template.transform.localPosition + new Vector3(0.22f, 0.05f, 0);
                display.transform.localRotation = template.transform.localRotation;
                module.ItemPurchased += controller.AddItemToInstantiationQueue;
                shop.scanItemResourceModules = shop.scanItemResourceModules.Concat(new[] { module }).ToArray();
                shop.cashRegister.registerModules = shop.cashRegister.registerModules.Concat(new CashRegisterModule[] { module }).ToArray();
                var register = shop.cashRegister;
                module.OnUnitsToBuyChanged += () => AccessTools.Method(register.GetType(), "OnUnitsToBuyChanged").Invoke(register, null);
                Main.Entry.Logger.Log("Added brake handle scanner display at " + shop.name);
            }
        }
    }
}
