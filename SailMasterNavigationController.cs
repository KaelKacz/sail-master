using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterNavigationController : MonoBehaviour
    {
        private const float waypointArrivalDistance = 0.02f;
        private const float headingUpdateInterval = 0.05f;
        private const float debugLogInterval = 1f;
        private const float steeringWheelResolveInterval = 2f;
        private const float manualWheelInputSpeed = 90f;
        private const float manualRudderNudgeStep = 0.1f;
        private const float headingNudgeStep = 5f;
        private const float defaultKp = 0.03f;
        private const float defaultKi = 0.005f;
        private const float defaultKd = 0.015f;

        private static readonly List<SailMasterNavigationController> controllers = new List<SailMasterNavigationController>();

        private Transform boat;
        private HingeJoint rudderJoint;
        private Rudder rudder;
        private GPButtonSteeringWheel steeringWheel;
        private float currentInputMax;
        private float targetHeading;
        private float targetRudderInput;
        private float targetRudderAngle;
        private float lastWrittenInput;
        private float nextSteeringWheelResolveTime;
        private float nextDebugLogTime;
        private bool headingLockActive;
        private bool routeActive;
        private bool manualRudderActive;
        private readonly List<Vector2> waypoints = new List<Vector2>();
        private int waypointIndex;
        private float integral;
        private float lastError;
        private float lastTime;
        private string status = "Navigation controller ready.";
        private static bool debugLogSessionStarted;
        private static readonly string debugLogPath = Path.Combine(Paths.BepInExRootPath, "SailMasterNavigationDebug.log");

        public bool CanControl { get; private set; }
        public bool IsReady => rudder != null && steeringWheel != null && boat != null;
        public bool HeadingLockActive => headingLockActive;
        public bool RouteActive => routeActive;
        public bool ManualRudderActive => manualRudderActive;
        public float ManualRudderInput => CurrentRudderInput;
        public float CurrentRudderInput => MaxRudderAngle > 0f ? Mathf.Clamp(-RudderAngle / MaxRudderAngle, -1f, 1f) : 0f;
        public float TargetRudderInput => targetRudderInput;
        public int WaypointCount => waypoints.Count;
        public int CurrentWaypointNumber => routeActive ? waypointIndex + 1 : 0;
        public string Status => status;
        public float CurrentHeading => IsReady ? NormalizeHeading360(BoatHeading()) : 0f;
        public float TargetHeading => NormalizeHeading360(targetHeading);
        public float RudderAngle => rudder != null ? rudder.currentAngle : 0f;
        public string DebugStatus => $"Ready {IsReady}  Can {CanControl}  Input {(steeringWheel != null ? steeringWheel.currentInput : 0f):F1}  Max {currentInputMax:F1}";
        public string DebugLogPath => debugLogPath;
        private float MaxRudderAngle => rudderJoint != null ? Mathf.Max(1f, Mathf.Abs(rudderJoint.limits.max)) : 0f;

        private void Awake()
        {
            rudder = GetComponent<Rudder>();
            if (rudder == null) return;

            StartDebugLogSession();
            ResolveSteeringWheel();
            controllers.Add(this);
        }

        private void OnDestroy()
        {
            controllers.Remove(this);
        }

        private void Update()
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
                UpdateRouteTarget();
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

            if (headingLockActive || routeActive || manualRudderActive)
            {
                WriteDebugLogThrottled();
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
                var buttonRudder = Traverse.Create(button).Field("rudder").GetValue() as Rudder;
                if (buttonRudder != rudder) continue;

                steeringWheel = button;
                rudderJoint = button.attachedRudder != null ? button.attachedRudder : rudder.GetComponent<HingeJoint>();
                RefreshCurrentInputMax();
                WriteDebugLog("resolved steering wheel");
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
            return controllers.FirstOrDefault(controller => controller != null && controller.IsReady && controller.CanControl)
                ?? controllers.FirstOrDefault(controller => controller != null && controller.IsReady);
        }

        public void SetManualRudder(float input)
        {
            targetRudderInput = Mathf.Clamp(input, -1f, 1f);
            targetRudderAngle = -targetRudderInput * MaxRudderAngle;
            manualRudderActive = true;
            headingLockActive = false;
            routeActive = false;
            status = $"Manual rudder target {targetRudderInput:P0}.";
            WriteDebugLog($"manual rudder target set input={targetRudderInput:F3} targetAngle={targetRudderAngle:F2}");
        }

        public void CenterRudder()
        {
            if (steeringWheel != null)
            {
                steeringWheel.currentInput = RudderAngle * steeringWheel.gearRatio;
            }

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
            WriteDebugLog("navigation stopped");
        }

        public void EnableHeadingLock(float heading)
        {
            targetHeading = NormalizeHeading180(heading);
            headingLockActive = true;
            routeActive = false;
            manualRudderActive = false;
            ResetPid();
            status = $"Heading lock {NormalizeHeading360(targetHeading):F0} deg.";
            WriteDebugLog($"heading lock target={targetHeading:F2}");
        }

        public bool StartRouteFromJson(string json, out string message)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                message = "Paste a coordinate JSON list first.";
                status = message;
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<CoordinateListJson>(json);
                var parsedWaypoints = parsed?.path?
                    .Where(point => point?.pos != null
                        && point.pos.Length >= 2
                        && string.Equals(point.colour, "orangepoint", StringComparison.OrdinalIgnoreCase))
                    .Select(point => new Vector2(point.pos[0], point.pos[1]))
                    .ToList();

                if (parsedWaypoints == null || parsedWaypoints.Count == 0)
                {
                    message = "No orangepoint path positions found in JSON.";
                    status = message;
                    return false;
                }

                waypoints.Clear();
                waypoints.AddRange(parsedWaypoints);
                waypointIndex = 0;
                routeActive = true;
                headingLockActive = false;
                manualRudderActive = false;
                ResetPid();
                UpdateRouteTarget();
                message = $"Route started with {waypoints.Count} waypoint(s).";
                status = message;
                WriteDebugLog(message);
                return true;
            }
            catch (Exception ex)
            {
                message = $"Could not parse coordinate JSON: {ex.Message}";
                status = message;
                SailMasterMain.Logger?.LogWarning(message);
                return false;
            }
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
            while (waypointIndex < waypoints.Count && Vector2.Distance(current, waypoints[waypointIndex]) <= waypointArrivalDistance)
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
            status = $"Waypoint {waypointIndex + 1}/{waypoints.Count}: {target.x:F4}, {target.y:F4}.";
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
            float command = (defaultKp * error) + (defaultKi * integral) + (defaultKd * derivative);

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

            if (Mathf.Abs(steeringWheel.currentInput - targetWheelInput) <= 0.5f && Mathf.Abs(targetRudderAngle - RudderAngle) <= 1f)
            {
                manualRudderActive = false;
                ReleaseSteeringWheel();
                status = "Manual rudder target reached.";
                WriteDebugLog("manual rudder target reached");
            }
        }

        private void WriteSteeringInput(float input)
        {
            if (steeringWheel == null) return;

            steeringWheel.currentInput = input;
            lastWrittenInput = input;
            Traverse.Create(steeringWheel).Method("ApplyRudderRotation").GetValue();
            Traverse.Create(steeringWheel).Method("ApplyWheelRotationFromRudder").GetValue();
        }

        private static void StartDebugLogSession()
        {
            if (debugLogSessionStarted) return;

            debugLogSessionStarted = true;
            try
            {
                File.AppendAllText(debugLogPath, $"{DateTime.Now:O} --- SailMaster navigation debug session ---{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                SailMasterMain.Logger?.LogWarning($"Could not start navigation debug log: {ex.Message}");
            }
        }

        private void WriteDebugLogThrottled()
        {
            if (Time.time < nextDebugLogTime) return;

            nextDebugLogTime = Time.time + debugLogInterval;
            WriteDebugLog("tick");
        }

        private void WriteDebugLog(string reason)
        {
            try
            {
                string locked = steeringWheel != null
                    ? ((bool)Traverse.Create(steeringWheel).Field("locked").GetValue()).ToString()
                    : "n/a";
                string wheelEuler = steeringWheel != null
                    ? steeringWheel.transform.localEulerAngles.ToString("F2")
                    : "n/a";
                string springTarget = rudderJoint != null
                    ? rudderJoint.spring.targetPosition.ToString("F2")
                    : "n/a";
                string line =
                    $"{DateTime.Now:O} {reason} " +
                    $"ready={IsReady} can={CanControl} activeManual={manualRudderActive} activeHeading={headingLockActive} activeRoute={routeActive} " +
                    $"locked={locked} currentInput={(steeringWheel != null ? steeringWheel.currentInput : 0f):F2} lastWritten={lastWrittenInput:F2} maxInput={currentInputMax:F2} " +
                    $"rudder={RudderAngle:F2} targetRudder={targetRudderAngle:F2} targetInput={targetRudderInput:F3} springTarget={springTarget} " +
                    $"heading={CurrentHeading:F2} targetHeading={TargetHeading:F2} wheelEuler={wheelEuler}{Environment.NewLine}";
                File.AppendAllText(debugLogPath, line);
            }
            catch (Exception ex)
            {
                SailMasterMain.Logger?.LogWarning($"Could not write navigation debug log: {ex.Message}");
            }
        }

        private void LockSteeringWheel()
        {
            if (!(bool)Traverse.Create(steeringWheel).Field("locked").GetValue())
            {
                Traverse.Create(steeringWheel).Field("locked").SetValue(true);
            }
        }

        private void ReleaseSteeringWheel()
        {
            if (steeringWheel == null) return;

            if ((bool)Traverse.Create(steeringWheel).Field("locked").GetValue())
            {
                Traverse.Create(steeringWheel).Field("locked").SetValue(false);
            }
        }

        private float BoatHeading()
        {
            return NormalizeHeading180(Vector3.SignedAngle(boat.forward, Vector3.forward, -Vector3.up));
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

        [Serializable]
        private class CoordinateListJson
        {
            public CoordinatePathPointJson[] path = new CoordinatePathPointJson[0];
        }

        [Serializable]
        private class CoordinatePathPointJson
        {
            public float[] pos = new float[0];
            public string colour = string.Empty;
        }
    }
}
