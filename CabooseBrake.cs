using System.Collections;
using System.Linq;
using DV.CabControls;
using DV.CabControls.Spec;
using DV.InventorySystem;
using DV.Interaction;
using DV.Shops;
using UnityEngine;

namespace DVCabooseBreak
{
    public sealed class CabooseBrake : MonoBehaviour
    {
        // Seat DE2's rear mounting face (model Z=-.002027) on the caboose
        // valve face (Z=-4.806551). The shaft tip is Z=-4.785331, not the seat.
        internal static readonly Vector3 Spindle = new Vector3(-0.885909f, 2.4713f, -4.804524f);
        internal const float ReleasedAngle = 25f;
        internal const float AppliedAngle = 75f;
        internal const float InteractionRadius = 0.22f;
        internal static readonly Vector3 InteractionCenter = new Vector3(0.045f, 0.055f, 0.015f);
        private TrainCar car;
        private LeverBase lever;
        private GameObject handle;
        private GameObject interactionArea;
        private string guid;
        public bool HasHandle => !string.IsNullOrEmpty(guid) && Main.Installed.ContainsKey(guid);
        public float Position => HasHandle ? Main.Installed[guid] : 0f;

        internal void Initialize(TrainCar train)
        {
            if (car == null)
            {
                car = train;
                Main.Brakes[car.brakeSystem] = this;
                car.InteriorLoaded += OnInterior;
            }
            guid = car.CarGUID;
            EnsureVisual();
        }

        private void OnInterior(GameObject interior)
        {
            if (interior != null) EnsureVisual();
            else { lever = null; handle = null; interactionArea = null; }
        }

        private void EnsureVisual()
        {
            if (car == null || !car.IsInteriorLoaded || !HasHandle || handle != null) return;
            handle = new GameObject("Caboose independent brake");
            handle.transform.SetParent(Main.Stage.transform, false);
            handle.transform.localPosition = Spindle;
            var model = HandleFactory.AddModel(handle.transform);
            // Seen from the cabin, the valve faces local +Z. A positive angle
            // about -Z turns up (+Y) towards screen left (+X): counterclockwise.
            // Start 25 degrees counterclockwise from up; another 50 degrees
            // gives the requested 75-degree fully applied position.
            handle.transform.localRotation = Quaternion.Euler(0, 0, -ReleasedAngle);
            var spec = handle.AddComponent<Lever>();
            spec.colliderGameObjects = new[] { model };
            spec.jointAxis = Vector3.back;
            spec.jointLimitMin = 0f;
            spec.jointLimitMax = AppliedAngle - ReleasedAngle;
            spec.useSteppedJoint = true;
            spec.steppedValueUpdate = true;
            spec.notches = 21;
            spec.useSpring = true;
            spec.jointSpring = 100f;
            spec.jointDamper = 10f;
            spec.rigidbodyMass = 1f;
            spec.scrollWheelHoverScroll = 1f;
            HandleFactory.CopyS060BrakeAudio(spec);
            // Match the native DE2/S060 StaticInteractionArea mechanism. This
            // sibling stays fixed to the valve instead of rotating with the arm.
            interactionArea = new GameObject("Caboose brake fixed interaction area");
            interactionArea.SetActive(false);
            interactionArea.transform.SetParent(car.loadedInterior.transform, false);
            interactionArea.transform.localPosition = Spindle;
            var areaCollider = interactionArea.AddComponent<SphereCollider>();
            areaCollider.center = InteractionCenter;
            areaCollider.radius = InteractionRadius;
            areaCollider.isTrigger = true;
            spec.nonVrStaticInteractionArea = interactionArea.AddComponent<StaticInteractionArea>();
            SetLayer(handle, LayerMask.NameToLayer("Interactable"));
            handle.transform.SetParent(car.loadedInterior.transform, false);
            lever = handle.GetComponent<LeverBase>();
            if (lever == null)
            {
                Main.Entry.Logger.Error("Native lever implementation was not created.");
                Destroy(interactionArea);
                Destroy(handle);
                return;
            }
            StartCoroutine(RestoreLever());
        }

        private IEnumerator RestoreLever()
        {
            // Native LeverBase builds its hinge on the next frame.
            yield return null;
            yield return null;
            if (lever == null || !HasHandle) yield break;
            lever.SetValue(Position);
            lever.ValueChanged += Changed;
        }

        private void Changed(ValueChangedEventArgs args)
        {
            if (!HasHandle) return;
            Main.Installed[guid] = Mathf.Clamp01(args.newValue);
            car.brakeSystem.independentBrakePosition = Position;
        }

        private static void SetLayer(GameObject go, int layer)
        {
            if (layer < 0) layer = 0;
            foreach (var tr in go.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = layer;
        }

        internal bool Install()
        {
            if (HasHandle || car == null || string.IsNullOrEmpty(guid) || !car.IsInteriorLoaded) return false;
            Main.Installed.Add(guid, 0f);
            EnsureVisual();
            if (lever == null)
            {
                Main.Installed.Remove(guid);
                return false;
            }
            Main.Entry.Logger.Log("Installed handle on " + guid);
            return true;
        }

        private void Update()
        {
            if (car == null) return;
            if (HasHandle) car.brakeSystem.independentBrakePosition = Position;
            if (!HasHandle || Position > 0.01f || !Input.GetKey(KeyCode.LeftControl) || !Input.GetKeyDown(KeyCode.F) || AimedSocket() != this) return;
            // Spawn before removing state; failed creation never loses a handle.
            var loose = Instantiate(HandleFactory.GetPrefab(), car.interior.TransformPoint(Spindle + new Vector3(0, 0, 0.25f)), car.interior.rotation);
            loose.GetComponent<InventoryItemSpec>().BelongsToPlayer = true;
            loose.GetComponent<ShopRestocker>().restockOnItemDestroyed = true;
            StorageController.Instance.AddItemToWorldStorage(loose.GetComponent<ItemBase>());
            Main.Installed.Remove(guid);
            car.brakeSystem.independentBrakePosition = 0f;
            if (lever != null) lever.ForceEndInteraction();
            if (interactionArea != null) Destroy(interactionArea);
            if (handle != null) Destroy(handle);
            lever = null;
        }

        internal static CabooseBrake AimedSocket()
        {
            var camera = PlayerManager.PlayerCamera;
            if (camera == null || Time.timeScale == 0) return null;
            var ray = new Ray(camera.transform.position, camera.transform.forward);
            return Main.Brakes.Values.FirstOrDefault(b => b != null && b.car != null && b.car.IsInteriorLoaded && b.SocketInSight(ray));
        }

        private bool SocketInSight(Ray ray)
        {
            var delta = car.interior.TransformPoint(Spindle) - ray.origin;
            var distance = Vector3.Dot(delta, ray.direction);
            if (distance <= 0 || distance > 2f || (delta - distance * ray.direction).sqrMagnitude > 0.0225f) return false;
            // Prevent installing through a wall; allow the valve's own collider.
            RaycastHit hit;
            return !Physics.Raycast(ray, out hit, Mathf.Max(0, distance - 0.12f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        private void OnGUI()
        {
            if (AimedSocket() != this) return;
            var text = HasHandle
                ? $"守车小闸 {Position:P0}  |  制动缸 {Mathf.Max(0, car.brakeSystem.brakeCylinderPressure - 1):F1} bar\n拖动 / 滚轮；缓解后 Ctrl+F 拆卸"
                : "守车制动阀：手持购买的制动拉杆，按 F 安装";
            GUI.Box(new Rect(Screen.width / 2f - 235, Screen.height / 2f + 45, 470, 55), text);
        }

        private void OnDestroy()
        {
            if (interactionArea != null) Destroy(interactionArea);
            if (car != null)
            {
                car.InteriorLoaded -= OnInterior;
                if (car.brakeSystem != null) Main.Brakes.Remove(car.brakeSystem);
            }
        }
    }
}
