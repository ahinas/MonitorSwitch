# Monitor Switch 🖥️↔️🖥️

Monitor Switch is a small Windows system-tray app for changing monitor input sources without using the monitor's physical buttons. Create profiles for your displays, then apply them from the tray menu, with a keyboard shortcut, or automatically when a USB device connects or disconnects.

Input switching uses the monitor's DDC/CI interface (MCCS VCP feature `0x60`). Your monitor and connection must support DDC/CI input switching.

## Features

- Create, rename, duplicate, reorder, and remove input profiles.
- Choose an input source for each connected display in a profile.
- Apply a profile from the tray menu or assign it a global hotkey.
- Optionally apply profiles when a selected USB device connects or disconnects.
- Start the app when you sign in to Windows.
- Test an input source from Settings before adding it to a profile.

## Requirements

- Windows
- .NET 10 SDK to build and run from source
- Monitors with DDC/CI enabled and support for input-source switching

Some monitors disable DDC/CI by default. Check the monitor's on-screen menu for a setting such as **DDC/CI** or **Display Data Channel**.

## Build and run

From the project directory:

```powershell
dotnet run --project .\MonitorSwitch.csproj
```

To build a Release version:

```powershell
dotnet build .\MonitorSwitch.csproj --configuration Release
```

The app runs in the system tray rather than opening a main window. Double-click the tray icon to open Settings, or right-click it for profiles and other actions.

## Configure profiles

Open **Settings** from the tray icon:

1. In **Profiles**, add a profile and give it a name.
2. Optionally assign a global hotkey.
3. Select the displays the profile should affect and choose an input for each.
4. Use **Apply this profile now** to try it, then select **Save**.

The input list includes common DisplayPort, HDMI, USB-C, DVI, VGA, and other sources. If your monitor uses a different value, enter its raw VCP value as a decimal number (for example, `15`) or hexadecimal value (for example, `0x0F`).

## Automatic USB switching

In **Settings → USB**, enable automatic switching, select or enter the USB device to watch, and choose the profiles to apply on connect and disconnect. The tray menu also has an **Automatic USB switching** toggle.

The first-run configuration includes example DisplayPort and HDMI 2 profiles and a prefilled USB device identifier. These defaults may not match your monitors or device; review and update them in Settings.

## Configuration

Settings are stored at:

```text
%APPDATA%\MonitorSwitch\config.json
```

Close Monitor Switch before editing this file manually. The app loads the saved configuration when it starts.

## Troubleshooting

- **A profile reports that DDC/CI failed:** enable DDC/CI in the monitor's on-screen menu, and check that the monitor and cable/connection support changing inputs over DDC/CI.
- **A display is missing or marked not connected:** use **Refresh displays** in Settings and confirm Windows currently detects the display.
- **The selected input does not work:** monitor manufacturers may use different VCP input values. Try the monitor's documented value using the raw input field.
- **A hotkey does not work:** another application may already have registered that global shortcut; choose a different combination.
- **USB switching does not trigger:** confirm automatic switching is enabled and the configured device identifier matches the device Windows reports.
