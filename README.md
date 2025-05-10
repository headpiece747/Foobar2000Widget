# Foobar2000Widget

Foobar2000Widget is a widget designed for the WigiDash Widget Framework. It provides an interface to control your Foobar2000 media player and view information about the currently playing media.

## Features

* **Player Control:** Play, pause, skip to the next track, or go to the previous track.
* **Track Information Display:** Shows the title, artist, and album of the currently playing song.
* **Album Art Display:** Fetches and displays album art for the current track. The widget background color can also adapt to the album art.
* **Playback State:** Indicates whether the player is currently playing, paused, or stopped.
* **API Interaction:** Communicates with Foobar2000 via the Beefweb plugin and its API.
* **Customizable Settings:** Allows configuration of the Beefweb API URL.

## Project Structure

The project is built in C# and includes the following key files:

* `Foobar2000WidgetObject.cs`: Defines the widget's metadata such as name, author, description, and supported sizes. It handles the loading of preview images for the widget.
* `Foobar2000WidgetInstance.cs`: Contains the main logic for the widget instance. It handles background polling for player state updates, sends commands to the player, fetches album art, and manages the visual rendering of the widget.
* `BeefwebApiModels.cs`: Defines C# classes that represent the JSON data structures used by the Beefweb Player API for deserializing API responses.
* `Foobar2000WidgetSettings.xaml.cs` & `Foobar2000WidgetSettings.xaml`: Implement the settings user interface for the widget, allowing users to configure options like the Beefweb API URL.
* `Properties/AssemblyInfo.cs`: Contains assembly information for the project.
* `packages.config`: Lists the NuGet packages used by the project.

## Prerequisites

* Foobar2000 media player installed.
* Beefweb plugin (foo_beefweb) installed and configured in Foobar2000.
* WigiDash Widget Framework.

## Setup

1.  Ensure Foobar2000 and the Beefweb plugin are running.
2.  Note the Beefweb API URL (default is usually `http://localhost:8880/api`).
3.  Install the Foobar2000Widget in your WigiDash environment.
4.  Configure the widget instance with your Beefweb API URL if it differs from the default.

## How It Works

The widget polls the Beefweb API at regular intervals (e.g., every 2 seconds) to get the current player state, including active track information and playback status. When a track changes, it attempts to fetch the album art for the new track. User interactions with the widget's control buttons (previous, play/pause, next) send commands to the Foobar2000 player via the Beefweb API.

## Pre-requisites

- Visual Studio 2022
- WigiDash Manager (https://wigidash.com/)

## Getting started

1. Clone this repository
2. Open ClockWidget.csproj in Visual Studio
3. Resolve the dependancy for WigiDashWidgetFramework under References by adding a reference to 
```
C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashWidgetFramework.dll
```
4. Open Project properties -> Build Events and add this to Post-build event command line:
```
rd /s /q "%AppData%\G.SKILL\WigiDashManager\Widgets\$(TargetName)\"
xcopy "$(TargetDir)\" "%AppData%\G.SKILL\WigiDashManager\Widgets\$(TargetName)\" /F /Y /E /H /C /I
```
5. Open Project properties -> Debug and select Start external program: "C:\Program Files (x86)\G.SKILL\WigiDash Manager\WigiDashManager.exe".
6. Start debugging the project, and it should launch WigiDash Manager with your Widget loaded and debuggable.