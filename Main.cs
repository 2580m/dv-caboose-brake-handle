using System;
using System.Collections.Generic;
using System.Linq;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.InventorySystem;
using DV.Shops;
using DV.Simulation.Brake;
using DV.ThingTypes;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityModManagerNet;
using Object = UnityEngine.Object;

namespace DVCabooseBreak
{
    public static class Main
    {
        public const string PrefabId = "dv-caboose-break/BrakeHandle";
        internal const string SaveKey = "dv-caboose-break.v1";
        internal static UnityModManager.ModEntry Entry;
        internal static GameObject Staging;
        internal static GameObject HandlePrefab;
        internal static float Price = 500f;
        internal static readonly Dictionary<string, float> Installed = new Dictionary<string, float>();
        internal static readonly Dictionary<BrakeSystem, CabooseBrake> Brakes = new Dictionary<BrakeSystem, CabooseBrake>();
        private static SaveGameData boundSave;

        public static bool Load(UnityModManager.ModEntry entry)
        {
            Entry = entry;
            new Harmony(entry.Info.Id).PatchAll();
            entry.OnGUI = _ => {
                GUILayout.Label("守车制动拉杆 / Caboose brake handle — $500");
                GUILayout.Label("商店扫码结账；手持拉杆瞄准守车阀轴，按 F 安装。\n拖动或滚轮操作小闸；完全缓解后，瞄准拉杆按住左 Ctrl 再按 F 拆卸。");
                GUILayout.Label("需要重启游戏才能卸载。多人游戏尚未验证。");
            };
            entry.Logger.Log("Loaded. Native shop item + caboose independent air brake.");
            return true;
        }

        internal static GameObject Stage
        {
            get
            {
                if (Staging == null)
                {
                    Staging = new GameObject("[dv-caboose-break prefabs]");
                    Staging.SetActive(false);
                    Object.DontDestroyOnLoad(Staging);
                }
                return Staging;
            }
        }

        internal static void BindSave()
        {
            var manager = Object.FindObjectOfType<SaveGameManager>();
            if (manager == null || manager.data == null || ReferenceEquals(boundSave, manager.data)) return;
            boundSave = manager.data;
            Installed.Clear();
            var saved = boundSave.GetJObject(SaveKey);
            if (saved != null)
                foreach (var property in saved.Properties())
                {
                    float value;
                    if (float.TryParse(property.Value.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out value) && !float.IsNaN(value) && !float.IsInfinity(value))
                        Installed[property.Name] = Mathf.Clamp01(value);
                }
        }

        internal static void Save(SaveGameManager manager)
        {
            BindSave();
            if (manager.data == null) return;
            var data = new JObject();
            foreach (var pair in Installed) data[pair.Key] = pair.Value;
            manager.data.SetJObject(SaveKey, data);
        }

        internal static bool IsCaboose(TrainCar car) => car != null && car.carLivery != null && CarTypes.IsCaboose(car.carLivery);

        internal static void PrepareCar(TrainCar car)
        {
            if (!IsCaboose(car) || car.brakeSystem == null || car.interior == null) return;
            BindSave();
            var controller = car.GetComponent<CabooseBrake>();
            if (controller == null) controller = car.gameObject.AddComponent<CabooseBrake>();
            controller.Initialize(car);
        }
    }

    // Patch the managed wrapper. The (string, Type) overload is a Unity native
    // InternalCall in this game version and cannot safely take a Harmony prefix.
    [HarmonyPatch]
    internal static class ResourcePatch
    {
        static System.Reflection.MethodBase TargetMethod() => typeof(Resources).GetMethods()
            .Single(m => m.Name == "Load" && !m.IsGenericMethod && m.GetParameters().Length == 1);
        static bool Prefix(string path, ref Object __result)
        {
            if (path != Main.PrefabId) return true;
            __result = HandleFactory.GetPrefab();
            return false;
        }
    }

    [HarmonyPatch(typeof(GlobalShopController), "InitializeShopData")]
    internal static class CatalogPatch
    {
        static void Prefix(GlobalShopController __instance) => ShopIntegration.AddCatalog(__instance);
    }

    [HarmonyPatch(typeof(GlobalShopController), "Awake")]
    internal static class ShopsPatch
    {
        static void Postfix(GlobalShopController __instance) => ShopIntegration.AddDisplays(__instance);
    }

    [HarmonyPatch(typeof(TrainCar), nameof(TrainCar.LoadInterior))]
    internal static class InteriorPatch
    {
        static void Postfix(TrainCar __instance) => Main.PrepareCar(__instance);
    }

    // Logic initialization also runs for distant cars and reused pooled cars.
    [HarmonyPatch(typeof(TrainCar), "InitializeLogicCarRelatedScript")]
    internal static class CarReadyPatch
    {
        static void Postfix(TrainCar __instance) => Main.PrepareCar(__instance);
    }

    [HarmonyPatch(typeof(GlobalShopController), nameof(GlobalShopController.UpdateItemStocksOnGameLoad))]
    internal static class StockPatch
    {
        static void Postfix()
        {
            Main.BindSave();
            var controller = GlobalShopController.Instance;
            var data = controller.GetShopItemData(Main.PrefabId);
            if (data == null) return;
            data.purchasedItems += Main.Installed.Count;
            controller.Fire_GlobalShopDataChanged();
        }
    }

    [HarmonyPatch(typeof(SaveGameManager), "UpdateInternalData")]
    internal static class SavePatch
    {
        static void Postfix(SaveGameManager __instance) => Main.Save(__instance);
    }

    // The native freight control valve still supplies air from the finite auxiliary
    // reservoir, vents the cylinder, applies heat/fade and updates both gauge needles.
    [HarmonyPatch(typeof(BrakeSystem), "TargetCylinderPressure")]
    internal static class CylinderTargetPatch
    {
        static void Postfix(BrakeSystem __instance, ref float __result)
        {
            CabooseBrake controller;
            if (Main.Brakes.TryGetValue(__instance, out controller) && controller != null && controller.HasHandle)
                __result = Mathf.Max(__result, Mathf.Lerp(1f, 4.5f, controller.Position));
        }
    }
}
