# Horizon Studio (for N.I.N.A.)

**Horizon Studio** is the definitive, professional-grade tool for creating, editing, and calibrating local horizon profiles in Nighttime Imaging 'N' Astronomy (N.I.N.A.).

Instead of guessing where trees, rooftops, or distant mountains intersect the night sky, Horizon Studio allows you to trace your actual, physical horizon using a live video feed, generating a native `.hrzn` file that prevents your telescope from slewing into obstacles.

---

## ✨ Implemented Features

### 🎥 Live Eyepiece & HUD Overlay
* **Circular Eyepiece Masking:** Applied a custom, symmetric circular Donut Mask over the live camera feed. This hides all tilted black margins of rotated video frames, creating a premium round telescope eyepiece look.
* **Futuristic AR HUD Overlay:** Superimposes a semi-transparent Sky Dome Radar grid (grid lines, cardinal labels, mapped obstruction polygon, active reticle, and mount pointer) directly over the circular webcam feed.
* **Aspect-Ratio-Aware Translation:** Translates and stretches the camera feed uniformly, scaling dynamically to N.I.N.A.'s dockable panel size.
* **Dynamic Cursor & Click Mapping:** The mouse cursor changes to a Hand (👆) when hovering over clickable horizon regions. Clicking near the horizon line on the HUD immediately slews the mount to that Alt/Az position.

### 🔄 Co-Alignment & Equatorial Counter-Rotation
* **Webcam Co-Alignment Assistant:** Features an interactive co-alignment assistant. When aligning, the HUD overlays and masks are temporarily hidden, presenting a full rectangular view. Center a landmark in your main telescope, click the same target on the webcam feed, and the plugin locks that offset.
* **Precision Optical-Axis-Centered Rotation:** When counter-rotating, the grid is translated to shift the alignment landmark to the center, then rotated. This ensures the target landmark stays absolutely stationary during camera rotation.
* **Equatorial Counter-Rotation:** Automatically calculates the continuous Parallactic Angle ($q$) from Altitude, Azimuth, and Latitude to rotate the webcam feed, keeping your physical horizon level in the feed.
* **GEM Meridian Flip Tracking:** Fully tracks and compensates for German Equatorial Mount (GEM) meridian flips by monitoring `SideOfPier` (falling back to Hour Angle mathematical signs on older mounts) to apply the correct $180^\circ$ orientation shift, eliminating upside-down feeds.

### 🗺️ Interactive Sky Dome Radar
* **Premium Shaded Obstruction Zone:** Renders a smooth, 360-point polar projection loop. Constructs a beautiful, hollow shaded obstruction zone filling the outer ring representing the blocked low-altitude sky while keeping the zenith dark.
* **Live Sorting:** The `HorizonNodes` collection is kept sorted by Azimuth in real-time. Dropping a pin automatically inserts it at its mathematically correct index to prevent interpolation artifacts.
* **Interactive Radar Click-to-Slew:** Clicking directly on the Sky Dome Radar projects your click to Alt/Az. If within $2.5^\circ$ of a saved node, it snaps to it; otherwise, it slews to the interpolated horizon altitude at that azimuth.

### 🕹️ Traversal, Verification & Safety Locks
* **CW / CCW Traversal Controls:** Features circle-geometry aligned `◀ Slew CCW` (decreasing Azimuth) and `Slew CW ▶` (increasing Azimuth) buttons with automatic $360^\circ$ circular modulo wrapping.
* **Intelligent Traverse Start:** Traversal automatically begins at the closest node to the mount's current Azimuth, stepping forward or backward to prevent stationary double-clicks.
* **Direct UI Action Lock:** Integrates a synchronous local action lock (`IsActionSlewing`) that disables all jog buttons, traversal buttons, and homing commands on the UI thread when a slew is active, preventing driver collisions.
* **Large Azimuth Slew Safeguard (> 45°):** Displays a confirmation dialog before executing a traversal slew larger than $45^\circ$, protecting your mount and cables from sudden large jumps.
* **Safety Lockout Prevention:** The solar and zenith safety systems monitor safety status at a 1 Hz heartbeat. Lockouts are throttled so that emergency stops are only triggered once, preventing UI button lockout spam.

### 🛠️ Advanced Pin & Session Management
* **Always-On Mapping Mode:** The redundant "Start/Stop Mapping" buttons have been completely removed. The plugin is in a running mapping state by default and is continuously active.
* **Safe Action-Triggered Tracking Suspension:** Sidereal tracking is not disabled when the mount connects, protecting your imaging sessions. Tracking is only automatically suspended the moment you interact with mapping controls (jog, slew, or drop pin).
* **Automatic Pin Selection:** Dropping a new horizon pin immediately highlights and selects it, displaying its Alt/Az coordinates in the options card.
* **"Remove Current Pin" Command:** Replaced "Undo Last Pin" with a precise deletion system that deletes whichever node is currently selected on the map.
* **Proximity Button Deactivation:** Automatically disables the "Drop Horizon Pin" button when the mount is positioned within $0.05^\circ$ of the active pin, preventing accidental double-drops. The button reactivates instantly as the mount moves.

### 📂 Profile Import & Review (.hrzn)
* **Load Horizon Profile:** Added a premium emerald-green load button in a clean two-column grid.
* **Overwrite Protection:** Prompts with a confirmation dialog before loading if nodes are already present in the active map.
* **Robust Parser:** Parses standard space, tab, or comma-separated Azimuth-Altitude coordinate pairs from N.I.N.A. `.hrzn` files, gracefully skipping malformed data and empty lines.
* **Sorting & Radar Sync:** Automatically re-sorts imported points by Azimuth, immediately drawing the imported polygon onto the Sky Dome Radar.

---

## 🔮 Future Features Roadmap

### 4. Profile Synchronization (3D Tilt Correction)
* **Goal:** Fix "shifted" profiles caused by tripod bumps, polar alignment changes, or optical co-alignment errors without needing to remap the entire sky.
* **Concept:** 
  1. Slew to a known landmark node.
  2. Click **Prepare Sync**, then manually jog the mount until the physical landmark is perfectly centered in your eyepiece.
  3. Click **Confirm Sync**. 
  4. The plugin calculates the exact Alt/Az offset and mathematically warps the rest of the profile.
* **Mathematics:**
  * **Azimuth Correction:** Rotational offset: `Az_new = (Az_old + ΔAz) % 360`
  * **Altitude Correction:** Mount tilt behaves like a tilted 3D plane, modeled via a cosine wave:
    `Alt_new = Alt_old + (ΔAlt * cos(Az_old - Az_ref))`
    *(Where `ΔAlt` is the altitude error at the sync point, and `Az_ref` is the azimuth of the sync point).*

### 7. Main Camera Integration (Dawn / Daytime / Nighttime Mapping)
* **Goal:** Allow users to use their primary astronomical camera to map the horizon instead of requiring a separate USB webcam.
* **UI Additions:** A camera source toggle ("USB Webcam" vs "Main Camera") and a premium HUD star-count indicator.
* **Daytime Auto-Exposure Algorithm:** Runs an automatic exposure/gain calculation loop (similar to N.I.N.A.'s Flat Wizard) to dynamically maintain a target ADU, keeping the daytime horizon silhouette crisp.
* **Nighttime Star-Detection Indicator (Dark Horizon Mapping):**
  * Bypasses the auto-exposure loop at night, using default camera/exposure settings to ensure stars are sufficiently exposed and visible.
  * Uses N.I.N.A.'s internal HFR/star detection algorithm to monitor and display **Star Count** in the HUD/UI.
  * **Horizon Detection Rule:** If the camera is pointed above the horizon, the star count will be high. The moment the telescope is slewed down and the field of view is obstructed by a local landmark (e.g. a tree or building), the star count will drop dramatically (e.g. near zero), allowing precise and automatic mapping of the horizon line even in complete darkness!

---

## 🛠️ Requirements
* **N.I.N.A.** (Version 3.0 or higher)
* An equatorial mount connected via ASCOM/Alpaca
* A wide-angled DirectShow Webcam **OR** a connected ASCOM/native primary camera

---

## 🚀 Basic Workflow

### Creating a New Profile
1. Connect your Mount and Camera in N.I.N.A.
2. Open the Horizon Studio dockable tab.
3. Co-align the on-screen targeting crosshair with your telescope's main optics.
4. Use the on-screen jog controls to move your mount to the peak of an obstacle (e.g., the top of a tree).
5. Click **Drop Pin** to record the Alt/Az coordinate. The pin is auto-selected, and the radar obstruction polygon updates in real-time.
6. Move to the next obstacle and repeat.
7. Click **Save Horizon Profile** to export your custom `.hrzn` file.

### Editing, Fixing, or Syncing Profiles
You can continue refining your current mapping session, or click **Load Horizon Profile** to load a saved `.hrzn` file.
* **Add & Delete Pins:** Delete inaccurate data points using **Remove Current Pin**, or drop new pins directly.
* **Verify Points:** Use `◀ Slew CCW` and `Slew CW ▶` controls to automatically slew the mount to saved points and visually verify their accuracy.
* **Sync Entire Profile:** If you bumped your tripod or adjusted your polar alignment, use the upcoming 3D Tilt Correction (Feature #4) to warp and correct the entire profile from a single synchronized point.

---

## 📝 License
Copyright (c) Nir Zonshine. All rights reserved.
