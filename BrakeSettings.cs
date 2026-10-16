using DV.CabControls;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DVCabooseBreak
{
    public sealed class BrakeSettings : UnityModManager.ModSettings
    {
        public bool InvertMouseWheel = false;
        public override void Save(UnityModManager.ModEntry entry) => Save(this, entry);
    }

    // Attached only to the lever we create, never to a locomotive control.
    public sealed class CabooseBrakeScrollScope : MonoBehaviour { }

    [HarmonyPatch(typeof(LeverBase), nameof(LeverBase.Scroll))]
    internal static class CabooseMouseScrollPatch
    {
        static void Prefix(LeverBase __instance, ref ScrollAction action, ScrollSource source)
        {
            if (!Main.Settings.InvertMouseWheel || source != ScrollSource.Mouse) return;
            action = Map(action, source, __instance.GetComponent<CabooseBrakeScrollScope>() != null, true);
        }

        internal static ScrollAction Map(ScrollAction action, ScrollSource source, bool caboose, bool invert)
        {
            if (!invert || !caboose || source != ScrollSource.Mouse) return action;
            if (action == ScrollAction.ScrollUp) return ScrollAction.ScrollDown;
            if (action == ScrollAction.ScrollDown) return ScrollAction.ScrollUp;
            return action;
        }
    }
}
