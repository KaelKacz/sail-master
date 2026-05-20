using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterGui : MonoBehaviour
    {
        private const int groupCount = 6;
        private const float minWindowWidth = 660f;
        private const float minWindowHeight = 360f;
        private const float screenMargin = 20f;
        private const float desiredWindowHeight = 700f;
        private const float minSailListHeight = 120f;
        private const float windowChromeHeight = 200f;
        private const float sailListHeightBuffer = 28f;
        private const float raiseLowerRowHeight = 32f;
        private const float trimHeaderHeight = 26f;
        private const float trimControlHeight = 24f;
        private const float navigationContentHeight = 620f;
        private const float rowBoxPaddingHeight = 10f;
        private const float sliderLayoutHeight = 24f;
        private const float sliderVisualHeight = 16f;
        private static readonly Color windowBackgroundColor = new Color(0.25f, 0.25f, 0.25f, 0.95f);
        private static readonly Color sailRowColor = new Color(0.14f, 0.14f, 0.14f, 0.95f);
        private static readonly Color groupedSailRowColor = new Color(0.34f, 0.34f, 0.34f, 0.95f);
        private static readonly string[] tabLabels = { "Raise/Lower", "Trim", "Navigation" };

        private readonly HashSet<SailMasterControlSail>[] groups = new HashSet<SailMasterControlSail>[groupCount];
        private Rect windowRect = new Rect(40f, 80f, minWindowWidth, desiredWindowHeight);
        private Vector2 scroll;
        private GUIStyle windowStyle;
        private GUIStyle sailRowStyle;
        private GUIStyle groupedSailRowStyle;
        private Texture2D windowBackgroundTexture;
        private Texture2D sailRowTexture;
        private Texture2D groupedSailRowTexture;
        private int selectedGroup;
        private int selectedTab;
        private int lastHeightSailCount = -1;
        private int lastHeightTrimRows = -1;
        private int lastHeightSelectedTab = -1;
        private int lastHeightScreenWidth = -1;
        private int lastHeightScreenHeight = -1;
        private bool visible;
        private bool hadCursorState;
        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;
        private string headingInput = string.Empty;
        private string routeJsonInput = string.Empty;
        private string navigationMessage = string.Empty;
        private readonly List<Vector2> routePreviewWaypoints = new List<Vector2>();
        private Vector2 routePreviewScroll;
        private string lastParsedRouteJson = string.Empty;
        private int selectedRouteWaypointIndex;

        public bool Visible
        {
            get => visible;
            set
            {
                if (visible == value) return;

                visible = value;
                if (visible)
                {
                    previousCursorLockState = Cursor.lockState;
                    previousCursorVisible = Cursor.visible;
                    hadCursorState = true;
                    UnlockCursor();
                }
                else if (hadCursorState)
                {
                    Cursor.lockState = previousCursorLockState;
                    Cursor.visible = previousCursorVisible;
                    hadCursorState = false;
                }
            }
        }

        private void Awake()
        {
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new HashSet<SailMasterControlSail>();
            }
        }

        private void OnDestroy()
        {
            if (visible && hadCursorState)
            {
                Cursor.lockState = previousCursorLockState;
                Cursor.visible = previousCursorVisible;
            }

            if (windowBackgroundTexture != null)
            {
                Destroy(windowBackgroundTexture);
                windowBackgroundTexture = null;
            }

            if (sailRowTexture != null)
            {
                Destroy(sailRowTexture);
                sailRowTexture = null;
            }

            if (groupedSailRowTexture != null)
            {
                Destroy(groupedSailRowTexture);
                groupedSailRowTexture = null;
            }
        }

        private void Update()
        {
            if (HandleHotkeys()) return;
            if (!visible) return;

            UnlockCursor();
            Input.ResetInputAxes();
        }

        private void LateUpdate()
        {
            if (!visible) return;

            UnlockCursor();
            Input.ResetInputAxes();
        }

        private void OnGUI()
        {
            if (!Visible) return;

            UnlockCursor();
            if (IsShortcutKeyDownEvent(SailMasterMain.toggleGuiKey.Value))
            {
                Visible = false;
                Event.current.Use();
                return;
            }

            GUI.Button(new Rect(0f, 0f, Screen.width, Screen.height), string.Empty, GUIStyle.none);
            EnsureWindowStyle();
            EnsureSailRowStyles();
            var sails = SailMasterControlSail.GetControllableSails();
            PruneMissingSails(sails);
            FitWindowToScreen();
            FitWindowHeightToContentIfNeeded(sails);
            FitWindowToScreen();
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "SailMaster", windowStyle);
            Input.ResetInputAxes();
        }

        private void EnsureWindowStyle()
        {
            if (windowStyle != null) return;

            windowBackgroundTexture = MakeTexture(windowBackgroundColor);
            windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(12, 12, 24, 12),
                border = new RectOffset(1, 1, 1, 1)
            };
            windowStyle.normal.background = windowBackgroundTexture;
            windowStyle.onNormal.background = windowBackgroundTexture;
            windowStyle.normal.textColor = Color.white;
            windowStyle.onNormal.textColor = Color.white;
        }

        private void EnsureSailRowStyles()
        {
            if (sailRowStyle != null && groupedSailRowStyle != null) return;

            sailRowTexture = MakeTexture(sailRowColor);
            groupedSailRowTexture = MakeTexture(groupedSailRowColor);

            sailRowStyle = CreateSailRowStyle(sailRowTexture);
            groupedSailRowStyle = CreateSailRowStyle(groupedSailRowTexture);
        }

        private static GUIStyle CreateSailRowStyle(Texture2D background)
        {
            var style = new GUIStyle(GUI.skin.box)
            {
                normal =
                {
                    background = background,
                    textColor = Color.white
                },
                border = new RectOffset(1, 1, 1, 1)
            };
            style.onNormal.background = background;
            style.onNormal.textColor = Color.white;
            return style;
        }

        private static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        private void FitWindowToScreen()
        {
            float maxWidth = Mathf.Max(320f, Screen.width - (screenMargin * 2f));
            float maxHeight = Mathf.Max(240f, Screen.height - (screenMargin * 2f));

            windowRect.width = Mathf.Min(Mathf.Max(windowRect.width, minWindowWidth), maxWidth);
            windowRect.height = Mathf.Min(Mathf.Max(windowRect.height, minWindowHeight), maxHeight);
            windowRect.x = Mathf.Clamp(windowRect.x, screenMargin, Mathf.Max(screenMargin, Screen.width - windowRect.width - screenMargin));
            windowRect.y = Mathf.Clamp(windowRect.y, screenMargin, Mathf.Max(screenMargin, Screen.height - windowRect.height - screenMargin));
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool IsShortcutKeyDownEvent(KeyboardShortcut shortcut)
        {
            Event current = Event.current;
            if (current == null || shortcut.MainKey == KeyCode.None)
            {
                return false;
            }

            if (current.type == EventType.KeyDown && current.keyCode == shortcut.MainKey)
            {
                return EventModifiersMatchShortcut(current.modifiers, shortcut);
            }

            if (current.type == EventType.MouseDown && MouseButtonMatchesShortcut(current.button, shortcut.MainKey))
            {
                return EventModifiersMatchShortcut(current.modifiers, shortcut);
            }

            return false;
        }

        private static bool EventModifiersMatchShortcut(EventModifiers eventModifiers, KeyboardShortcut shortcut)
        {
            return ModifierMatches(eventModifiers, EventModifiers.Control, shortcut, KeyCode.LeftControl, KeyCode.RightControl)
                && ModifierMatches(eventModifiers, EventModifiers.Alt, shortcut, KeyCode.LeftAlt, KeyCode.RightAlt)
                && ModifierMatches(eventModifiers, EventModifiers.Shift, shortcut, KeyCode.LeftShift, KeyCode.RightShift)
                && ModifierMatches(eventModifiers, EventModifiers.Command, shortcut, KeyCode.LeftCommand, KeyCode.RightCommand);
        }

        private static bool ModifierMatches(EventModifiers eventModifiers, EventModifiers eventModifier, KeyboardShortcut shortcut, params KeyCode[] keys)
        {
            bool eventHasModifier = (eventModifiers & eventModifier) != 0;
            bool shortcutHasModifier = keys.Contains(shortcut.MainKey) || shortcut.Modifiers.Any(keys.Contains);
            return eventHasModifier == shortcutHasModifier;
        }

        private static bool MouseButtonMatchesShortcut(int button, KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Mouse0:
                    return button == 0;
                case KeyCode.Mouse1:
                    return button == 1;
                case KeyCode.Mouse2:
                    return button == 2;
                case KeyCode.Mouse3:
                    return button == 3;
                case KeyCode.Mouse4:
                    return button == 4;
                case KeyCode.Mouse5:
                    return button == 5;
                case KeyCode.Mouse6:
                    return button == 6;
                default:
                    return false;
            }
        }

        private bool HandleHotkeys()
        {
            if (SailMasterMain.toggleGuiKey.Value.IsDown())
            {
                Visible = !Visible;
                return true;
            }

            if (SailMasterMain.allSailsKey.Value.IsDown())
            {
                SailMasterControlSail.CommandSails(SailMasterControlSail.GetControllableSails(), "all");
                return true;
            }

            if (SailMasterMain.group1SailsKey.Value.IsDown()) return CommandGroupHotkey(0);
            if (SailMasterMain.group2SailsKey.Value.IsDown()) return CommandGroupHotkey(1);
            if (SailMasterMain.group3SailsKey.Value.IsDown()) return CommandGroupHotkey(2);
            if (SailMasterMain.group4SailsKey.Value.IsDown()) return CommandGroupHotkey(3);
            if (SailMasterMain.group5SailsKey.Value.IsDown()) return CommandGroupHotkey(4);
            if (SailMasterMain.group6SailsKey.Value.IsDown()) return CommandGroupHotkey(5);

            return false;
        }

        private bool CommandGroupHotkey(int groupIndex)
        {
            SailMasterControlSail.CommandSails(groups[groupIndex], $"group {groupIndex + 1}");
            return true;
        }

        private void DrawWindow(int windowId)
        {
            var sails = SailMasterControlSail.GetControllableSails();
            PruneMissingSails(sails);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", GUILayout.Width(70f)))
            {
                Visible = false;
            }
            GUILayout.EndHorizontal();

            DrawGroupHeader(sails);
            selectedTab = GUILayout.Toolbar(selectedTab, tabLabels);

            GUILayout.Space(8f);
            if (selectedTab != 2 && sails.Count == 0)
            {
                GUILayout.Label("No controllable sails detected on the current ship.");
                GUI.DragWindow();
                return;
            }

            if (selectedTab == 0)
            {
                DrawRaiseLowerTab(sails);
            }
            else if (selectedTab == 1)
            {
                DrawTrimTab(sails);
            }
            else
            {
                DrawNavigationTab();
            }

            GUI.DragWindow();
        }

        private void DrawGroupHeader(List<SailMasterControlSail> sails)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Group", GUILayout.Width(48f));
            for (int i = 0; i < groupCount; i++)
            {
                bool selected = GUILayout.Toggle(selectedGroup == i, $"G{i + 1}", GUI.skin.button, GUILayout.Width(48f));
                if (selected) selectedGroup = i;
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Clear", GUILayout.Width(70f)))
            {
                groups[selectedGroup].Clear();
            }

            if (GUILayout.Button("All", GUILayout.Width(70f)))
            {
                groups[selectedGroup].Clear();
                foreach (var sail in sails)
                {
                    groups[selectedGroup].Add(sail);
                }
            }

            GUILayout.Label($"G{selectedGroup + 1}: {groups[selectedGroup].Count}", GUILayout.Width(60f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawRaiseLowerTab(List<SailMasterControlSail> sails)
        {
            DrawGroupHoistControls();

            GUILayout.Space(8f);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(GetSailListHeight(sails)));
            for (int i = 0; i < sails.Count; i++)
            {
                DrawSailRow(sails[i], i);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            DrawAllControls(sails);
        }

        private void DrawGroupHoistControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Group Sails", GUILayout.Width(82f));
            if (GUILayout.Button("Min", GUILayout.Width(48f)))
            {
                SetGroupAmount(0f);
            }

            float amount = GetAverageDeployedAmount(groups[selectedGroup]);
            float newAmount = DrawClickableSlider(amount, 0f, 1f);
            if (!Mathf.Approximately(amount, newAmount))
            {
                SetGroupAmount(newAmount);
            }

            GUILayout.Label($"{amount:P0}", GUILayout.Width(42f));

            if (GUILayout.Button("Max", GUILayout.Width(48f)))
            {
                SetGroupAmount(1f);
            }
            GUILayout.EndHorizontal();
        }

        private void FitWindowHeightToContentIfNeeded(List<SailMasterControlSail> sails)
        {
            int trimRows = GetTrimRowCount(sails);
            if (lastHeightSailCount == sails.Count &&
                lastHeightTrimRows == trimRows &&
                lastHeightSelectedTab == selectedTab &&
                lastHeightScreenWidth == Screen.width &&
                lastHeightScreenHeight == Screen.height)
            {
                return;
            }

            lastHeightSailCount = sails.Count;
            lastHeightTrimRows = trimRows;
            lastHeightSelectedTab = selectedTab;
            lastHeightScreenWidth = Screen.width;
            lastHeightScreenHeight = Screen.height;

            FitWindowHeightToContent(sails);
        }

        private void FitWindowHeightToContent(List<SailMasterControlSail> sails)
        {
            float maxHeight = Mathf.Max(minWindowHeight, Screen.height - (screenMargin * 2f));
            float contentHeight = windowChromeHeight + GetSailListContentHeight(sails) + sailListHeightBuffer;
            windowRect.height = Mathf.Clamp(contentHeight, minWindowHeight, maxHeight);
            windowRect.y = Mathf.Clamp(windowRect.y, screenMargin, Mathf.Max(screenMargin, Screen.height - windowRect.height - screenMargin));
        }

        private float GetSailListHeight(List<SailMasterControlSail> sails)
        {
            float maxListHeight = Mathf.Max(minSailListHeight, windowRect.height - windowChromeHeight);
            return Mathf.Min(GetSailListContentHeight(sails) + sailListHeightBuffer, maxListHeight);
        }

        private float GetSailListContentHeight(List<SailMasterControlSail> sails)
        {
            if (selectedTab == 0)
            {
                return Mathf.Max(minSailListHeight, sails.Count * raiseLowerRowHeight);
            }

            if (selectedTab == 2)
            {
                return navigationContentHeight;
            }

            float height = GetTrimRowCount(sails) * trimControlHeight;
            height += sails.Count * (trimHeaderHeight + rowBoxPaddingHeight);

            return Mathf.Max(minSailListHeight, height);
        }

        private static int GetTrimRowCount(List<SailMasterControlSail> sails)
        {
            int trimRows = 0;
            foreach (var sail in sails)
            {
                trimRows += Mathf.Max(1, sail.TrimControls.Count);
            }

            return trimRows;
        }

        private void DrawTrimTab(List<SailMasterControlSail> sails)
        {
            DrawGroupTrimControls();

            GUILayout.Space(8f);
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(GetSailListHeight(sails)));
            for (int i = 0; i < sails.Count; i++)
            {
                DrawTrimRow(sails[i], i);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            DrawAllTrimControls(sails);
        }

        private void DrawGroupTrimControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Group Sails", GUILayout.Width(82f));
            if (GUILayout.RepeatButton("Pull", GUILayout.Width(70f)))
            {
                ApplyTrim(groups[selectedGroup], true);
            }

            if (GUILayout.RepeatButton("Release", GUILayout.Width(70f)))
            {
                ApplyTrim(groups[selectedGroup], false);
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawNavigationTab()
        {
            var navigation = SailMasterNavigationController.GetCurrent();
            if (navigation == null || !navigation.IsReady)
            {
                GUILayout.Label("No rudder or steering wheel detected on the current ship.");
                DrawFooter();
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Heading {navigation.CurrentHeading:F0} deg    Rudder {navigation.RudderAngle:F0} deg");
            GUILayout.Label($"Boat speed {navigation.BoatSpeedKnots:F1} kt    True wind {navigation.TrueWindSpeed:F1} kt @ {navigation.TrueWindDirection:F0} deg");
            GUILayout.Label($"Apparent wind {navigation.ApparentWindSpeed:F1} kt    Angle {navigation.ApparentWindAngle:F0} deg");
            GUILayout.Label($"Mode: {GetNavigationModeLabel(navigation)}");
            GUILayout.Label(navigation.Status);
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Rudder");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Port", GUILayout.Width(70f)))
            {
                navigation.NudgeManualRudder(-0.1f);
            }

            float rudderInput = navigation.CurrentRudderInput;
            float newRudderInput = DrawClickableSlider(rudderInput, -1f, 1f, GUILayout.Width(220f));
            if (!Mathf.Approximately(rudderInput, newRudderInput))
            {
                navigation.SetManualRudder(newRudderInput);
            }

            if (GUILayout.Button("Center", GUILayout.Width(70f)))
            {
                navigation.CenterRudder();
            }

            if (GUILayout.Button("Starboard", GUILayout.Width(82f)))
            {
                navigation.NudgeManualRudder(0.1f);
            }

            GUILayout.Label($"{navigation.CurrentRudderInput:P0}", GUILayout.Width(50f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Target {navigation.TargetRudderInput:P0}");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(navigation.HelmLocked ? "Unlock Helm" : "Lock Helm", GUILayout.Width(100f)))
            {
                navigation.ToggleHelmLock();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(navigation.HelmControlStatus);
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Heading Mode");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Heading", GUILayout.Width(60f));
            if (string.IsNullOrWhiteSpace(headingInput))
            {
                headingInput = navigation.TargetHeading.ToString("F0", CultureInfo.InvariantCulture);
            }

            headingInput = GUILayout.TextField(headingInput, GUILayout.Width(80f));
            if (GUILayout.Button("Current", GUILayout.Width(70f)))
            {
                headingInput = navigation.CurrentHeading.ToString("F0", CultureInfo.InvariantCulture);
            }

            if (GUILayout.Button("Port", GUILayout.Width(70f)))
            {
                navigation.NudgeHeadingLock(-5f);
                headingInput = navigation.TargetHeading.ToString("F0", CultureInfo.InvariantCulture);
                navigationMessage = $"Heading mode nudged to {navigation.TargetHeading:F0} deg.";
            }

            if (GUILayout.Button("Starboard", GUILayout.Width(82f)))
            {
                navigation.NudgeHeadingLock(5f);
                headingInput = navigation.TargetHeading.ToString("F0", CultureInfo.InvariantCulture);
                navigationMessage = $"Heading mode nudged to {navigation.TargetHeading:F0} deg.";
            }

            if (GUILayout.Button("Enable", GUILayout.Width(70f)))
            {
                if (float.TryParse(headingInput, NumberStyles.Float, CultureInfo.InvariantCulture, out float heading))
                {
                    navigation.EnableHeadingLock(heading);
                    navigationMessage = $"Heading mode enabled at {navigation.TargetHeading:F0} deg.";
                }
                else
                {
                    navigationMessage = "Enter a numeric heading.";
                }
            }

            if (GUILayout.Button("Stop", GUILayout.Width(70f)))
            {
                navigation.StopNavigation();
                navigationMessage = "Navigation stopped.";
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("Coordinate Route JSON");
            GUILayout.Label("Paste a Sailwind Interactive Map export. Route points must be path entries with colour \"orangepoint\" and pos [longitude, latitude].");
            routeJsonInput = GUILayout.TextArea(routeJsonInput, GUILayout.Height(120f));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Validate", GUILayout.Width(80f)))
            {
                RefreshRoutePreview();
            }

            if (GUILayout.Button("Start Route", GUILayout.Width(100f)))
            {
                if (EnsureRoutePreviewCurrent())
                {
                    selectedRouteWaypointIndex = Mathf.Clamp(selectedRouteWaypointIndex, 0, routePreviewWaypoints.Count - 1);
                    navigation.StartRouteFromJson(routeJsonInput, selectedRouteWaypointIndex, out navigationMessage);
                }
            }

            if (GUILayout.Button("Stop Route", GUILayout.Width(90f)))
            {
                navigation.StopNavigation();
                navigationMessage = "Route stopped.";
            }

            if (GUILayout.Button("Clear", GUILayout.Width(70f)))
            {
                routeJsonInput = string.Empty;
                navigationMessage = string.Empty;
                routePreviewWaypoints.Clear();
                lastParsedRouteJson = string.Empty;
                selectedRouteWaypointIndex = 0;
            }

            GUILayout.Label(navigation.RouteActive
                ? $"Waypoint {navigation.CurrentWaypointNumber}/{navigation.WaypointCount}"
                : "Route idle");
            GUILayout.EndHorizontal();
            DrawRoutePreview(navigation);
            DrawRouteCoordinates(navigation);
            GUILayout.EndVertical();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            GUILayout.Label(navigationMessage);
            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.EndHorizontal();
        }

        private static string GetNavigationModeLabel(SailMasterNavigationController navigation)
        {
            if (navigation.RouteActive) return "Route";
            if (navigation.HeadingLockActive) return "Heading Mode";
            if (navigation.ManualRudderActive) return "Manual Rudder";
            return "Idle";
        }

        private static void DrawRouteCoordinates(SailMasterNavigationController navigation)
        {
            Vector2 coordinates = navigation.Coordinates;
            GUILayout.Label($"Current coords: {coordinates.x:F4}, {coordinates.y:F4}");
            if (!navigation.HasCurrentWaypoint)
            {
                return;
            }

            Vector2 waypoint = navigation.CurrentWaypoint;
            string label = navigation.RouteActive
                ? $"Active waypoint {navigation.CurrentWaypointNumber}/{navigation.WaypointCount}"
                : $"Loaded waypoint {navigation.CurrentWaypointNumber}/{navigation.WaypointCount}";
            GUILayout.Label($"{label}: {waypoint.x:F4}, {waypoint.y:F4}    Distance {navigation.CurrentWaypointDistanceNm:F2} nm");
        }

        private bool EnsureRoutePreviewCurrent()
        {
            if (routePreviewWaypoints.Count > 0 && routeJsonInput == lastParsedRouteJson)
            {
                return true;
            }

            return RefreshRoutePreview();
        }

        private bool RefreshRoutePreview()
        {
            routePreviewWaypoints.Clear();
            lastParsedRouteJson = string.Empty;
            if (!SailMasterNavigationController.TryGetRouteWaypoints(routeJsonInput, out List<Vector2> waypoints, out navigationMessage))
            {
                selectedRouteWaypointIndex = 0;
                return false;
            }

            routePreviewWaypoints.AddRange(waypoints);
            lastParsedRouteJson = routeJsonInput;
            selectedRouteWaypointIndex = Mathf.Clamp(selectedRouteWaypointIndex, 0, routePreviewWaypoints.Count - 1);
            return true;
        }

        private void DrawRoutePreview(SailMasterNavigationController navigation)
        {
            if (routePreviewWaypoints.Count == 0)
            {
                return;
            }

            GUILayout.Space(4f);
            GUILayout.Label($"Start next from waypoint {selectedRouteWaypointIndex + 1}/{routePreviewWaypoints.Count}");
            routePreviewScroll = GUILayout.BeginScrollView(routePreviewScroll, GUILayout.Height(92f));
            Vector2 currentCoordinates = navigation.Coordinates;
            for (int i = 0; i < routePreviewWaypoints.Count; i++)
            {
                Vector2 waypoint = routePreviewWaypoints[i];
                float distanceNm = SailMasterNavigationController.CalculateGlobeDistanceNm(currentCoordinates, waypoint);
                bool selected = GUILayout.Toggle(
                    selectedRouteWaypointIndex == i,
                    $"{i + 1}: {waypoint.x:F4}, {waypoint.y:F4} ({distanceNm:F2} nm)",
                    GUI.skin.button);
                if (selected)
                {
                    selectedRouteWaypointIndex = i;
                }
            }
            GUILayout.EndScrollView();
        }

        private void DrawSailRow(SailMasterControlSail sail, int rowIndex)
        {
            BeginSailRow(sail);
            GUILayout.BeginHorizontal();

            DrawGroupMembershipToggle(sail);
            GUILayout.Label($"{sail.DisplayName} ({sail.CategoryName})", GUILayout.Width(210f));

            if (GUILayout.Button("Min", GUILayout.Width(48f)))
            {
                SetSailTarget(sail, 0f);
            }

            float amount = sail.DeployedAmount;
            float newAmount = DrawClickableSlider(amount, 0f, 1f, GUILayout.Width(150f));
            if (!Mathf.Approximately(amount, newAmount))
            {
                SetSailTarget(sail, newAmount);
            }

            GUILayout.Label($"{sail.DeployedAmount:P0}", GUILayout.Width(42f));

            if (GUILayout.Button("Max", GUILayout.Width(48f)))
            {
                SetSailTarget(sail, 1f);
            }

            GUILayout.EndHorizontal();
            EndSailRow();
        }

        private void DrawTrimRow(SailMasterControlSail sail, int rowIndex)
        {
            BeginSailRow(sail);
            GUILayout.BeginVertical();

            GUILayout.BeginHorizontal();
            DrawGroupMembershipToggle(sail);
            GUILayout.Label($"{sail.DisplayName} ({sail.CategoryName})");
            DrawEfficiencyLabel(sail.Efficiency);
            GUILayout.EndHorizontal();

            if (sail.TrimControls.Count == 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(26f);
                GUILayout.Label("No trim controls found");
                GUILayout.EndHorizontal();
            }
            else
            {
                foreach (var trimControl in sail.TrimControls)
                {
                    DrawTrimControl(trimControl);
                }
            }

            GUILayout.EndVertical();
            EndSailRow();
        }

        private void BeginSailRow(SailMasterControlSail sail)
        {
            GUIStyle style = groups[selectedGroup].Contains(sail) ? groupedSailRowStyle : sailRowStyle;
            GUILayout.BeginVertical(style);
        }

        private void EndSailRow()
        {
            GUILayout.EndVertical();
        }

        private void DrawTrimControl(SailMasterControlSail.TrimControl trimControl)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Space(26f);
            GUILayout.Label(trimControl.Label, GUILayout.Width(70f));
            if (GUILayout.RepeatButton("Pull", GUILayout.Width(70f)))
            {
                trimControl.Pull();
            }

            float amount = trimControl.Amount;
            float newAmount = DrawClickableSlider(amount, 0f, 1f, GUILayout.Width(150f));
            if (!Mathf.Approximately(amount, newAmount))
            {
                trimControl.SetAmount(newAmount);
            }

            if (GUILayout.RepeatButton("Release", GUILayout.Width(70f)))
            {
                trimControl.Release();
            }

            GUILayout.Label($"{amount:P0}", GUILayout.Width(42f));
            GUILayout.EndHorizontal();
        }

        private void DrawEfficiencyLabel(float? efficiency)
        {
            Color previousColor = GUI.color;
            GUI.color = GetEfficiencyColor(efficiency);
            GUILayout.Label(efficiency.HasValue ? $"Eff {efficiency.Value:F0}%" : "Eff --", GUILayout.Width(70f));
            GUI.color = previousColor;
        }

        private static Color GetEfficiencyColor(float? efficiency)
        {
            if (!efficiency.HasValue) return Color.gray;
            if (efficiency.Value < 0f) return Color.red;
            if (efficiency.Value < 40f) return new Color(1f, 0.35f, 0.25f);
            if (efficiency.Value < 70f) return Color.yellow;
            return Color.green;
        }

        private void DrawGroupMembershipToggle(SailMasterControlSail sail)
        {
            bool inGroup = groups[selectedGroup].Contains(sail);
            bool newInGroup = GUILayout.Toggle(inGroup, "", GUILayout.Width(22f));
            if (newInGroup == inGroup) return;

            if (newInGroup)
            {
                groups[selectedGroup].Add(sail);
            }
            else
            {
                groups[selectedGroup].Remove(sail);
            }
        }

        private static float DrawClickableSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] options)
        {
            Rect layoutRect = GUILayoutUtility.GetRect(0f, 10000f, sliderLayoutHeight, sliderLayoutHeight, GUI.skin.horizontalSlider, options);
            Rect sliderRect = layoutRect;
            sliderRect.y += (layoutRect.height - sliderVisualHeight) * 0.5f;
            sliderRect.height = sliderVisualHeight;

            int controlId = GUIUtility.GetControlID(FocusType.Passive, layoutRect);
            Event current = Event.current;
            if (current != null)
            {
                if (current.type == EventType.MouseDown && layoutRect.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    value = SliderValueAtMouse(layoutRect, leftValue, rightValue, current.mousePosition.x);
                    GUI.changed = true;
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    value = SliderValueAtMouse(layoutRect, leftValue, rightValue, current.mousePosition.x);
                    GUI.changed = true;
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }

            return GUI.HorizontalSlider(sliderRect, value, leftValue, rightValue);
        }

        private static float SliderValueAtMouse(Rect rect, float leftValue, float rightValue, float mouseX)
        {
            return Mathf.Lerp(leftValue, rightValue, Mathf.InverseLerp(rect.xMin, rect.xMax, mouseX));
        }

        private void DrawAllControls(List<SailMasterControlSail> sails)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("All Sails", GUILayout.Width(82f));
            if (GUILayout.Button("Min", GUILayout.Width(60f)))
            {
                SetAllAmount(sails, 0f);
            }

            float amount = GetAverageDeployedAmount(sails);
            float newAmount = DrawClickableSlider(amount, 0f, 1f, GUILayout.Width(150f));
            if (!Mathf.Approximately(amount, newAmount))
            {
                SetAllAmount(sails, newAmount);
            }

            GUILayout.Label($"{amount:P0}", GUILayout.Width(42f));

            if (GUILayout.Button("Max", GUILayout.Width(60f)))
            {
                SetAllAmount(sails, 1f);
            }

            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.EndHorizontal();
        }

        private void DrawAllTrimControls(List<SailMasterControlSail> sails)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("All Sails", GUILayout.Width(82f));
            if (GUILayout.RepeatButton("Pull", GUILayout.Width(70f)))
            {
                ApplyTrim(sails, true);
            }

            if (GUILayout.RepeatButton("Release", GUILayout.Width(70f)))
            {
                ApplyTrim(sails, false);
            }

            GUILayout.FlexibleSpace();
            DrawFooter();
            GUILayout.EndHorizontal();
        }

        private void DrawFooter()
        {
            GUILayout.Label($"Toggle GUI: {FormatShortcut(SailMasterMain.toggleGuiKey.Value)}", GUILayout.Width(150f));
        }

        private static string FormatShortcut(KeyboardShortcut shortcut)
        {
            if (shortcut.MainKey == KeyCode.None) return "None";

            return string.Join(" + ", shortcut.Modifiers
                .Select(FormatKey)
                .Concat(new[] { FormatKey(shortcut.MainKey) })
                .ToArray());
        }

        private static string FormatKey(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                    return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                    return "Alt";
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                    return "Shift";
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                    return "Cmd";
                default:
                    return key.ToString();
            }
        }

        private static float GetAverageDeployedAmount(IEnumerable<SailMasterControlSail> sails)
        {
            var validSails = sails.Where(sail => sail != null).ToList();
            if (validSails.Count == 0) return 0f;

            return Mathf.Clamp01(validSails.Average(sail => sail.DeployedAmount));
        }

        private void SetGroupAmount(float amount)
        {
            foreach (var sail in groups[selectedGroup].ToList())
            {
                if (sail == null)
                {
                    groups[selectedGroup].Remove(sail);
                    continue;
                }

                SetSailTarget(sail, amount);
            }
        }

        private void SetAllAmount(List<SailMasterControlSail> sails, float amount)
        {
            foreach (var sail in sails)
            {
                SetSailTarget(sail, amount);
            }
        }

        private void SetSailTarget(SailMasterControlSail sail, float amount)
        {
            amount = Mathf.Clamp01(amount);
            sail.SetTargetDeployedAmount(amount);
        }

        private void ApplyTrim(IEnumerable<SailMasterControlSail> sails, bool pull)
        {
            foreach (var sail in sails.ToList())
            {
                if (sail == null)
                {
                    groups[selectedGroup].Remove(sail);
                    continue;
                }

                foreach (var trimControl in sail.TrimControls)
                {
                    if (pull)
                    {
                        trimControl.Pull();
                    }
                    else
                    {
                        trimControl.Release();
                    }
                }
            }
        }

        private void PruneMissingSails(List<SailMasterControlSail> sails)
        {
            var visibleSails = new HashSet<SailMasterControlSail>(sails);
            foreach (var group in groups)
            {
                group.RemoveWhere(sail => sail == null || !visibleSails.Contains(sail));
            }

        }
    }
}
