using System;
using DV.CabControls;
using DV.CabControls.Spec;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace DVCabooseBreak
{
    public sealed class BrakeSettings : UnityModManager.ModSettings
    {
        public bool InvertMouseWheel = false;
        public float MouseWheelSensitivity = 1f;
        internal static float ClampSensitivity(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? 1f : Math.Max(0.1f, Math.Min(5f, value));
        public override void Save(UnityModManager.ModEntry entry) => Save(this, entry);
    }

    // Attached only to the lever we create, never to a locomotive control.
    public sealed class CabooseBrakeScrollScope : MonoBehaviour
    {
        internal readonly WheelStepAccumulator Wheel = new WheelStepAccumulator();
    }

    // Preserve the native physical notches at slow settings: 0.5x means one
    // notch per two wheel ticks, rather than repeatedly snapping half a notch back.
    internal sealed class WheelStepAccumulator
    {
        private float pending;
        private float lastSensitivity = 1f;
        internal int Consume(ScrollAction action, float sensitivity)
        {
            if (action != ScrollAction.ScrollUp && action != ScrollAction.ScrollDown) return 0;
            sensitivity = BrakeSettings.ClampSensitivity(sensitivity);
            if (sensitivity != lastSensitivity) { pending = 0f; lastSensitivity = sensitivity; }
            pending += (action == ScrollAction.ScrollUp ? 1f : -1f) * sensitivity;
            // Avoid losing the final step to floating point drift at e.g. 0.7x.
            float nearest = (float)Math.Round(pending);
            if (Math.Abs(pending - nearest) < 0.00001f) pending = nearest;
            int steps = (int)pending;
            pending -= steps;
            return steps;
        }
    }

    [HarmonyPatch(typeof(LeverBase), nameof(LeverBase.Scroll))]
    internal static class CabooseMouseScrollPatch
    {
        internal struct ScrollRestore
        {
            internal Lever Spec;
            internal float Speed;
        }

        static bool Prefix(LeverBase __instance, ref ScrollAction action, ScrollSource source, out ScrollRestore __state)
        {
            __state = default;
            if (source != ScrollSource.Mouse || (action != ScrollAction.ScrollUp && action != ScrollAction.ScrollDown)) return true;
            var scope = __instance.GetComponent<CabooseBrakeScrollScope>();
            if (scope == null) return true;
            action = Map(action, source, true, Main.Settings.InvertMouseWheel);
            int steps = scope.Wheel.Consume(action, Main.Settings.MouseWheelSensitivity);
            if (steps == 0) return false;
            action = steps > 0 ? ScrollAction.ScrollUp : ScrollAction.ScrollDown;
            var spec = __instance.GetComponent<Lever>();
            __state = new ScrollRestore { Spec = spec, Speed = spec.scrollWheelHoverScroll };
            spec.scrollWheelHoverScroll *= Math.Abs(steps);
            return true;
        }

        // Restore even on an exception. HUD, drag and later input events always
        // see the unmodified native lever specification.
        static Exception Finalizer(Exception __exception, ScrollRestore __state)
        {
            if (__state.Spec != null) __state.Spec.scrollWheelHoverScroll = __state.Speed;
            return __exception;
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
