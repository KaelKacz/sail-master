# SailMaster

SailMaster is a Sailwind mod that adds an in-game panel for sail and helm control.

Features:
- Raise/lower individual sails, all sails, or 6 custom sail groups.
- Set sail deployment with sliders, `Min`, and `Max`.
- Assign groups from the `Raise/Lower` or `Trim` tab.
- Manually pull/release trim lines per sail, group, or all sails.
- Enable all-sail Auto Trim from the `Trim` tab.
- View color-coded sail efficiency while trimming.
- Control rudder position with port/starboard nudges, center, or a slider.
- Lock/unlock the helm from the panel.
- Use heading mode with configurable PID gains.
- Follow Sailwind Interactive Map JSON routes and start from any parsed waypoint.
- Hold right mouse while the panel is open to rotate the camera.

Default hotkeys:
- `0`: raise/lower all controllable sails
- `1`-`6`: raise/lower SailMaster group 1-6
- `F7`: show/hide the panel

Trim sliders use rope released amount: `0%` is pulled in, `100%` is released. Square sails with paired trim lines are shown as one combined trim control when appropriate.

Route input expects a Sailwind Interactive Map JSON export. SailMaster uses only `path` entries with `colour` set to `orangepoint`; `pos[0]` is longitude and `pos[1]` is latitude.

Requires BepInEx 5 for Sailwind. Install the release contents into:

```text
Sailwind/BepInEx/plugins/SailMaster
```

The folder should contain `SailMaster.dll` and `Newtonsoft.Json.dll`.

Releases:
https://github.com/KaelKacz/sail-master/releases
