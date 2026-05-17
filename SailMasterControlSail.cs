using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterControlSail : MonoBehaviour
    {
        private static readonly List<SailMasterControlSail> controllers = new List<SailMasterControlSail>();

        private Sail sail;
        private RopeController hoistWinch;
        private GPButtonRopeWinch hoistButton;
        private PurchasableBoat boat;
        private bool reverseReefing;
        private float? targetDeployedAmount;

        public static void CommandGroup(SailCategory? category)
        {
            var targets = controllers
                .Where(controller => controller != null && controller.IsReady && controller.CanControl)
                .Where(controller => !category.HasValue || controller.sail.category == category.Value)
                .ToList();

            if (targets.Count == 0)
            {
                SailMasterMain.Logger?.LogDebug($"No controllable sails found for {FormatCategoryName(category)}.");
                return;
            }

            CommandSails(targets, FormatCategoryName(category));
        }

        public static void CommandSails(IEnumerable<SailMasterControlSail> sails, string groupName)
        {
            var targets = sails
                .Where(controller => controller != null && controller.IsReady && controller.CanControl)
                .ToList();

            if (targets.Count == 0)
            {
                SailMasterMain.Logger?.LogDebug($"No controllable sails found for {groupName}.");
                return;
            }

            float targetAmount = targets.Any(controller => controller.IsDeployed) ? 0f : 1f;
            foreach (var target in targets)
            {
                target.SetTargetDeployedAmount(targetAmount);
            }

            string command = targetAmount <= 0f ? "Lower" : "Raise";
            SailMasterMain.Logger?.LogDebug($"{command} {targets.Count} {groupName} sail(s).");
        }

        public static List<SailMasterControlSail> GetControllableSails()
        {
            return controllers
                .Where(controller => controller != null && controller.IsReady && controller.CanControl)
                .OrderBy(controller => controller.CategoryName)
                .ThenBy(controller => controller.DisplayName)
                .ThenBy(controller => controller.GetInstanceID())
                .ToList();
        }

        private static string FormatCategoryName(SailCategory? category)
        {
            return category.HasValue ? category.Value.ToString() : "all";
        }

        public string DisplayName => SafeSailName();

        public string CategoryName => sail == null ? "unknown" : sail.category.ToString();

        public float DeployedAmount
        {
            get
            {
                if (!IsReady) return 0f;
                return reverseReefing ? 1f - hoistWinch.currentLength : hoistWinch.currentLength;
            }
        }

        private bool IsReady => sail != null && hoistWinch != null && hoistButton != null && boat != null;

        private bool CanControl
        {
            get
            {
                if (!IsReady || !boat.isPurchased()) return false;

                if (GameState.currentBoat != null)
                {
                    return GameState.currentBoat.IsChildOf(boat.transform);
                }

                if (GameState.lastBoat != null)
                {
                    return GameState.lastBoat.IsChildOf(boat.transform);
                }

                return false;
            }
        }

        private bool IsDeployed
        {
            get
            {
                if (!IsReady) return false;

                float length = hoistWinch.currentLength;
                return reverseReefing ? length < 1f : length > 0f;
            }
        }

        private void Awake()
        {
            controllers.Add(this);
        }

        private void OnDestroy()
        {
            controllers.Remove(this);
        }

        private void Start()
        {
            sail = GetComponent<Sail>();
            if (sail == null) return;

            foreach (GPButtonRopeWinch button in FindObjectsOfType<GPButtonRopeWinch>())
            {
                if (button.rope is RopeControllerSailReef reefWinch && reefWinch.sail == sail)
                {
                    hoistButton = button;
                    hoistWinch = button.rope;
                    break;
                }
            }

            if (hoistWinch == null || hoistButton == null)
            {
                SailMasterMain.Logger?.LogDebug($"No reef winch found for sail {SafeSailName()}.");
                return;
            }

            reverseReefing = (bool)Traverse.Create(hoistWinch).Field("reverseReefing").GetValue();
            boat = Traverse.Create(hoistButton).Field("boat").GetValue<PurchasableBoat>();
        }

        private void Update()
        {
            if (!IsReady || !targetDeployedAmount.HasValue) return;

            MoveTowardTarget();
        }

        private void MoveTowardTarget()
        {
            float target = Mathf.Clamp01(targetDeployedAmount.Value);
            float current = DeployedAmount;
            float next = Mathf.MoveTowards(current, target, SailMasterMain.hoistingSpeed.Value);

            ApplyDeployedAmount(next);

            float visualInput = next >= current ? GetLengthDirection(true) : GetLengthDirection(false);
            Traverse.Create(hoistButton).Field("currentInput").SetValue(visualInput < 0f ? 25f : -25f);
            hoistButton.ApplyRotation();

            if (Mathf.Approximately(next, target))
            {
                targetDeployedAmount = null;
            }
        }

        public void SetTargetDeployedAmount(float amount)
        {
            if (!IsReady) return;

            targetDeployedAmount = Mathf.Clamp01(amount);
        }

        private void ApplyDeployedAmount(float amount)
        {
            hoistWinch.currentLength = reverseReefing ? 1f - amount : amount;
            hoistWinch.currentLength = Mathf.Clamp01(hoistWinch.currentLength);
            hoistWinch.changed = true;
        }

        private float GetLengthDirection(bool raising)
        {
            if (reverseReefing)
            {
                return raising ? -1f : 1f;
            }

            return raising ? 1f : -1f;
        }

        private string SafeSailName()
        {
            try
            {
                return string.IsNullOrEmpty(sail.sailName) ? sail.category.ToString() : sail.sailName;
            }
            catch (Exception)
            {
                return "unknown";
            }
        }
    }
}
