using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using UnityEngine;

namespace SailMaster
{
    public class SailMasterGui : MonoBehaviour
    {
        private const int groupCount = 6;

        private readonly HashSet<SailMasterControlSail>[] groups = new HashSet<SailMasterControlSail>[groupCount];
        private readonly Dictionary<SailMasterControlSail, float> sailTargets = new Dictionary<SailMasterControlSail, float>();
        private Rect windowRect = new Rect(40f, 80f, 560f, 520f);
        private Vector2 scroll;
        private int selectedGroup;
        private float groupTarget = 1f;
        private float allTarget = 1f;
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
            windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "SailMaster");
            Input.ResetInputAxes();
        }

        private static void UnlockCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static bool IsShortcutKeyDownEvent(KeyboardShortcut shortcut)
        {
            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown || current.keyCode != shortcut.MainKey)
            {
                return false;
            }

            return shortcut.Modifiers.All(Input.GetKey);
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

            groupTarget = GUILayout.HorizontalSlider(groupTarget, 0f, 1f);
            GUILayout.Label($"{groupTarget:P0}", GUILayout.Width(42f));

            if (GUILayout.Button("Set", GUILayout.Width(48f)))
            {
                SetGroupAmount(groupTarget);
            }

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

            float amount = GetSailTarget(sail);
            float newAmount = GUILayout.HorizontalSlider(amount, 0f, 1f, GUILayout.Width(150f));
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

        private void DrawAllControls(List<SailMasterControlSail> sails)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("All visible", GUILayout.Width(82f));
            if (GUILayout.Button("Min", GUILayout.Width(60f)))
            {
                SetAllAmount(sails, 0f);
            }

            allTarget = GUILayout.HorizontalSlider(allTarget, 0f, 1f, GUILayout.Width(150f));
            GUILayout.Label($"{allTarget:P0}", GUILayout.Width(42f));

            if (GUILayout.Button("Set", GUILayout.Width(60f)))
            {
                SetAllAmount(sails, allTarget);
            }

            if (GUILayout.Button("Max", GUILayout.Width(60f)))
            {
                SetAllAmount(sails, 1f);
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("Toggle GUI: F7", GUILayout.Width(95f));
            GUILayout.EndHorizontal();
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

        private float GetSailTarget(SailMasterControlSail sail)
        {
            if (!sailTargets.TryGetValue(sail, out float target))
            {
                target = sail.DeployedAmount;
                sailTargets[sail] = target;
            }

            return target;
        }

        private void SetSailTarget(SailMasterControlSail sail, float amount)
        {
            amount = Mathf.Clamp01(amount);
            sailTargets[sail] = amount;
            sail.SetTargetDeployedAmount(amount);
        }

        private void PruneMissingSails(List<SailMasterControlSail> sails)
        {
            var visibleSails = new HashSet<SailMasterControlSail>(sails);
            foreach (var group in groups)
            {
                group.RemoveWhere(sail => sail == null || !visibleSails.Contains(sail));
            }

            foreach (var sail in sailTargets.Keys.ToList())
            {
                if (sail == null || !visibleSails.Contains(sail))
                {
                    sailTargets.Remove(sail);
                }
            }
        }
    }
}
