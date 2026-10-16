using System;
using System.Linq;
using System.Reflection;
using DV.CabControls;
using DV.Common;
using DV.Customization.Gadgets;
using DV.InventorySystem;
using DV.Items;
using DV.Player;
using HarmonyLib;
using UnityEngine;

namespace DVCabooseBreak
{
    // Only intercept the native tool query over our own valve. Ordinary gadget
    // drilling/hammering and the game's input bindings retain their native path.
    [HarmonyPatch(typeof(GadgetInteractor), "Update")]
    internal static class ToolQueryPatch
    {
        static bool Prefix(GadgetInteractor __instance)
        {
            if (!(__instance is DrillTool) && !(__instance is GadgetRemover)) return true;
            var adapter = __instance.GetComponent<CabooseToolAdapter>();
            var target = CabooseBrake.AimedSocket();
            if (adapter == null)
            {
                if (target == null || !__instance.IsGrabbed) return true;
                adapter = __instance.gameObject.AddComponent<CabooseToolAdapter>();
                adapter.Initialize(__instance);
            }
            return !adapter.HandleQuery(target);
        }
    }

    public sealed class CabooseToolAdapter : MonoBehaviour, ItemPositionController.IPositionProvider
    {
        private static readonly FieldInfo UseField = AccessTools.Field(typeof(GadgetInteractor), "use");
        private static readonly FieldInfo TipField = AccessTools.Field(typeof(GadgetInteractor), "vrInteractionPoint");
        private static readonly PropertyInfo ValidProperty = AccessTools.Property(typeof(DrillTool), "TargetIsValid");
        private static readonly PropertyInfo ProgressProperty = AccessTools.Property(typeof(DrillTool), "ProcessingProgress");
        private GadgetInteractor tool;
        private DrillTool drill;
        private GadgetRemover hammer;
        private ItemWorkingAnimation animation;
        private ItemWorkingAnimation nativeAnimation;
        private CabooseBrake target;
        private Transform tip;
        private bool requireRelease;
        private bool completed;
        public int Priority => 1;

        internal void Initialize(GadgetInteractor source)
        {
            tool = source;
            drill = source as DrillTool;
            hammer = source as GadgetRemover;
            nativeAnimation = drill != null ? drill.itemWorkingAnimation : hammer.itemWorkingAnimation;
            tip = (Transform)TipField.GetValue(tool);
            // Use a separate native animation driver so the original tool's
            // gadget-specific callbacks never receive a fake Drillable/Gadget.
            animation = gameObject.AddComponent<ItemWorkingAnimation>();
            animation.moveInTime = Mathf.Max(0.05f, nativeAnimation.moveInTime);
            animation.moveOutTime = Mathf.Max(0.05f, nativeAnimation.moveOutTime);
            animation.minWorkTime = drill != null ? Mathf.Max(0.1f, drill.processingTime) : Mathf.Max(0.75f, nativeAnimation.minWorkTime);
            animation.InputPressedCallback = () => OperationValid() && tool.IsPressed;
            animation.AnimationStarted += () => { if (!VRManager.IsVREnabled()) ItemPositionController.Instance.Add(this); };
            animation.AnimationStopped += StopEffects;
            animation.WorkStopped += FinishWork;
        }

        internal bool HandleQuery(CabooseBrake aimed)
        {
            if (nativeAnimation.IsAnimating) return false;
            if (hammer != null && !GameFeatureFlags.IsAllowed(GameFeatureFlags.Flag.HammeringGadgets)) return false;
            if (!tool.IsPressed) requireRelease = false;
            if (animation.IsAnimating)
            {
                UseField.SetValue(tool, false);
                SetDrillEffects(animation.IsWorking && OperationValid(), animation.WorkProgress);
                return true;
            }
            if (aimed == null || !tool.IsGrabbed) return false;
            UseField.SetValue(tool, false);
            target = aimed;
            bool valid;
            if (drill != null)
            {
                valid = aimed.HasHandle && !aimed.IsSecured;
                GadgetInteractor.ShowInteractionTextLMB(L10n.Get(aimed.IsSecured ? "tool.already_secured" :
                    valid ? "tool.secure" : "tool.place_first"), false);
            }
            else
            {
                valid = aimed.IsSecured && aimed.Position <= 0.01f;
                GadgetInteractor.ShowInteractionTextLMB(L10n.Get(!aimed.HasHandle ? "tool.no_handle" :
                    !aimed.IsSecured ? "tool.pick_up" :
                    valid ? "tool.unfasten" : "tool.release_first"), false);
            }
            if (valid && tool.IsPressed && !requireRelease)
            {
                completed = false;
                requireRelease = true;
                animation.StartAnimating();
            }
            return true;
        }

        private bool OperationValid()
        {
            if (completed || tool == null || !tool.IsGrabbed || target == null || CabooseBrake.AimedSocket() != target) return false;
            return drill != null ? target.HasHandle && !target.IsSecured : target.IsSecured && target.Position <= 0.01f;
        }

        private void FinishWork()
        {
            if (animation.WorkProgress < 1f || !OperationValid()) return;
            if (drill != null)
            {
                if (!target.Secure()) return;
            }
            else
            {
                if (!target.Unfasten()) return;
                hammer.soundOnGadgetRemoved?.Play(transform.position);
            }
            completed = true;
            SetDrillEffects(false, 0);
        }

        private void SetDrillEffects(bool working, float progress)
        {
            if (drill == null) return;
            // Original DrillEffects reads these to play its real motor/contact
            // audio and spin the original drill bit during our operation.
            ValidProperty.SetValue(drill, working, null);
            ProgressProperty.SetValue(drill, progress, null);
        }

        private void StopEffects()
        {
            if (!VRManager.IsVREnabled()) ItemPositionController.Instance.Remove(this);
            SetDrillEffects(false, 0);
        }

        public (Vector3 pos, Quaternion rot, float overridePreviousPerc) GetPose(Vector3 pos, Quaternion rot)
        {
            if (target == null || animation == null || !animation.IsAnimating) return (pos, rot, 0);
            var pose = TransformUtils.CalculateAlignmentTargets(ItemPositionController.Instance.itemAnchor,
                target.ToolPosition, target.ToolRotation, tip);
            return (pose.targetPosition, pose.targetRotation, ItemWorkingAnimation.EaseInOutCubic(animation.MoveToWorkProgress));
        }

        private void OnDisable()
        {
            if (animation != null) animation.StopAnimating();
        }

        private void OnDestroy()
        {
            if (animation != null)
            {
                animation.StopAnimating();
                Destroy(animation);
            }
        }
    }
}
