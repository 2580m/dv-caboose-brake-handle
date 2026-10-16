using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;

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
            foreach (var dir in new[] { managed, Path.Combine(managed, "UnityModManager"), Path.GetDirectoryName(modPath) })
                if (File.Exists(Path.Combine(dir, name))) return Assembly.LoadFrom(Path.Combine(dir, name));
            return null;
        };
        try { Run(); Console.WriteLine(assertions + " assertions passed."); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
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
        var interaction = Assembly.LoadFrom(Path.Combine(managed, "DV.Interaction.dll"));
        Check(interaction.GetType("DV.Interaction.StaticInteractionArea").GetMethod("Initialize", flags) != null,
            "native fixed interaction area initialization");
        var specAssembly = Assembly.LoadFrom(Path.Combine(managed, "DV.CabControls.Spec.dll"));
        var leverSpec = specAssembly.GetType("DV.CabControls.Spec.Lever");
        foreach (var field in new[] { "nonVrStaticInteractionArea", "notch", "drag", "limitHit" })
            Check(leverSpec.GetField(field, flags) != null, "native lever area/audio field " + field);
        var caboose = mod.GetType("DVCabooseBreak.CabooseBrake");
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
