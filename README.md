# SailMaster

SailMaster is a Sailwind mod for sail and helm control from an in-game panel.

It adds grouped sail hoisting/lowering, manual trim controls, live sail efficiency display, rudder controls, heading mode, and JSON route following.

## Features

- Raise or lower individual sails, all sails, or one of 6 custom sail groups.
- Assign sails to groups from the `Raise/Lower` or `Trim` tab.
- Set deployed amount with sliders, `Min`, and `Max` buttons.
- Manually pull/release trim lines per sail, selected group, or all sails.
- Use trim sliders where `0%` is pulled in and `100%` is released.
- View color-coded sail efficiency in the Trim tab.
- Control rudder position with a slider, port/starboard nudges, and center command.
- Lock/unlock the helm from the Navigation tab.
- Use heading mode with configurable PID gains.
- Follow Sailwind Interactive Map / CoordinateViewer-style JSON routes.
- Hold right mouse while the SailMaster panel is open to rotate the camera.

## Default Hotkeys

- `0`: raise/lower all controllable sails
- `1`-`6`: raise/lower SailMaster group 1-6
- `F7`: show/hide the SailMaster panel

Hotkeys can be changed in the BepInEx config file.

## Panel Tabs

### Raise/Lower

Use this tab to set sail deployed amount.

Group and all-sail controls use average deployed amount for their sliders. Clicking or dragging a group/all slider applies that deployed amount to every controllable sail in that set.

### Trim

Use this tab for manual trim. SailMaster does not auto-trim sails.

Each trim slider controls rope released amount:

- `0%`: fully pulled in
- `100%`: fully released

Square sails with paired trim lines are shown as one combined trim control when appropriate.

### Navigation

Use this tab for rudder and route control.

- Rudder slider: set a one-shot rudder target.
- Port/Starboard: nudge rudder target.
- Center: target centered rudder.
- Lock Helm / Unlock Helm: mirrors the game's helm lock behavior.
- Heading Mode: hold a compass heading.
- Coordinate Route JSON: paste a route export and start from any parsed waypoint.

SailMaster drives the game steering wheel internally, not the rudder directly. It temporarily takes helm control while manual rudder, heading mode, or route mode is active, then releases control.

## Route JSON

Route input expects a Sailwind Interactive Map / CoordinateViewer-style JSON export.

Only `path` entries with `colour` set to `orangepoint` are used. For each point:

- `pos[0]` is longitude
- `pos[1]` is latitude

Other map annotations such as `lines`, `points`, and `goals` are ignored.

## Installation

Requires BepInEx 5 for Sailwind.

Install the release contents into:

```text
Sailwind/BepInEx/plugins/SailMaster
```

The folder should contain:

- `SailMaster.dll`
- `Newtonsoft.Json.dll`
