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
        private HoistCommand currentCommand = HoistCommand.None;

        private enum HoistCommand
        {
            None,
            Raise,
            Lower
        }

        public static void CommandGroup(SailCategory? category)
        {
            var targets = controllers
                .Where(controller => controller != null && controller.IsReady && controller.CanControl)
                .Where(controller => !category.HasValue || controller.sail.category == category.Value)
                .ToList();

            if (targets.Count == 0)
            {
                SailMasterMain.Logger?.LogDebug($"No controllable sails found for {CategoryName(category)}.");
                return;
            }

            HoistCommand command = targets.Any(controller => controller.IsDeployed)
                ? HoistCommand.Lower
                : HoistCommand.Raise;

            foreach (var target in targets)
            {
                target.currentCommand = command;
            }

            SailMasterMain.Logger?.LogDebug($"{command} {targets.Count} {CategoryName(category)} sail(s).");
        }

        private static string CategoryName(SailCategory? category)
        {
            return category.HasValue ? category.Value.ToString() : "all";
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
            if (!IsReady || currentCommand == HoistCommand.None) return;

            bool raising = currentCommand == HoistCommand.Raise;
            PerformHoist(raising);
        }

        private void PerformHoist(bool raising)
        {
            float previousLength = hoistWinch.currentLength;
            float direction = GetLengthDirection(raising);

            Traverse.Create(hoistButton).Field("currentInput").SetValue(direction < 0f ? 25f : -25f);
            hoistButton.ApplyRotation();

            hoistWinch.currentLength += direction * SailMasterMain.hoistingSpeed.Value;
            hoistWinch.currentLength = Mathf.Clamp01(hoistWinch.currentLength);
            hoistWinch.changed = true;

            if (Mathf.Approximately(previousLength, hoistWinch.currentLength) || HasReachedTarget(raising))
            {
                currentCommand = HoistCommand.None;
            }
        }

        private float GetLengthDirection(bool raising)
        {
            if (reverseReefing)
            {
                return raising ? -1f : 1f;
            }

            return raising ? 1f : -1f;
        }

        private bool HasReachedTarget(bool raising)
        {
            if (reverseReefing)
            {
                return raising ? hoistWinch.currentLength <= 0f : hoistWinch.currentLength >= 1f;
            }

            return raising ? hoistWinch.currentLength >= 1f : hoistWinch.currentLength <= 0f;
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
