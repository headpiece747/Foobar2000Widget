# Foobar2000 Player Control & Media Deck Widget for WigiDash

Foobar2000Widget is a rich, dynamic media control and tracking widget built for the **WigiDash Widget Framework**. It connects to your [Foobar2000](https://www.foobar2000.org/) media player via the [Beefweb API plugin (`foo_beefweb`)](https://github.com/hyperblast/beefweb) to display real-time track info, interactive touch controls, and stunning dynamic artwork backgrounds on your WigiDash touch screen.

---

## ✨ Features

* **👆 Touch-Screen Progress Bar Seeking:**
  Tap or drag anywhere along the progress bar (`+/- 16px` vertical touch zone) to instantly scrub forward or backward through your track with zero latency (`SetPlayerPositionAsync`). The scrubber dot jumps immediately under your finger upon contact.
* **🎨 Vibrancy-Aware Brightest-Color Artwork Backgrounds:**
  Automatically scans the currently playing album art (`32x32` thumbnail sample) and extracts the **brightest and most vibrant color** on the artwork. Uses a two-pass vibrancy filter (`GetSaturation() >= 0.22`) so that rich artwork colors (like vivid red or royal blue) always take priority over pale skin tones, white microphones, or muddy grey highlights. Includes an automatic contrast safety guard (`0.65` brightness cap) so crisp white text and buttons remain sharp and legible even on pale covers.
* **⏱️ Real-Time Playback Timestamps (`M:SS`):**
  Displays real-time current track position (e.g., `1:24`) below the left edge and total duration (e.g., `3:07`) below the right edge of the progress bar using zero-overhead time formatting (`~0.001 ms`).
* **📐 Ergonomic Media Control Deck:**
  * **Wide Touch Spacing (`28px`):** Generous separation between `Previous`, `Play/Pause`, and `Next` buttons prevents accidental mis-taps on touch screens.
  * **Circular Glass Pill Highlight:** A translucent white circular background highlight around the center `Play/Pause` (`||` / `▶`) button makes playback control pop.
* **📝 Hierarchical Typography & Flush Alignment:**
  * **Visual Hierarchy:** Features bold `24pt` Track Title paired with crisp `16pt` (80% scaled) `Album Artist` and `Album Name` lines.
  * **Flush Left-Alignment:** All track info lines up vertically along a clean guide right above the left edge of the progress bar (`35px` clear of the album artwork).
* **⚡ Ultra-Lightweight & Zero-Overhead Idle:**
  Background polling runs at `2 FPS` (`500 ms`) while playing and drops to `0 CPU / 0 Overhead` when paused or stopped.

---

## 📋 Prerequisites

* **Foobar2000** media player installed and running.
* **Beefweb Plugin (`foo_beefweb`)** installed in Foobar2000 (`File -> Preferences -> Components -> Install`).
  * Default API URL: `http://localhost:8880/api`
* **WigiDash Manager** ([https://wigidash.com/](https://wigidash.com/)) installed on Windows.

---

## 🚀 Getting Started

You can install the widget quickly from a pre-built **Release Zip**, or build it yourself using **Visual Studio**.

### Option A: Quick Installation via Release Zip (No Building Required)

If you simply want to use the widget right away without compiling code:

1. Go to the **[Releases](https://github.com/headpiece747/Foobar2000Widget/releases)** page and download the latest release zip (e.g., `Foobar2000Widget-v1.0.5.zip`).
2. Open your Windows File Explorer and navigate to your WigiDash Manager widgets directory:
   ```text
   %AppData%\G.SKILL\WigiDashManager\Widgets\Foobar2000Widget\
   ```
   *(If the `Foobar2000Widget` folder does not exist yet inside `Widgets\`, create it).*
3. Extract all files from the `.zip` (including `Foobar2000Widget.dll` and its dependencies) directly into that `Foobar2000Widget\` folder.
4. Restart **WigiDash Manager** (`WigiDashManager.exe`) or reload your widget layout grid.
5. Drag **foobar2000 Player** (`5x4`) onto your WigiDash screen. If your Beefweb API is on a custom port/URL, click the gear icon on the widget to configure it!

---

### Option B: Building & Debugging with Visual Studio (For Developers)

If you want to modify code, customize colors, or step through with the debugger:

#### Prerequisites for Development
* **Visual Studio 2022 / 2026** with `.NET Desktop Development` workload (`.NET Framework 4.8.1 SDK`).
* **WigiDash Manager** installed at `C:\Program Files (x86)\G.SKILL\WigiDash Manager\`.

#### Step-by-Step Build Guide
1. **Clone this repository:**
   ```bash
   git clone https://github.com/headpiece747/Foobar2000Widget.git
   cd Foobar2000Widget
   ```
2. **Open the solution:**
   Open `Foobar2000Widget.sln` (or `Foobar2000Widget.csproj`) in Visual Studio.
3. **Verify WigiDash Framework Reference:**
   Under **References** in Solution Explorer, verify `WigiDashWidgetFramework` points to:
   ```text
   C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashWidgetFramework.dll
   ```
4. **Post-Build Auto-Deployment:**
   The project is pre-configured with MSBuild Post-Build events (`Foobar2000Widget.csproj`) that automatically copy built DLLs directly into your active WigiDash directory on every compilation (`Debug` or `Release`):
   ```cmd
   rd /s /q "%AppData%\G.SKILL\WigiDashManager\Widgets\$(TargetName)\"
   xcopy "$(TargetDir)\*" "%AppData%\G.SKILL\WigiDashManager\Widgets\$(TargetName)\" /F /Y /E /H /C /I
   ```
5. **Start Debugging (`F5`):**
   Open **Project Properties -> Debug** and ensure **Start external program** is set to:
   ```text
   C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashManager.exe
   ```
   Press **`F5`** (`Start Debugging`). Visual Studio will compile the widget, deploy it to `%AppData%`, and launch WigiDash Manager with the debugger attached so you can test live breakpoints and UI changes!

---

## 📁 Project Structure

* `Foobar2000WidgetObject.cs` — Defines widget metadata, GUID (`{45036078-FE39-4A51-9C01-D7DA1677686B}`), supported dimensions (`5x4`), dynamic version reporting (`Version`), and thumbnail/preview image handling.
* `Foobar2000WidgetInstance.cs` — Core widget lifecycle: handles `500 ms` polling, Beefweb REST API requests, dynamic brightest-color extraction (`UpdateBackgroundColorFromAlbumArt`), GDI+ rendering (`DrawWidget`), touch progress bar seeking (`ClickEvent`), and command execution (`SendPlayerCommandAsync`).
* `BeefwebApiModels.cs` — Strongly-typed C# JSON deserialization models for Beefweb player state, active item metadata, and playback status (`System.Text.Json`).
* `Foobar2000WidgetSettings.xaml` / `.xaml.cs` — WPF Settings dialog interface for configuring the Beefweb endpoint URL.
* `Properties/AssemblyInfo.cs` — Assembly metadata and 3-part version numbering (`1.0.5`).

---

## 📄 License & Credits
Author: **headpiece747** ([https://eclipticsight.com](https://eclipticsight.com))  
Designed for the **WigiDash Widget Framework**. Compatible with Foobar2000 & `foo_beefweb`.