# Horizon Studio - Code Architecture and Structure Guide

**Project Codebase Metrics (As of May 27, 2026):**
- **Total Lines of Source Code:** 5,645 lines (C# and XAML, excluding `bin`, `obj`, and `scratch` directories)
- **Architecture Design:** Composed MVVM (Model-View-ViewModel)
- **Maximum File Line Threshold:** < 400 lines per file

This document describes the internal structure, directories, design patterns, and data flows of the **Horizon Studio** N.I.N.A. plugin. 

Following a major modular refactoring, the project adheres to a clean, decoupled **MVVM (Model-View-ViewModel)** architectural pattern. Every file is strictly organized, maintains single-responsibility, and is kept **under 400 lines of code** for maximum readability, maintainability, and testability.

---

## 🏛️ Architectural Overview

Horizon Studio is structured into five distinct, decoupled layers:

```mermaid
graph TD
    subgraph View Layer (.xaml Controls)
        OptionsXAML["Options.xaml (Main Hub)"]
        OptionsXAML --> HUDView["HUDOverlayView.xaml (Video HUD)"]
        OptionsXAML --> RadarView["SkyDomeRadarView.xaml (2D Canvas)"]
        OptionsXAML --> LandmarkCard["LandmarkManagementCard.xaml (Landmarks List)"]
        OptionsXAML --> HorizonCard["HorizonManagementCard.xaml (Horizon Pins)"]
        OptionsXAML --> JoggingCard["JoggingCard.xaml (Mount Jogging)"]
    end

    subgraph ViewModel Layer (Composed VMs)
        MainVM["HorizonMapperDockableVM.cs (Root)"]
        MainVM --> CameraVM["CameraViewModel.cs (Main Camera Loops)"]
        MainVM --> WebcamVM["WebcamViewModel.cs (USB Streams)"]
        MainVM --> RadarVM["RadarViewModel.cs (Polar Projection)"]
        MainVM --> LandmarkVM["LandmarkViewModel.cs (Sync State)"]
    end

    subgraph Command Layer (Modular Actions)
        MainVM --> FileCmds["FileCommands.cs (Save/Load .hrz)"]
        MainVM --> LandmarkCmds["LandmarkCommands.cs (CRUD Operations)"]
        MainVM --> SyncCmds["SyncCommands.cs (3D Cosine-Warp)"]
        MainVM --> NavCmds["NavigationCommands.cs (Slews & Pins)"]
        MainVM --> JogCmds["MountJogCommands.cs (Step Jogging)"]
    end

    subgraph Services Layer (System Interfaces)
        MainVM --> ThreadHelper["ThreadHelper.cs (UI Dispatcher)"]
        MainVM --> SafetyMgr["SafetyManager.cs (Solar/Zenith Limits)"]
        MainVM --> SettingsMgr["SettingsManager.cs (Profile Storage)"]
        MainVM --> WebcamSvc["WebcamService.cs (FlashCap Driver)"]
    end

    subgraph Domain Layer (Plain Data Models)
        RadarVM --> NodeModel["HorizonNode.cs (Spherical Node)"]
        LandmarkVM --> LandmarkModel["SyncLandmark.cs (Observable Landmark)"]
    end
```

---

## 📂 Directory Map

Below is a detailed layout of all source directories and files:

```bash
Horizon Studio/
│
├── Domain/                         # Plain C# Data Models & DTOs
│   ├── HorizonNode.cs             # Represents a single Alt/Az horizon trace point
│   ├── SyncLandmark.cs            # Named calibration point with mutable Alt/Az
│   └── HorizonProgressReport.cs   # DTO carrying mapping logs and safety status
│
├── Services/                       # Core System Interfaces & Background Workers
│   ├── AstronomyHelper.cs         # Shared spherical trigonometry (GetAngularDistance)
│   ├── ThreadHelper.cs            # Null-safe, thread-safe UI thread dispatcher
│   ├── SafetyManager.cs           # Zenith limits and real-time solar tracking safety
│   ├── SettingsManager.cs         # Reads/writes plugin settings to NINA active profile
│   ├── IWebcamService.cs          # Interface defining USB video capture services
│   ├── WebcamService.cs           # UVC webcam capture implementation (FlashCap wrapper)
│   └── DeviceDescriptor.cs        # Struct representing metadata for discovered webcams
│
├── ViewModels/                     # MVVM ViewModel Layer (State & Binding properties)
│   ├── SubViewModelBase.cs        # Abstract class implementing INotifyPropertyChanged
│   ├── RelayCommand.cs            # ICommand implementation forwarding executing delegates
│   │
│   ├── HorizonMapperDockableVM.cs # ROOT VM orchestrator (composition entry, MEF export)
│   ├── HorizonMapperDockableVM.Properties.cs # NINA settings forwarding properties
│   ├── HorizonMapperDockableVM.Commands.cs   # Direct ICommand bindings exposed to UI
│   ├── HorizonMapperDockableVM.State.cs      # Tick timer polling & hardware connections
│   │
│   ├── CameraViewModel.cs         # Looping exposure controls, auto-exposure, star counts
│   ├── WebcamViewModel.cs         # Webcam streams, alignment ratios, and co-alignment
│   ├── WebcamViewModel.Rotation.cs # Parallactic angle calculations & field rotation
│   ├── RadarViewModel.cs          # Calculates polar mappings & coordinate traces
│   ├── LandmarkViewModel.cs       # Stores collections and active sync state workflows
│   │
│   └── Commands/                  # Decoupled Command Execution handlers
│       ├── FileCommands.cs        # Profile serializing/parsing (.hrz, .hrzn legacy)
│       ├── LandmarkCommands.cs    # Landmark CRUD, slew-to-landmark, renames
│       ├── SyncCommands.cs        # 3D cosine-tilt profile warping
│       ├── MountJogCommands.cs    # Step jogging and settling settle micro-jumps
│       ├── NavigationCommands.cs  # Pin tracking and click-slewing base
│       ├── NavigationCommands.Mapping.cs # Mapping session, Pin drop/undo stack
│       ├── NavigationCommands.Slew.cs    # TRAVERSAL slews (CW/CCW) & canvas clicks
│       └── RenameDialog.cs        # Static WPF overlay dialog for naming landmarks
│
├── Views/                          # WPF UserControl View Components
│   ├── CameraConfigCard.xaml      # Left panel: Feed choice, Exposure settings
│   ├── HUDOverlayView.xaml        # Center panel: Circular webcam feed & canvas HUD
│   ├── SkyDomeRadarView.xaml      # Center panel: 2D polar projection of obstructions
│   ├── HorizonManagementCard.xaml # Right panel: Pins table, CCW/CW, export options
│   ├── LandmarkManagementCard.xaml # Right panel: Landmarks checklist, Slew, Rename
│   └── JoggingCard.xaml           # Right panel: 8-direction Alt/Az keypad controls
│
└── Options.xaml                    # ROOT Dictionary merging views in NINA Workspace
```

---

## 🔑 Core Layers & Responsibilities

### 1. View Layer (`/Views`)
Rather than relying on a giant monolithic interface, the UI is split into visual cards.
* **`HUDOverlayView`**: Custom-clips the video feed into a circular telescope eyepiece. Projects coordinate lines, co-alignment targets, fuchsia landmark diamonds, and active nodes directly onto the live feed. It automatically translates layout clicks to UniformToFill image ratios.
* **`SkyDomeRadarView`**: Draws a polar map canvas. Fills blocked sky directions in semi-transparent cyan, maps nodes into orange dots, and draws a red crosshair representing the active telescope position.
* **`Options.xaml`**: Merges all cards together, binding them explicitly to their corresponding sub-ViewModel data contexts.

### 2. ViewModel Layer (`/ViewModels`)
Employs **ViewModel Composition** (Composition VM pattern) to split large logic blocks while preserving clean state-forwarding:
* **`HorizonMapperDockableVM` (Root)**: Composes N.I.N.A. system mediators (Camera, Telescope, Profile) and exposes them to sub-VMs. Spreads settings and state timers through partial classes.
* **`CameraViewModel`**: Controls looping exposures, automatically calculates ADU changes, and initiates star count and HFR detection via N.I.N.A. core engines.
* **`WebcamViewModel` & `WebcamViewModel.Rotation`**: Connects DirectShow streams and computes real-time field rotation using observer latitude, mount Alt/Az coordinates, and physical pier side telemetry.
* **`RadarViewModel`**: Calculates the coordinates representing 2D polar projection points:
  $$x = x_{\text{center}} + r \cdot \sin(\theta), \quad y = y_{\text{center}} - r \cdot \cos(\theta)$$
  interpolating Alt/Az obstructions smoothly clockwise.

### 3. Commands Layer (`/ViewModels/Commands`)
Commands are decoupled from ViewModels to keep files clean and modular:
* **`SyncCommands`**: Manages the **3D Cosine-Tilt correction** calculations. If a physical landmark drift is measured, it warps the entire Alt/Az horizon trace to align with the mount's physical tilt:
  $$\Delta\text{Alt}_{\text{warp}} = \Delta\text{Alt}_{\text{ref}} \cdot \cos\left((\text{NodeAz} - \text{RefAz}) \cdot \frac{\pi}{180^\circ}\right)$$
* **`NavigationCommands`**: Hosts the pin history `Stack<HorizonNode>` for multi-step Undos, snuffs out sidereal tracking during manual traces, and snaps cursor clicks to nearest landmarks or trace lines.
* **`MountJogCommands`**: Controls mount movement, verifies solar/zenith bounds before execution, and implements **Exact Position Micro-Jumps** to compensate for settling drift errors.

### 4. Services Layer (`/Services`)
* **`ThreadHelper`**: Coordinates robust WPF UI thread dispatching. It null-guards `Application.Current` during plugin unload or shutdown cycles and optimizes access by running synchronously if called directly from the UI thread.
* **`SafetyManager`**: Drives a 1Hz safety timer. Calculates solar coordinates using low-precision Keplerian equations and issues emergency `StopSlew()` commands if zenith limit ($>85^\circ$) or solar lockout thresholds are violated.

---

## 🔄 Core Data & Event Flows

### Camera Capture Loop
```
[CameraViewModel] ──> Exposes light frames ──> [ImagingMediator]
                                                     │ (Async Capture)
                                                     ▼
[WebcamViewModel] <── Updates LastFrame Image <── [CaptureImage Async]
        │
        ▼ (UI Thread Marshal via ThreadHelper)
[HUDOverlayView.xaml] (Updates Image brush & overlays reticle)
```

### Horizon Pin Mapping
```
[TelescopeInfo] ──> (1Hz Telemetry Alt/Az) ──> [HorizonMapperDockableVM]
                                                         │ (DropPin Command)
                                                         ▼
[NavigationCommands] ──> Inserts Node ──> [HorizonNodes Collection]
                                                         │
                                                         ▼ (Event Trigger)
[RadarViewModel] ──> Computes Points ──> [SkyDomeRadarView.xaml] (Draws Polygon)
```

### Profile Warp Calibration
```
[Mount Positioned on Physical Target] ──> [SyncRefNode Set]
                                                │
                                                ▼ (ConfirmSync Command)
[SyncCommands] ──> Computes DeltaAlt & DeltaAz
     │
     ├── Applies 3D Cosine-Tilt Warp on HorizonNodes
     ├── Shifts SyncLandmarks Coordinates
     └── Re-orders nodes by Azimuth ──> [Options.xaml] (Triggers Visual Redraw)
```

---

## 🛠️ Design Patterns Applied

1. **Composition VM**: Avoids giant monolithic C# files by building a modular hierarchy of specialized sub-ViewModels under a root composer.
2. **Command Pattern**: Encapsulates actions into isolated command classes (`FileCommands`, `SyncCommands`), keeping UI triggers completely decoupled from the orchestrator.
3. **Reactive UI Dispatching**: Uses `ThreadHelper` to marshal data across background asynchronous hardware threads and the WPF UI thread seamlessly.
4. **Observer Pattern**: Settings and properties bind reactively via custom `SettingsManager` and `SafetyManager` `INotifyPropertyChanged` notification networks.
