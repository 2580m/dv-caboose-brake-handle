using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Newtonsoft.Json.Linq;

// Runs against the installed game's assemblies, without starting Unity or
// constructing native Unity objects. Exercises the real pneumatic math.
internal static class CompatibilityProbe
{
    private static string managed;
    private static string modPath;
    private static int assertions;
    private static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception(message);
        assertions++;
        Console.WriteLine("PASS " + message);
    }
    public static int Main(string[] args)
    {
        managed = Path.Combine(args[0], "DerailValley_Data", "Managed");
        modPath = args[1];
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            var name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var dir in new[] { managed, Path.Combine(managed, "UnityModManager"), Path.GetDirectoryName(modPath), Path.Combine(args[0], "Mods", "DVLangHelper") })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        try { Run(); VerifyTranslations(args[2]); Console.WriteLine(assertions + " assertions passed."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void VerifyTranslations(string path)
    {
        var i2 = Assembly.LoadFrom(Path.Combine(managed, "I2.Localization.dll"));
        var reader = i2.GetType("I2.Loc.LocalizationReader");
        var csv = (string)reader.GetMethod("ReadCSVfile").Invoke(null, new object[] { path, System.Text.Encoding.UTF8 });
        var rows = (System.Collections.Generic.List<string[]>)reader.GetMethod("ReadCSV").Invoke(null, new object[] { csv, ',' });
        Check(rows.Count == 18, "native CSV reader loads two header rows and 16 terms");
        Check(rows[0].Length == 27 && rows[0][2] == "English" && rows[0][25] == "Chinese (Simplified)", "DV Language Helper template language columns");
        Check(rows[1][2] == "en" && rows[1][25] == "zh-CN", "native CSV language codes");
        Check(rows.Skip(2).Select(r => r[0]).Distinct().Count() == 16, "unique translation keys");
        foreach (var row in rows.Skip(2))
        {
            Check(row.Length == 27 && row[0].StartsWith("dv-caboose-break/"), "namespaced CSV row " + row[0]);
            Check(row.Skip(2).All(s => !string.IsNullOrWhiteSpace(s)), "complete translations and English fallback " + row[0]);
            var pattern = @"\{\[\d+\]\}";
            var en = System.Text.RegularExpressions.Regex.Matches(row[2], pattern).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value);
            var zh = System.Text.RegularExpressions.Regex.Matches(row[25], pattern).Cast<System.Text.RegularExpressions.Match>().Select(m => m.Value);
            Check(en.SequenceEqual(zh), "English/Chinese placeholder parity " + row[0]);
        }
        var instructions = rows.Single(r => r[0] == "dv-caboose-break/settings.instructions");
        Check(instructions[2].Contains("\n") && instructions[25].Contains("\n"), "native CSV preserves quoted multiline instructions");
    }
    private static void Run()
    {
        var game = Assembly.LoadFrom(Path.Combine(managed, "Assembly-CSharp.dll"));
        var brakes = Assembly.LoadFrom(Path.Combine(managed, "DV.BrakeSystem.dll"));
        var unity = Assembly.LoadFrom(Path.Combine(managed, "UnityEngine.CoreModule.dll"));
        var mod = Assembly.LoadFrom(modPath);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        foreach (var name in new[] { "InitializeShopData", "Awake", "UpdateItemStocksOnGameLoad" })
            Check(game.GetType("DV.Shops.GlobalShopController").GetMethod(name, flags) != null, "shop patch target " + name);
        foreach (var name in new[] { "LoadInterior", "InitializeLogicCarRelatedScript" })
            Check(game.GetType("TrainCar").GetMethod(name, flags) != null, "car patch target " + name);
        Check(game.GetType("SaveGameManager").GetMethod("UpdateInternalData", flags) != null, "save patch target");
        var resourceWrapper = unity.GetType("UnityEngine.Resources").GetMethods()
            .Single(m => m.Name == "Load" && !m.IsGenericMethod && m.GetParameters().Length == 1);
        Check(resourceWrapper != null && resourceWrapper.GetMethodBody() != null, "custom resource lookup uses a managed patch target");
        var brakeType = brakes.GetType("DV.Simulation.Brake.BrakeSystem");
        Check(brakeType.GetMethod("TargetCylinderPressure", flags) != null, "cylinder target patch");
        Check(game.GetType("InventoryItemSpec").GetField("itemPrefabName", flags) != null, "custom item prefab identity field");
        Check(game.GetType("DV.Shops.ShopItemData").GetField("amount", flags) != null, "shop stock field");
        Check(game.GetType("DV.Shops.GlobalShopController").GetField("scanItemShelfPrefab", flags) != null,
            "native scanner shelf prefab provides original price-card meshes");
        var hideTemplateRenderer = mod.GetType("DVCabooseBreak.ShopIntegration").GetMethod("HideTemplateRenderer", flags);
        Func<bool, bool, bool> hideSample = (text, label) => (bool)hideTemplateRenderer.Invoke(null, new object[] { text, label });
        Check(!hideSample(true, false), "keep native name and price text");
        Check(!hideSample(false, true), "keep native price-card backing and barcode meshes");
        Check(!hideSample(true, true), "keep label text even when its mesh belongs to native label");
        Check(hideSample(false, false), "hide old merchandise without hiding price label");
        var shelfType = game.GetType("DV.Shops.ShelfItem");
        Check(shelfType.GetField("size", flags) != null && shelfType.GetField("height", flags) != null,
            "native shelf pile footprint fields");
        Check(game.GetType("DV.Shops.ShelfPlacer").GetMethod("TryPlaceOnAnyShelf", flags) != null,
            "native shelf placer accepts the whole display pile");
        var poses = (float[,])mod.GetType("DVCabooseBreak.ShopIntegration").GetField("DisplayPoses", flags).GetValue(null);
        Check(poses.GetLength(0) == 5 && poses.GetLength(1) == 4, "five shelf handles with position/layer/yaw poses");
        Check(Enumerable.Range(0, 5).Select(i => poses[i, 3]).Distinct().Count() == 5,
            "shelf pile has irregular non-repeating angles");
        Check(Enumerable.Range(1, 4).All(i => poses[i, 1] > poses[i - 1, 1]), "overlapping shelf handles use distinct ascending layers");
        // DE2 visual bounds in upright AddModel space, transformed with the
        // same flat-then-yaw order as the display. No Unity scene required.
        double shelfMinX = double.MaxValue, shelfMaxX = double.MinValue;
        double shelfMinZ = double.MaxValue, shelfMaxZ = double.MinValue;
        for (int i = 0; i < 5; i++)
        for (int corner = 0; corner < 4; corner++)
        {
            double x = (corner & 1) == 0 ? -0.0295 : 0.0295;
            double z = -((corner & 2) == 0 ? -0.053 : 0.184);
            double angle = poses[i, 3] * Math.PI / 180;
            double px = poses[i, 0] + x * Math.Cos(angle) + z * Math.Sin(angle);
            double pz = poses[i, 2] - x * Math.Sin(angle) + z * Math.Cos(angle);
            shelfMinX = Math.Min(shelfMinX, px); shelfMaxX = Math.Max(shelfMaxX, px);
            shelfMinZ = Math.Min(shelfMinZ, pz); shelfMaxZ = Math.Max(shelfMaxZ, pz);
        }
        Check(shelfMaxZ - shelfMinZ + 0.024 < 0.387, "flat pile including margins fits native shelf depth");
        Check(shelfMaxX - shelfMinX + 0.024 < 0.30, "display pile keeps a compact shelf width");
        var interaction = Assembly.LoadFrom(Path.Combine(managed, "DV.Interaction.dll"));
        Check(interaction.GetType("DV.Interaction.StaticInteractionArea").GetMethod("Initialize", flags) != null,
            "native fixed interaction area initialization");
        var specAssembly = Assembly.LoadFrom(Path.Combine(managed, "DV.CabControls.Spec.dll"));
        var leverSpec = specAssembly.GetType("DV.CabControls.Spec.Lever");
        foreach (var field in new[] { "nonVrStaticInteractionArea", "notch", "drag", "limitHit" })
            Check(leverSpec.GetField(field, flags) != null, "native lever area/audio field " + field);
        var caboose = mod.GetType("DVCabooseBreak.CabooseBrake");
        var settings = Activator.CreateInstance(mod.GetType("DVCabooseBreak.BrakeSettings"));
        Check(!(bool)settings.GetType().GetField("InvertMouseWheel").GetValue(settings), "mouse-wheel inversion defaults off");
        Check((float)settings.GetType().GetField("MouseWheelSensitivity").GetValue(settings) == 1f, "wheel sensitivity defaults to original 1x speed");
        var mapScroll = mod.GetType("DVCabooseBreak.CabooseMouseScrollPatch").GetMethod("Map", flags);
        var scrollAction = game.GetType("ScrollAction");
        var scrollSource = game.GetType("ScrollSource");
        var wheelType = mod.GetType("DVCabooseBreak.WheelStepAccumulator");
        var consume = wheelType.GetMethod("Consume", flags);
        Func<object> newWheel = () => Activator.CreateInstance(wheelType, true);
        Func<object, string, float, int> steps = (wheel, action, sensitivity) =>
            (int)consume.Invoke(wheel, new object[] { Enum.Parse(scrollAction, action), sensitivity });
        var slow = newWheel();
        Check(steps(slow, "ScrollUp", 0.5f) == 0 && steps(slow, "ScrollUp", 0.5f) == 1,
            "half sensitivity accumulates two ticks into one physical notch");
        var fast = newWheel();
        Check(steps(fast, "ScrollUp", 1.5f) == 1 && steps(fast, "ScrollUp", 1.5f) == 2,
            "fractional increased sensitivity preserves the requested average speed");
        var precision = newWheel();
        int total = 0;
        for (int i = 0; i < 10; i++) total += steps(precision, "ScrollUp", 0.7f);
        Check(total == 7, "fractional wheel input has no lost step from numerical drift");
        Check(steps(newWheel(), "ScrollDown", 5f) == -5, "maximum sensitivity works in the release direction");
        Check(steps(newWheel(), "Release", 5f) == 0, "scroll release never accumulates movement");
        var changed = newWheel();
        steps(changed, "ScrollUp", 0.5f);
        Check(steps(changed, "ScrollUp", 1f) == 1, "changing sensitivity discards stale fractional input");
        Func<string, string, bool, bool, string> mapped = (action, source, own, invert) =>
            mapScroll.Invoke(null, new object[] { Enum.Parse(scrollAction, action), Enum.Parse(scrollSource, source), own, invert }).ToString();
        Check(mapped("ScrollUp", "Mouse", true, true) == "ScrollDown" && mapped("ScrollDown", "Mouse", true, true) == "ScrollUp",
            "enabled option reverses caboose mouse up/down");
        Check(mapped("ScrollUp", "Mouse", true, false) == "ScrollUp", "disabled option preserves normal caboose wheel direction");
        Check(mapped("ScrollUp", "Mouse", false, true) == "ScrollUp", "locomotive and other levers remain unaffected");
        Check(mapped("ScrollUp", "HUD", true, true) == "ScrollUp", "HUD input remains unaffected");
        Check(mapped("Release", "Mouse", true, true) == "Release" && mapped("ScrollLeft", "Mouse", true, true) == "ScrollLeft",
            "release and horizontal inputs remain unaffected");
        Check(caboose.GetMethod("OnGUI", flags) == null, "custom valve hover UI removed");
        var mainType = mod.GetType("DVCabooseBreak.Main");
        var readMounts = mainType.GetMethod("ReadMountStates", flags);
        var writeLoose = mainType.GetMethod("WriteLooseMounts", flags);
        var instance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(caboose);
        caboose.GetField("guid", flags).SetValue(instance, "probe-car");
        Func<bool> hasHandle = () => (bool)caboose.GetProperty("HasHandle", flags).GetValue(instance);
        Func<bool> secured = () => (bool)caboose.GetProperty("IsSecured", flags).GetValue(instance);
        Func<float> position = () => (float)caboose.GetProperty("Position", flags).GetValue(instance);
        var positions = (System.Collections.IDictionary)mainType.GetField("Installed", flags).GetValue(null);
        readMounts.Invoke(null, new object[] { JObject.Parse("{'probe-car':0.7}"), JObject.Parse("{'probe-car':true}") });
        Check(hasHandle() && !secured() && position() == 0f, "loose mount restores visually present but cannot brake");
        var secureMount = mainType.GetMethod("SecureMount", flags);
        var unfastenMount = mainType.GetMethod("UnfastenMount", flags);
        Check((bool)secureMount.Invoke(null, new object[] { "probe-car" }) && secured(), "drilling secures the pre-hung handle");
        Check(!(bool)secureMount.Invoke(null, new object[] { "probe-car" }), "cannot secure an already secured handle twice");
        positions["probe-car"] = 0.5f;
        Check(!(bool)unfastenMount.Invoke(null, new object[] { "probe-car" }) && secured(), "hammer cannot unfasten an applied brake");
        positions["probe-car"] = 0f;
        Check((bool)unfastenMount.Invoke(null, new object[] { "probe-car" }) && hasHandle() && !secured(),
            "hammer only loosens the handle and leaves it visually mounted");
        var looseSaved = (JObject)writeLoose.Invoke(null, null);
        readMounts.Invoke(null, new object[] { JObject.Parse("{'probe-car':0}"), looseSaved });
        Check(hasHandle() && !secured(), "loosened state survives a save round trip");
        readMounts.Invoke(null, new object[] { JObject.Parse("{'probe-car':0.4}"), null });
        Check(secured() && Math.Abs(position() - 0.4f) < 0.001f, "older saves migrate existing handles as secured");
        readMounts.Invoke(null, new object[] { null, JObject.Parse("{'probe-car':true}") });
        Check(!hasHandle() && !secured(), "orphan loose-state entries cannot mint a handle");
        var interactor = game.GetType("DV.Customization.Gadgets.GadgetInteractor");
        Check(interactor.GetMethod("Update", flags).GetMethodBody() != null, "native tool query patch target");
        foreach (var name in new[] { "use", "vrInteractionPoint" })
            Check(interactor.GetField(name, flags) != null, "native tool bridge field " + name);
        var drillType = game.GetType("DV.Customization.Gadgets.DrillTool");
        foreach (var name in new[] { "TargetIsValid", "ProcessingProgress" })
            Check(drillType.GetProperty(name, flags).GetSetMethod(true) != null, "original drill effects input " + name);
        Check(game.GetType("DV.Customization.Gadgets.GadgetRemover").GetField("soundOnGadgetRemoved", flags) != null,
            "original hammer removal sound");
        Check(game.GetType("DV.Items.ItemWorkingAnimation").GetEvent("WorkStopped", flags) != null,
            "native tool completion/cancellation event");
        var spindle = caboose.GetField("Spindle", flags).GetValue(null);
        float mountingZ = (float)spindle.GetType().GetField("z").GetValue(spindle);
        // Original mesh measurements (metres), independently read with UnityPy.
        Check(Math.Abs(mountingZ - 0.0020270874f - (-4.806551f)) < 0.00001f,
            "DE2 rear mounting face contacts caboose valve within 0.01 mm");
        float released = (float)caboose.GetField("ReleasedAngle", flags).GetRawConstantValue();
        float applied = (float)caboose.GetField("AppliedAngle", flags).GetRawConstantValue();
        Check(released == 25f && applied == 75f, "requested released/applied angles and 50-degree travel");
        var center = caboose.GetField("InteractionCenter", flags).GetValue(null);
        var vecType = center.GetType();
        float cx = (float)vecType.GetField("x").GetValue(center);
        float cy = (float)vecType.GetField("y").GetValue(center);
        float cz = (float)vecType.GetField("z").GetValue(center);
        float radius = (float)caboose.GetField("InteractionRadius", flags).GetRawConstantValue();
        bool coversSweep = true;
        // Rotate every corner of the original DE2 mesh bounds through the whole
        // new travel (native mesh -> up is -90 degrees about Z).
        for (int step = 0; step <= 100; step++)
        foreach (float x in new[] { -0.184056f, 0.052983f })
        foreach (float y in new[] { -0.029381f, 0.029382f })
        foreach (float z in new[] { -0.002028f, 0.030942f })
        {
            double a = (-90 - released - (applied - released) * step / 100) * Math.PI / 180;
            double dx = x * Math.Cos(a) - y * Math.Sin(a) - cx;
            double dy = x * Math.Sin(a) + y * Math.Cos(a) - cy;
            double dz = z - cz;
            coversSweep &= dx * dx + dy * dy + dz * dz < radius * radius;
        }
        Check(coversSweep, "fixed interaction area covers full DE2 handle sweep");
        foreach (var type in mod.GetTypes())
            foreach (var method in type.GetMethods(flags | BindingFlags.DeclaredOnly))
                Check(method.GetMethodBody() != null || method.IsAbstract, "load method " + type.Name + "." + method.Name);

        var flow = brakeType.GetMethod("OneWayFlow", flags);
        // Charging an independent brake from the real 50 L auxiliary reservoir
        // into the real 10 L cylinder: no free air and no target overshoot.
        float aux = 6f, cylinder = 1f;
        for (int i = 0; i < 1000; i++)
        {
            var values = new object[] { 0.02f, aux, cylinder, 50f, 10f, 30f, 4.5f };
            flow.Invoke(null, values);
            aux = (float)values[1]; cylinder = (float)values[2];
        }
        Check(Math.Abs(cylinder - 4.5f) < 0.001f, "charged cylinder reaches 3.5 bar gauge");
        Check(Math.Abs(aux * 50 + cylinder * 10 - 310) < 0.01f, "air mass conserved while applying");
        Check(aux < 6f && cylinder <= 4.5001f, "application consumes auxiliary air and respects target");
        var noAir = new object[] { 0.02f, 1f, 1f, 50f, 10f, 30f, 4.5f };
        flow.Invoke(null, noAir);
        Check((float)noAir[2] == 1f, "empty reservoir cannot generate braking air");
        var vent = brakeType.GetMethod("VentToAtmosphere", flags);
        for (int i = 0; i < 1000; i++)
        {
            var values = new object[] { 0.02f, cylinder, 10f, 30f, 1f };
            vent.Invoke(null, values);
            cylinder = (float)values[1];
        }
        Check(Math.Abs(cylinder - 1f) < 0.001f, "release vents cylinder to atmosphere");
    }
}
