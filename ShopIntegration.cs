using System;
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
                var template = shop.scanItemResourceModules.FirstOrDefault(m => m != null && m.GetComponent<ShelfItem>() != null);
                if (template == null) template = shop.scanItemResourceModules.FirstOrDefault(m => m != null);
                if (template == null || shop.scanItemResourceModules.Any(m => m != null && m.sellingItemSpec != null && m.sellingItemSpec.ItemPrefabName == Main.PrefabId)) continue;
                var display = Object.Instantiate(template.gameObject, Main.Stage.transform, false);
                display.name = "Caboose brake handle shop display";
                var module = display.GetComponent<ScanItemCashRegisterModule>();
                module.sellingItemSpec = Main.HandlePrefab.GetComponent<InventoryItemSpec>();
                // Keep native name/price text, scanner target and shelf footprint.
                // Replace only the old sample item's renderers.
                foreach (var renderer in display.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer.GetComponent<TMP_Text>() == null) renderer.enabled = false;
                var model = HandleFactory.AddModel(display.transform);
                model.transform.localPosition = new Vector3(0, 0.06f, -0.10f);
                model.GetComponent<Collider>().enabled = false;
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
