using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterGui : MonoBehaviour
    {
        private const int groupCount = 6;
        private const float minWindowWidth = 660f;
        private const float minWindowHeight = 520f;
        private const float screenMargin = 20f;

        private readonly HashSet<SailMasterControlSail>[] groups = new HashSet<SailMasterControlSail>[groupCount];
        private Rect windowRect = new Rect(40f, 80f, minWindowWidth, minWindowHeight);
        private Vector2 scroll;
        private int selectedGroup;
        private bool visible;
        private bool hadCursorState;
        private CursorLockMode previousCursorLockState;
        private bool previousCursorVisible;

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
            FitWindowToScreen();
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "SailMaster");
            Input.ResetInputAxes();
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

            GUILayout.Space(8f);
            if (sails.Count == 0)
            {
                GUILayout.Label("No controllable sails detected on the current ship.");
                GUI.DragWindow();
                return;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(360f));
            foreach (var sail in sails)
            {
                DrawSailRow(sail);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8f);
            DrawAllControls(sails);

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

        private void DrawSailRow(SailMasterControlSail sail)
        {
            GUILayout.BeginHorizontal();

            bool inGroup = groups[selectedGroup].Contains(sail);
            bool newInGroup = GUILayout.Toggle(inGroup, "", GUILayout.Width(22f));
            if (newInGroup != inGroup)
            {
                if (newInGroup)
                {
                    groups[selectedGroup].Add(sail);
                }
                else
                {
                    groups[selectedGroup].Remove(sail);
                }
            }

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
        }

        private static float DrawClickableSlider(float value, float leftValue, float rightValue, params GUILayoutOption[] options)
        {
            Rect rect = GUILayoutUtility.GetRect(GUIContent.none, GUI.skin.horizontalSlider, options);
            int controlId = GUIUtility.GetControlID(FocusType.Passive, rect);
            Event current = Event.current;
            if (current != null)
            {
                if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    value = SliderValueAtMouse(rect, leftValue, rightValue, current.mousePosition.x);
                    GUI.changed = true;
                    current.Use();
                }
                else if (current.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    value = SliderValueAtMouse(rect, leftValue, rightValue, current.mousePosition.x);
                    GUI.changed = true;
                    current.Use();
                }
                else if (current.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    current.Use();
                }
            }

            return GUI.HorizontalSlider(rect, value, leftValue, rightValue);
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
            GUILayout.Label($"Toggle GUI: {FormatShortcut(SailMasterMain.toggleGuiKey.Value)}", GUILayout.Width(150f));
            GUILayout.EndHorizontal();
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
