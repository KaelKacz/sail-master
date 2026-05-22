using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterNavigationController : MonoBehaviour
    {
        private const float waypointArrivalDistanceNm = 0.25f;
        private const float metersPerSecondToKnots = 1.94384f;
        private const float nauticalMilesPerDegreeLatitude = 60f;
        private const float headingUpdateInterval = 0.05f;
        private const float steeringWheelResolveInterval = 2f;
        private const float manualWheelInputSpeed = 90f;
        private const string routePointColor = "orangepoint";

        private static readonly List<SailMasterNavigationController> controllers = new List<SailMasterNavigationController>();
        private static readonly FieldInfo steeringWheelRudderField = AccessTools.Field(typeof(GPButtonSteeringWheel), "rudder");
        private static readonly FieldInfo steeringWheelLockedField = AccessTools.Field(typeof(GPButtonSteeringWheel), "locked");
        private static readonly MethodInfo applyRudderRotationMethod = AccessTools.Method(typeof(GPButtonSteeringWheel), "ApplyRudderRotation");
        private static readonly MethodInfo applyWheelRotationFromRudderMethod = AccessTools.Method(typeof(GPButtonSteeringWheel), "ApplyWheelRotationFromRudder");
        private static readonly MethodInfo lockMethod = AccessTools.Method(typeof(GPButtonSteeringWheel), "Lock");
        private static readonly MethodInfo unlockMethod = AccessTools.Method(typeof(GPButtonSteeringWheel), "Unlock");

        private Transform boat;
        private Rigidbody shipRigidbody;
        private HingeJoint rudderJoint;
        private Rudder rudder;
        private GPButtonSteeringWheel steeringWheel;
        private readonly List<GPButtonSteeringWheel> relatedSteeringWheels = new List<GPButtonSteeringWheel>();
        private float currentInputMax;
        private float targetHeading;
        private float targetRudderInput;
        private float targetRudderAngle;
        private float manualRudderWheelTargetReachedTime;
        private float nextSteeringWheelResolveTime;
        private bool headingLockActive;
        private bool routeActive;
        private bool manualRudderActive;
        private bool steeringWheelLockedBySailMaster;
        private readonly List<Vector2> waypoints = new List<Vector2>();
        private int waypointIndex;
        private float integral;
        private float lastError;
        private float lastTime;
        private string status = "Navigation controller ready.";

        public bool CanControl { get; private set; }
        public bool IsReady => rudder != null && steeringWheel != null && boat != null;
        public bool HeadingLockActive => headingLockActive;
        public bool RouteActive => routeActive;
        public bool ManualRudderActive => manualRudderActive;
        public bool HelmLocked => IsAnyRelatedSteeringWheelLocked() && !HelmHeldBySailMaster;
        public bool HelmHeldBySailMaster => steeringWheelLockedBySailMaster
            && IsSteeringWheelLocked()
            && (headingLockActive || routeActive || manualRudderActive);
        public string HelmControlStatus => HelmHeldBySailMaster
            ? "SailMaster has helm control."
            : "SailMaster is not controlling the helm.";
        public float ManualRudderInput => CurrentRudderInput;
        public float CurrentRudderInput => MaxRudderAngle > 0f ? Mathf.Clamp(-RudderAngle / MaxRudderAngle, -1f, 1f) : 0f;
        public float TargetRudderInput => targetRudderInput;
        public int WaypointCount => waypoints.Count;
        public int CurrentWaypointNumber => routeActive ? waypointIndex + 1 : 0;
        public bool HasCurrentWaypoint => waypointIndex >= 0 && waypointIndex < waypoints.Count;
        public string Status => status;
        public float CurrentHeading => IsReady ? NormalizeHeading360(BoatHeading()) : 0f;
        public float TargetHeading => NormalizeHeading360(targetHeading);
        public float RudderAngle => rudder != null ? rudder.currentAngle : 0f;
        public Vector2 Coordinates => IsReady ? CurrentCoordinates() : Vector2.zero;
        public Vector2 CurrentWaypoint => HasCurrentWaypoint ? waypoints[waypointIndex] : Vector2.zero;
        public float CurrentWaypointDistanceNm => HasCurrentWaypoint ? CalculateGlobeDistanceNm(Coordinates, CurrentWaypoint) : 0f;
        public float BoatSpeedKnots => shipRigidbody != null ? shipRigidbody.velocity.magnitude * metersPerSecondToKnots : 0f;
        public float TrueWindSpeed => Wind.currentWind.magnitude;
        public float TrueWindDirection => NormalizeHeading360(Vector3.SignedAngle(Wind.currentWind, Vector3.forward, -Vector3.up));
        public float ApparentWindSpeed => ApparentWind().magnitude;
        public float ApparentWindAngle => GetApparentWindAngle();
        public string DebugStatus => $"Ready {IsReady}  Can {CanControl}  Input {(steeringWheel != null ? steeringWheel.currentInput : 0f):F1}  Max {currentInputMax:F1}";
        private float MaxRudderAngle => rudderJoint != null ? Mathf.Max(1f, Mathf.Abs(rudderJoint.limits.max)) : 0f;

        private void Awake()
        {
            rudder = GetComponent<Rudder>();
            if (rudder == null) return;

            ResolveSteeringWheel();
            controllers.Add(this);
        }

        private void OnDestroy()
        {
            controllers.Remove(this);
        }

        private void Update()
        {
            using (SailMasterProfiler.Scope("SailMaster/Navigation/Update"))
            {
                if (!ResolveSteeringWheel()) return;

                UpdateCanControl();
                if (!CanControl)
                {
                    ReleaseSteeringWheel();
                    return;
                }

                if (routeActive)
                {
                    using (SailMasterProfiler.Scope("SailMaster/Navigation/RouteTarget"))
                    {
                        UpdateRouteTarget();
                    }
                }

                if (headingLockActive || routeActive)
                {
                    LockSteeringWheel();
                    ApplySteering(BoatHeading(), targetHeading);
                }
                else if (manualRudderActive)
                {
                    LockSteeringWheel();
                    ApplyRudderTarget();
                }
                else
                {
                    ReleaseSteeringWheel();
                }

            }
        }

        private bool ResolveSteeringWheel()
        {
            if (rudder == null)
            {
                rudder = GetComponent<Rudder>();
                if (rudder == null) return false;
            }

            if (boat == null)
            {
                boat = GetComponentInParent<PurchasableBoat>()?.transform;
            }

            if (boat != null && shipRigidbody == null)
            {
                shipRigidbody = boat.GetComponent<Rigidbody>();
                RefreshRelatedSteeringWheels();
            }

            if (steeringWheel != null && rudderJoint != null && currentInputMax > 0f)
            {
                return IsReady;
            }

            if (Time.time < nextSteeringWheelResolveTime)
            {
                return IsReady;
            }

            nextSteeringWheelResolveTime = Time.time + steeringWheelResolveInterval;
            foreach (var button in FindObjectsOfType<GPButtonSteeringWheel>())
            {
                var buttonRudder = steeringWheelRudderField?.GetValue(button) as Rudder;
                if (buttonRudder != rudder) continue;

                steeringWheel = button;
                rudderJoint = button.attachedRudder != null ? button.attachedRudder : rudder.GetComponent<HingeJoint>();
                RefreshCurrentInputMax();
                RefreshRelatedSteeringWheels();
                break;
            }

            return IsReady;
        }

        private void RefreshCurrentInputMax()
        {
            if (rudderJoint == null || steeringWheel == null) return;

            currentInputMax = rudderJoint.limits.max * steeringWheel.gearRatio;
            if (currentInputMax <= 0f)
            {
                currentInputMax = Mathf.Max(25f, Mathf.Abs(rudderJoint.limits.max));
            }
        }

        public static SailMasterNavigationController GetCurrent()
        {
            return controllers.FirstOrDefault(controller => controller != null && controller.IsReady && controller.CanControl && controller.IsSailMasterSteeringActive())
                ?? controllers.FirstOrDefault(controller => controller != null && controller.IsReady && controller.CanControl && controller.HelmLocked)
                ?? controllers.FirstOrDefault(controller => controller != null && controller.IsReady && controller.CanControl)
                ?? controllers.FirstOrDefault(controller => controller != null && controller.IsReady);
        }

        public void SetManualRudder(float input)
        {
            SyncWheelInputToCurrentRudder();
            targetRudderInput = Mathf.Clamp(input, -1f, 1f);
            targetRudderAngle = -targetRudderInput * MaxRudderAngle;
            manualRudderWheelTargetReachedTime = 0f;
            manualRudderActive = true;
            headingLockActive = false;
            routeActive = false;
            status = $"Manual rudder target {targetRudderInput:P0}.";
        }

        public void CenterRudder()
        {
            SyncWheelInputToCurrentRudder();
            SetManualRudder(0f);
            status = "Manual rudder target centered.";
        }

        public void NudgeManualRudder(float delta)
        {
            float baseInput = manualRudderActive ? targetRudderInput : CurrentRudderInput;
            SetManualRudder(baseInput + delta);
        }

        public void NudgeHeadingLock(float delta)
        {
            float heading = headingLockActive || routeActive ? targetHeading : BoatHeading();
            EnableHeadingLock(heading + delta);
        }

        public void ToggleHelmLock()
        {
            if (steeringWheel == null) return;

            bool nextLocked = !HelmLocked;
            if (HelmHeldBySailMaster)
            {
                status = "SailMaster has helm control.";
                return;
            }

            SetRelatedSteeringWheelLocks(nextLocked, true);
            steeringWheelLockedBySailMaster = false;
            status = nextLocked ? "Helm locked in place." : "Helm unlocked.";
        }

        public void StopNavigation()
        {
            headingLockActive = false;
            routeActive = false;
            manualRudderActive = false;
            targetRudderInput = CurrentRudderInput;
            targetRudderAngle = RudderAngle;
            waypoints.Clear();
            waypointIndex = 0;
            ResetPid();
            if (steeringWheel != null)
            {
                WriteSteeringInput(0f);
                ReleaseSteeringWheel();
            }

            status = "Navigation stopped.";
        }

        public void EnableHeadingLock(float heading)
        {
            targetHeading = NormalizeHeading180(heading);
            headingLockActive = true;
            routeActive = false;
            manualRudderActive = false;
            ResetPid();
            status = $"Heading lock {NormalizeHeading360(targetHeading):F0} deg.";
        }

        public bool StartRouteFromJson(string json, out string message)
        {
            return StartRouteFromJson(json, 0, out message);
        }

        public bool StartRouteFromJson(string json, int startWaypointIndex, out string message)
        {
            if (!TryParseRouteJson(json, out List<Vector2> parsedWaypoints, out message))
            {
                status = message;
                return false;
            }

            if (startWaypointIndex < 0 || startWaypointIndex >= parsedWaypoints.Count)
            {
                message = $"Select a waypoint from 1 to {parsedWaypoints.Count}.";
                status = message;
                return false;
            }

            waypoints.Clear();
            waypoints.AddRange(parsedWaypoints);
            waypointIndex = startWaypointIndex;
            routeActive = true;
            headingLockActive = false;
            manualRudderActive = false;
            ResetPid();
            UpdateRouteTarget();
            message = $"Route started at waypoint {waypointIndex + 1}/{waypoints.Count}.";
            status = message;
            return true;
        }

        public static bool ValidateRouteJson(string json, out string message)
        {
            return TryParseRouteJson(json, out _, out message);
        }

        public static bool TryGetRouteWaypoints(string json, out List<Vector2> routeWaypoints, out string message)
        {
            return TryParseRouteJson(json, out routeWaypoints, out message);
        }

        private static bool TryParseRouteJson(string json, out List<Vector2> parsedWaypoints, out string message)
        {
            parsedWaypoints = new List<Vector2>();

            if (string.IsNullOrWhiteSpace(json))
            {
                message = "Paste a Sailwind Interactive Map JSON export first.";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException ex)
            {
                message = $"Could not parse route JSON: {ex.Message}";
                SailMasterMain.Logger?.LogWarning(message);
                return false;
            }

            if (!(root["path"] is JArray path))
            {
                message = "JSON must contain a path array from Sailwind Interactive Map.";
                return false;
            }

            if (path.Count == 0)
            {
                message = "Route path is empty.";
                return false;
            }

            int ignoredLineCount = GetArrayCount(root, "lines");
            int ignoredPointCount = GetArrayCount(root, "points");
            int ignoredGoalCount = GetArrayCount(root, "goals");
            int skippedColor = 0;
            int skippedInvalidPosition = 0;
            foreach (JToken point in path)
            {
                if (point.Type != JTokenType.Object)
                {
                    skippedInvalidPosition++;
                    continue;
                }

                string color = point.Value<string>("colour");
                if (!string.Equals(color, routePointColor, StringComparison.OrdinalIgnoreCase))
                {
                    skippedColor++;
                    continue;
                }

                if (!(point["pos"] is JArray position)
                    || position.Count < 2
                    || !TryGetFloat(position[0], out float longitude)
                    || !TryGetFloat(position[1], out float latitude)
                    || !IsFinite(longitude)
                    || !IsFinite(latitude))
                {
                    skippedInvalidPosition++;
                    continue;
                }

                parsedWaypoints.Add(new Vector2(longitude, latitude));
            }

            if (parsedWaypoints.Count == 0)
            {
                message = skippedColor > 0
                    ? "No usable orangepoint path positions found."
                    : "Path entries need colour \"orangepoint\" and pos [longitude, latitude].";
                return false;
            }

            message = $"Valid route: {parsedWaypoints.Count} waypoint(s)";
            if (skippedColor > 0 || skippedInvalidPosition > 0)
            {
                message += $" ({skippedColor} non-route, {skippedInvalidPosition} invalid skipped)";
            }

            if (ignoredLineCount > 0 || ignoredPointCount > 0 || ignoredGoalCount > 0)
            {
                message += $" Ignored map annotations: {ignoredLineCount} line(s), {ignoredPointCount} point(s), {ignoredGoalCount} goal(s)";
            }

            message += ".";
            return true;
        }

        private void UpdateCanControl()
        {
            if (GameState.currentBoat != null)
            {
                CanControl = GameState.currentBoat.IsChildOf(boat);
            }
            else if (GameState.lastBoat != null)
            {
                CanControl = GameState.lastBoat.IsChildOf(boat);
            }
            else
            {
                CanControl = false;
            }
        }

        private void UpdateRouteTarget()
        {
            if (waypointIndex >= waypoints.Count)
            {
                StopNavigation();
                status = "Route complete.";
                return;
            }

            Vector2 current = CurrentCoordinates();
            while (waypointIndex < waypoints.Count && CalculateGlobeDistanceNm(current, waypoints[waypointIndex]) <= waypointArrivalDistanceNm)
            {
                waypointIndex++;
            }

            if (waypointIndex >= waypoints.Count)
            {
                StopNavigation();
                status = "Route complete.";
                return;
            }

            Vector2 target = waypoints[waypointIndex];
            Vector2 delta = target - current;
            targetHeading = NormalizeHeading180(Mathf.Atan2(delta.x, delta.y) * Mathf.Rad2Deg);
            status = $"Waypoint {waypointIndex + 1}/{waypoints.Count}: {target.x:F4}, {target.y:F4} ({CalculateGlobeDistanceNm(current, target):F2} nm).";
        }

        private Vector2 CurrentCoordinates()
        {
            Vector3 globeCoords = FloatingOriginManager.instance.GetGlobeCoords(boat);
            return new Vector2(globeCoords.x, globeCoords.z);
        }

        private void ApplySteering(float currentHeading, float desiredHeading)
        {
            float deltaTime = Time.time - lastTime;
            if (deltaTime <= headingUpdateInterval) return;

            float error = Mathf.DeltaAngle(desiredHeading, currentHeading);
            integral = Mathf.Clamp(integral + (error * deltaTime), -50f, 50f);
            float derivative = (error - lastError) / deltaTime;
            float command = (SailMasterMain.navigationKp.Value * error)
                + (SailMasterMain.navigationKi.Value * integral)
                + (SailMasterMain.navigationKd.Value * derivative);

            WriteSteeringInput(currentInputMax * Mathf.Clamp(command, -1f, 1f));
            lastError = error;
            lastTime = Time.time;
        }

        private void ApplyRudderTarget()
        {
            float maxAngle = MaxRudderAngle;
            if (maxAngle <= 0f) return;

            float targetWheelInput = targetRudderAngle * steeringWheel.gearRatio;
            float nextWheelInput = Mathf.MoveTowards(steeringWheel.currentInput, targetWheelInput, manualWheelInputSpeed * Time.deltaTime);
            WriteSteeringInput(nextWheelInput);

            bool wheelAtTarget = Mathf.Abs(steeringWheel.currentInput - targetWheelInput) <= 0.5f;
            bool rudderAtTarget = Mathf.Abs(targetRudderAngle - RudderAngle) <= 1f;
            if (wheelAtTarget && manualRudderWheelTargetReachedTime <= 0f)
            {
                manualRudderWheelTargetReachedTime = Time.time;
            }
            else if (!wheelAtTarget)
            {
                manualRudderWheelTargetReachedTime = 0f;
            }

            if (wheelAtTarget && (rudderAtTarget || Time.time - manualRudderWheelTargetReachedTime >= 0.75f))
            {
                manualRudderActive = false;
                ReleaseSteeringWheel();
                status = rudderAtTarget ? "Manual rudder target reached." : "Manual rudder wheel target reached.";
            }
        }

        private void SyncWheelInputToCurrentRudder()
        {
            if (steeringWheel == null) return;

            steeringWheel.currentInput = RudderAngle * steeringWheel.gearRatio;
            applyWheelRotationFromRudderMethod?.Invoke(steeringWheel, null);
        }

        private void WriteSteeringInput(float input)
        {
            using (SailMasterProfiler.Scope("SailMaster/Navigation/WriteSteeringInput"))
            {
                if (steeringWheel == null) return;

                steeringWheel.currentInput = input;
                applyRudderRotationMethod?.Invoke(steeringWheel, null);
                applyWheelRotationFromRudderMethod?.Invoke(steeringWheel, null);
            }
        }

        private void LockSteeringWheel()
        {
            if (IsAnyRelatedSteeringWheelLocked() && !HelmHeldBySailMaster)
            {
                SetRelatedSteeringWheelLocks(false, true);
            }

            if (!IsSteeringWheelLocked())
            {
                SetSteeringWheelLocked(steeringWheel, true);
            }

            steeringWheelLockedBySailMaster = true;
        }

        private void ReleaseSteeringWheel()
        {
            if (steeringWheel == null) return;

            if (steeringWheelLockedBySailMaster && IsSteeringWheelLocked())
            {
                SetSteeringWheelLocked(steeringWheel, false);
            }

            steeringWheelLockedBySailMaster = false;
        }

        private bool IsSteeringWheelLocked()
        {
            return IsSteeringWheelLocked(steeringWheel);
        }

        private bool IsAnyRelatedSteeringWheelLocked()
        {
            return relatedSteeringWheels.Any(IsSteeringWheelLocked)
                || (relatedSteeringWheels.Count == 0 && IsSteeringWheelLocked());
        }

        private bool IsSteeringWheelLocked(GPButtonSteeringWheel wheel)
        {
            return wheel != null && steeringWheelLockedField != null && (bool)steeringWheelLockedField.GetValue(wheel);
        }

        private static void SetSteeringWheelLocked(GPButtonSteeringWheel wheel, bool locked)
        {
            if (wheel == null || steeringWheelLockedField == null) return;

            steeringWheelLockedField.SetValue(wheel, locked);
        }

        private void SetRelatedSteeringWheelLocks(bool locked, bool callGameMethod)
        {
            List<GPButtonSteeringWheel> wheels = relatedSteeringWheels.Count > 0
                ? relatedSteeringWheels.Where(wheel => wheel != null).ToList()
                : new List<GPButtonSteeringWheel> { steeringWheel };
            GPButtonSteeringWheel soundWheel = wheels.Contains(steeringWheel) ? steeringWheel : wheels.FirstOrDefault();
            if (callGameMethod && soundWheel != null && IsSteeringWheelLocked(soundWheel) != locked)
            {
                (locked ? lockMethod : unlockMethod)?.Invoke(soundWheel, null);
            }

            foreach (GPButtonSteeringWheel wheel in wheels)
            {
                if (wheel == null) continue;
                SetSteeringWheelLocked(wheel, locked);
            }
        }

        private void RefreshRelatedSteeringWheels()
        {
            relatedSteeringWheels.Clear();
            if (boat == null) return;

            foreach (GPButtonSteeringWheel wheel in FindObjectsOfType<GPButtonSteeringWheel>())
            {
                Transform wheelBoat = wheel.GetComponentInParent<PurchasableBoat>()?.transform;
                if (wheelBoat != boat) continue;

                relatedSteeringWheels.Add(wheel);
            }

            if (relatedSteeringWheels.Count == 0 && steeringWheel != null)
            {
                relatedSteeringWheels.Add(steeringWheel);
            }
        }

        private bool IsSailMasterSteeringActive()
        {
            return headingLockActive || routeActive || manualRudderActive;
        }

        private float BoatHeading()
        {
            return NormalizeHeading180(Vector3.SignedAngle(boat.forward, Vector3.forward, -Vector3.up));
        }

        private Vector3 ApparentWind()
        {
            return Wind.currentWind - (shipRigidbody != null ? shipRigidbody.velocity : Vector3.zero);
        }

        private float GetApparentWindAngle()
        {
            Vector3 reference = shipRigidbody != null && shipRigidbody.velocity.sqrMagnitude > 0.01f
                ? -shipRigidbody.velocity
                : boat != null ? -boat.forward : Vector3.back;

            return NormalizeHeading180(Vector3.SignedAngle(reference, ApparentWind(), -Vector3.up));
        }

        private void ResetPid()
        {
            integral = 0f;
            lastError = 0f;
            lastTime = 0f;
        }

        private static float NormalizeHeading180(float heading)
        {
            return Mathf.DeltaAngle(0f, heading);
        }

        private static float NormalizeHeading360(float heading)
        {
            heading %= 360f;
            if (heading < 0f) heading += 360f;
            return heading;
        }

        public static float CalculateGlobeDistanceNm(Vector2 from, Vector2 to)
        {
            float averageLatitudeRadians = ((from.y + to.y) * 0.5f) * Mathf.Deg2Rad;
            float latitudeNm = (to.y - from.y) * nauticalMilesPerDegreeLatitude;
            float longitudeNm = (to.x - from.x) * nauticalMilesPerDegreeLatitude * Mathf.Cos(averageLatitudeRadians);
            return Mathf.Sqrt((latitudeNm * latitudeNm) + (longitudeNm * longitudeNm));
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static int GetArrayCount(JObject root, string propertyName)
        {
            return root[propertyName] is JArray array ? array.Count : 0;
        }

        private static bool TryGetFloat(JToken token, out float value)
        {
            value = 0f;
            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
            {
                value = token.Value<float>();
                return true;
            }

            return token.Type == JTokenType.String
                && float.TryParse(token.Value<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
