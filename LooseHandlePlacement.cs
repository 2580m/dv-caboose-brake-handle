using DV.CabControls;
using DV.InventorySystem;
using DV.Shops;
using UnityEngine;

namespace DVCabooseBreak
{
    public sealed class LooseHandlePlacement : MonoBehaviour
    {
        private ItemBase item;
        private bool placed;
        private void Start()
        {
            item = GetComponent<ItemBase>();
            if (item != null) item.Used += Place;
        }

        private void Place()
        {
            if (placed || item == null || !item.IsGrabbed() || !item.InventorySpecs.BelongsToPlayer) return;
            var valve = CabooseBrake.AimedSocket();
            if (valve == null || valve.HasHandle || !valve.PlaceLoosely()) return;
            placed = true;
            // Ownership moves from the loose item to the saved valve mount;
            // neither drilling nor hammering creates or consumes another item.
            var restocker = GetComponent<ShopRestocker>();
            if (restocker != null) restocker.restockOnItemDestroyed = false;
            item.ForceEndInteraction();
            Inventory.Instance.PurgeFromInventory(gameObject);
            StorageController.Instance.RemoveItemFromStorageItemList(item);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (item != null) item.Used -= Place;
        }
    }
}
