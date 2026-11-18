# Sunwall for Windows

A port of the macOS app: a tray application that paints the light coming through a
window onto your wallpaper, following the real position of the sun where you are.

Everything the Mac version does is here - the twelve looks, live cloud cover, shadows,
the time-of-day scrubber, full-wallpaper mode - built on Win32 and GDI+ instead of
AppKit and Core Graphics.

## Requirements

Windows 10 or 11, and nothing else. `build.bat` uses the C# compiler that ships with
the .NET Framework, so there is no SDK, Visual Studio or NuGet package to install.

## Build

On a Windows 10 or 11 machine:

1. Get this folder onto the PC - clone the repository, copy it from a USB stick, or unzip
   it. Only the `windows` folder is needed; the Swift sources are not.
2. Open a Command Prompt in `windows`. Shift-right-click the folder and choose
   "Open in Terminal" (or "Open command window here"), or `cd` to it.
3. Run:

        build.bat

4. It writes `Sunwall.exe` beside the script. Double-click it to start.

Nothing needs installing. `build.bat` compiles with the C# compiler that ships with the
.NET Framework - `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, falling back to
the 32-bit folder - and writes a single self-contained exe. It uses no NuGet package and
no project file.

The sources are plain ASCII and `build.bat` is CRLF, so the in-box compiler reads them
correctly on a default install. `.gitattributes` keeps the batch file CRLF through a
checkout.

Windows may warn about a freshly built program that has no signature (SmartScreen).
Choose "More info", then "Run anyway"; or right-click the exe, Properties and Unblock.

After editing a source, run `build.bat` again.

## Run

Sunwall starts with no window: it puts a sun icon in the notification area. Right-click
it for the menu, double-click it for the sliders.

- **Look** - the twelve light shapes.
- **Sliders...** - opacity, brightness, and a time-of-day scrubber.
- **Follow real time** - back to the live sun after scrubbing.
- **Clouds** - live weather, or forced clear, partly cloudy, overcast.
- **Shadows** - off, soft or deep.
- **Replace wallpaper (full mode)** - paints its own wall instead of overlaying yours.
- **Set location...** - decimal degrees; leave both fields empty to use network location.
- **Launch at login** - adds the app to the current user's Run key.
- **Quit Sunwall**.

Settings live under `HKEY_CURRENT_USER\Software\Sunwall`.

## How it stays cheap

The Mac version was reworked to stop redrawing when nothing has changed, and this port
keeps that: a once-a-minute timer, a key over everything the picture depends on, and no
redraw at all while the sun is more than eight degrees below the horizon, because the
image is identical then. Drawing also pauses while the machine is asleep. Opacity is
applied as the window's constant alpha, so dragging that slider never repaints.

## Windows-specific differences from the Mac version

- **Location**: there is no CoreLocation here. Sunwall asks `ipapi.co` once for an
  approximate position and otherwise uses the coordinates you enter. Nothing is sent
  anywhere except that request and the Open-Meteo cloud-cover lookup.
- **Where it draws**: the windows are children of the desktop's own wallpaper window
  (`WorkerW`), so the light sits above your wallpaper and below the desktop icons,
  the same place the Mac version puts it. If Explorer restarts, the windows are
  recreated on the next tick.
- **Blur**: GDI+ has no Gaussian filter, so the light patch is blurred with three box
  passes on the pixels. It looks the same; it is not bit-identical.
- **DPI**: the offscreen light patch is rendered at half resolution, matching the Mac
  build at 1x, then blurred and scaled up.

## Status

This port was written against the macOS source but has not been compiled or run - it
was built on a Mac, where no Windows toolchain is available. Treat the first build as
unverified, and expect at most small things to fix rather than a redesign.
