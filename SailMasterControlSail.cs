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
        private static readonly System.Reflection.FieldInfo unamplifiedForwardForceField = AccessTools.Field(typeof(Sail), "unamplifiedForwardForce");
        private static readonly System.Reflection.FieldInfo unamplifiedSidewayForceField = AccessTools.Field(typeof(Sail), "unamplifiedSidewayForce");
        private static readonly System.Reflection.FieldInfo totalWindForceField = AccessTools.Field(typeof(Sail), "totalWindForce");
        private const float trimStep = 0.002f;

        private Sail sail;
        private RopeController hoistWinch;
        private GPButtonRopeWinch hoistButton;
        private readonly List<TrimPoint> trimPoints = new List<TrimPoint>();
        private readonly List<TrimControl> trimControls = new List<TrimControl>();
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

        public IReadOnlyList<TrimPoint> TrimPoints => trimPoints;

        public IReadOnlyList<TrimControl> TrimControls => trimControls;

        public float DeployedAmount
        {
            get
            {
                if (!IsReady) return 0f;
                return reverseReefing ? 1f - hoistWinch.currentLength : hoistWinch.currentLength;
            }
        }

        public float? Efficiency
        {
            get
            {
                if (sail == null) return null;

                float totalWindForce = GetTotalWindForce();
                if (Mathf.Approximately(totalWindForce, 0f)) return null;

                float forwardForce = (float)unamplifiedForwardForceField.GetValue(sail);
                float forwardPercent = Mathf.Round(forwardForce / totalWindForce * 100f);
                if (forwardPercent <= 0f) return forwardPercent;

                float sideForce = (float)unamplifiedSidewayForceField.GetValue(sail);
                float sidePercent = Mathf.Abs(Mathf.Round(sideForce / totalWindForce * 100f));
                return Mathf.Round((forwardPercent + (100f - sidePercent)) / 2f);
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

        private float GetTotalWindForce()
        {
            float applied = sail.appliedWindForce;
            if (!Mathf.Approximately(applied, 0f))
            {
                float capturedFraction = sail.GetCapturedForceFraction();
                if (!Mathf.Approximately(capturedFraction, 0f))
                {
                    return applied / capturedFraction;
                }
            }

            return (float)totalWindForceField.GetValue(sail);
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
                }
                else if (IsAngleWinch(button.rope, sail))
                {
                    trimPoints.Add(new TrimPoint(button, GetTrimPointSortOrder(button.rope)));
                }
            }

            LabelTrimPoints();

            if (hoistWinch == null || hoistButton == null)
            {
                SailMasterMain.Logger?.LogDebug($"No reef winch found for sail {SafeSailName()}.");
                return;
            }

            reverseReefing = (bool)Traverse.Create(hoistWinch).Field("reverseReefing").GetValue();
            boat = Traverse.Create(hoistButton).Field("boat").GetValue<PurchasableBoat>();
        }

        private static bool IsAngleWinch(RopeController rope, Sail sail)
        {
            if (rope == null) return false;

            bool isAngleWinch =
                rope is RopeControllerSailAngle ||
                rope is RopeControllerSailAngleJib ||
                rope is RopeControllerSailAngleSquare;
            if (!isAngleWinch) return false;

            return Traverse.Create(rope).Field("sail").GetValue<Sail>() == sail;
        }

        private static int GetTrimPointSortOrder(RopeController rope)
        {
            if (rope is RopeControllerSailAngleJib jib)
            {
                return jib.side == RopeControllerSailAngleJib.JibWinch.left ? 0 : 1;
            }

            if (rope is RopeControllerSailAngleSquare square)
            {
                return square.side == RopeControllerSailAngleSquare.WinchSide.left ? 0 : 1;
            }

            return 0;
        }

        private void LabelTrimPoints()
        {
            trimPoints.Sort((left, right) => left.SortOrder.CompareTo(right.SortOrder));
            trimControls.Clear();

            if (trimPoints.Count == 1)
            {
                trimPoints[0].SetLabel("Trim");
                trimControls.Add(new TrimControl("Trim", trimPoints[0]));
                return;
            }

            if (sail != null && sail.category == SailCategory.square && trimPoints.Count >= 2)
            {
                trimControls.Add(new TrimControl("Trim", trimPoints[0], trimPoints[1]));
                return;
            }

            for (int i = 0; i < trimPoints.Count; i++)
            {
                if (i == 0)
                {
                    trimPoints[i].SetLabel("Port");
                }
                else if (i == 1)
                {
                    trimPoints[i].SetLabel("Starboard");
                }
                else
                {
                    trimPoints[i].SetLabel($"Trim {i + 1}");
                }

                trimControls.Add(new TrimControl(trimPoints[i].Label, trimPoints[i]));
            }
        }

        private void Update()
        {
            if (!IsReady) return;

            if (targetDeployedAmount.HasValue)
            {
                MoveTowardTarget();
            }

            foreach (var trimControl in trimControls)
            {
                trimControl.MoveTowardTarget();
            }
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

        public class TrimPoint
        {
            private readonly GPButtonRopeWinch button;

            internal TrimPoint(GPButtonRopeWinch button, int sortOrder)
            {
                this.button = button;
                SortOrder = sortOrder;
                Label = "Trim";
            }

            internal int SortOrder { get; }

            public string Label { get; private set; }

            public float Amount => button == null || button.rope == null ? 0f : Mathf.Clamp01(button.rope.currentLength);

            internal void SetLabel(string label)
            {
                Label = label;
            }

            public void Pull()
            {
                ApplyTrim(true);
            }

            public void Release()
            {
                ApplyTrim(false);
            }

            public void MoveTowardAmount(float amount)
            {
                if (button == null || button.rope == null) return;

                float current = Amount;
                float target = Mathf.Clamp01(amount);
                if (Mathf.Approximately(current, target)) return;

                float next = Mathf.MoveTowards(current, target, trimStep);
                ApplyAmount(next, next < current);
            }

            private void ApplyAmount(float amount, bool pull)
            {
                if (button == null || button.rope == null) return;
                if (pull && !button.rope.CanPull()) return;

                Traverse.Create(button).Field("currentInput").SetValue(pull ? 25f : -25f);
                button.ApplyRotation();

                button.rope.currentLength = Mathf.Clamp01(amount);
                button.rope.changed = true;
            }

            private void ApplyTrim(bool pull)
            {
                if (button == null || button.rope == null) return;

                if (pull && !button.rope.CanPull()) return;

                Traverse.Create(button).Field("currentInput").SetValue(pull ? 25f : -25f);
                button.ApplyRotation();

                if (pull)
                {
                    button.rope.currentLength -= trimStep;
                }
                else
                {
                    button.rope.currentLength += trimStep;
                }

                button.rope.currentLength = Mathf.Clamp01(button.rope.currentLength);
                button.rope.changed = true;
            }

        }

        public class TrimControl
        {
            private readonly TrimPoint primary;
            private readonly TrimPoint inverse;
            private float? targetAmount;

            internal TrimControl(string label, TrimPoint primary, TrimPoint inverse = null)
            {
                Label = label;
                this.primary = primary;
                this.inverse = inverse;
            }

            public string Label { get; }

            public float Amount => primary == null ? 0f : primary.Amount;

            public void Pull()
            {
                targetAmount = null;
                primary?.Pull();
                inverse?.Release();
            }

            public void Release()
            {
                targetAmount = null;
                primary?.Release();
                inverse?.Pull();
            }

            public void SetAmount(float amount)
            {
                targetAmount = Mathf.Clamp01(amount);
            }

            internal void MoveTowardTarget()
            {
                if (!targetAmount.HasValue) return;

                float target = targetAmount.Value;
                primary?.MoveTowardAmount(target);
                inverse?.MoveTowardAmount(1f - target);

                if (Mathf.Approximately(Amount, target))
                {
                    targetAmount = null;
                }
            }
        }
    }
}
