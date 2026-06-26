# Horizon Studio (for N.I.N.A.)

**Horizon Studio** is a professional-grade tool for creating, editing, and calibrating local horizon profiles in Nighttime Imaging 'N' Astronomy (N.I.N.A.).

---

## 📖 Introduction & Core Architecture

Traditional methods of mapping a local horizon require guessing where trees, rooftops, or distant mountains intersect the night sky. Horizon Studio eliminates this guesswork by allowing you to trace your actual, physical horizon using a live video feed, generating a native N.I.N.A. `.hrz` file that prevents your telescope from slewing into obstructions.

The plugin is designed around four key core capabilities:

1. **Wide-Angle Webcam Integration (Primary Source):** Designed primarily to utilize a wide-angle USB webcam mounted parallel to your telescope. This provides immediate, real-time daylight spatial awareness of your local obstructions. It features **Equatorial Counter-Rotation** (compensating for field rotation in real-time as your mount slews, keeping the physical ground level) and **Interactive Live Zoom** (1.0x to 3.0x) for precise positioning.
   * *Fallback Main Camera Support:* For setups without a webcam (or for users who choose not to add one to their rigs), you can use your main astronomical imaging camera to capture looping exposure feeds with auto-exposure ADU scaling and a real-time star-count overlay.
2. **Alt-Az Jogging Simulation on Equatorial Mounts:** Tracing a level horizon with an equatorial mount is notoriously difficult because standard mount controls move along RA/Dec lines (which tilt across the sky). Horizon Studio handles this by simulating Alt-Az steps—giving you a virtual **5x5 Jog Grid** that moves the mount directly along altitude and azimuth vectors by automatically translating Alt-Az coordinates in real-time, allowing you to walk levelly across roof lines and tree lines.
3. **Interactive Sky Dome & Eyepiece Mapping:** Features a circular telescope eyepiece HUD view and a 2D Polar Sky Dome Radar. You can double-click anywhere on the eyepiece overlay or click on the radar to slew directly to that sky position. You can easily select, add, or delete nodes, and step through them sequentially clockwise (`Slew CW ▶`) or counter-clockwise (`◀ Slew CCW`) by azimuth proximity.
4. **3D Profile Warping & 3D Tilt Correction (Align & Calibrate):** *One of the plugin's most powerful capabilities.* Tracing a horizon is best done during the day when obstructions are clearly visible, but polar aligning your mount at night shifts the coordinate grid, meaning your saved profile no longer matches the sky. **3D Tilt Correction** allows you to automatically warp, tilt, and shift the entire horizon profile based on a single reference point (either a saved horizon pin or a collection of permanent terrestrial landmarks).

---

## ✨ Implemented Features

### 🔄 Webcam Co-Alignment, Rotation & Zoom
* **Co-Alignment Assistant:** Sync the webcam's optical axis with your main telescope by centering a landmark in the main camera and clicking the same target in the webcam feed.
* **Equatorial Counter-Rotation:** Computes parallactic angles dynamically using mount coordinates, pier side telemetry, and observer latitude to rotate the video feed in real-time, keeping the ground level.
* **Interactive Live Zoom:** Magnify the live webcam feed from **1.0x to 3.0x** using a dedicated slider, making fine-grained landmark target clicks during co-alignment easy without affecting optical offset precision.

### 📐 Alt-Az Jogging Simulation
* **5x5 Directional Jog Grid:** Move your telescope in precise altitude and azimuth increments. The plugin automatically converts Alt-Az steps into equatorial slews on the fly.
* **Exact Position Micro-Jumps:** Automatically measures settling drift and applies a predictive lead to execute precise micro-adjustments, canceling out mount drift errors.

### 🌐 3D Profile Warping & 3D Tilt Correction
* **Profile Synchronization:** Warps the entire horizon profile dynamically using 3D cosine-tilt correction:
  $$\Delta\text{Alt}_{\text{warp}} = \Delta\text{Alt}_{\text{ref}} \cdot \cos\left((\text{NodeAz} - \text{RefAz}) \cdot \frac{\pi}{180^\circ}\right)$$
* **Horizon Sync:** Select any existing horizon pin, slew to it, center the physical obstruction in your eyepiece, and calibrate the entire profile.
* **Landmark Sync:** Save permanent terrestrial landmarks (e.g. antenna tips, church steeples) that sit above/below the horizon, and use them to recalibrate the profile in future sessions (e.g., after polar alignment or mount teardown).

### 🕹️ Interactive Eyepiece View & Click-to-Slew
* **Circular Eyepiece HUD:** Clips the camera view into a clean telescope eyepiece circular overlay.
* **Live AR Overlays:** Projects the polar radar grid, cardinal directions, active obstruction lines, and mount crosshairs directly onto the live feed.
* **Click-to-Slew Navigation:** Double-click on the camera HUD or click on the Sky Dome Radar to slew the telescope.
* **Sequential Traversal:** Step through saved pins and landmarks clockwise or counter-clockwise by physical azimuth proximity, automatically updating the active selection.

### 📷 Main Camera Integration (Fallback Mode)
* **Astronomical Camera Feeds:** Integrates with N.I.N.A.'s primary camera system to capture looping exposure feeds.
* **Auto-Exposure ADU Scaling:** Automatically scales exposure times to maintain a targeted ADU brightness level.
* **Star Detection Telemetry:** Computes and overlays star counts and median HFR (Half Flux Radius) on the live HUD in real-time.
* **Aborts & Driver Protection:** Handles exposure interruptions and hardware serialization to protect camera drivers during feed transitions.

### 🛠️ Horizon Pin Management & File Export
* **Dynamic Pin Dropping:** Drop nodes at your mount's position to build the horizon profile in real-time. Pins are automatically kept sorted by Azimuth.
* **Point Editing & Deletion:** Select any node to view its coordinates, slew to it, or delete it from the profile.
* **SkySafari PNG Export:** Save your profile as an equirectangular PNG, rendering your obstructions in 70% semi-transparent dark blue bounded by a solid red line for SkySafari mobile devices.
* **Cartes du Ciel (CdC) Compatibility:** Standard `.hrz` profiles can be imported directly into Cartes du Ciel as a local horizon coordinate chart.

---

## 🛠️ Requirements
* **N.I.N.A.** (Version 3.0 or higher)
* An equatorial mount connected via ASCOM/Alpaca
* A wide-angled DirectShow USB Webcam (Highly Recommended for macro spatial awareness and fast mapping) **OR** your main imaging camera (via N.I.N.A. integration)
  * 📱 **Smartphone as a Webcam**: You can use your smartphone (iOS or Android) as a high-quality wireless camera feed! We highly recommend using **Iriun Webcam** ([iriun.com](https://iriun.com/)) over WiFi or USB. We advise to lock the video stream orientation on your phone when using this setup to keep the stream aligned with the telescope's physical orientation.

---

## 🚀 Step-by-Step User Guide

### 1. Creating a New Horizon Profile
1. Connect your Mount and Camera in N.I.N.A., then open the **Horizon Studio** tab.
2. Align your camera crosshairs with your main telescope using the co-alignment helper.
3. Use the jogging controls to move the mount to the peak of a local obstacle (e.g. a tree top or roof line).
4. Click **Drop Pin** to save that horizon point.
5. Move the mount to the next obstacle along the horizon and click **Drop Pin** again. Repeat until you have mapped your sky.
6. Click **Save Horizon Profile** to save your `.hrz` file.

> 💡 **Pro-Tip:** The fastest way to build a profile is to map 3–4 macro "anchor points" around your sky first (e.g., major roof peaks or cardinal direction markers). Once those are dropped, click along the generated radar line to automatically slew nearby, and use the jogging controls to fine-tune the subtle dips and peaks.

---

### 2. Loading & Editing Profiles
* **Load Profile:** Click **Load Horizon Profile** to load a previously saved `.hrz` file.
* **Add/Modify Pins:** Slew to any area and click **Drop Pin** to add new nodes.
* **Delete Pins:** Click a node on the radar to select it, then click **Delete Node** to remove it.
* **Verify Coords:** Click `◀ Slew CCW` or `Slew CW ▶` to step through both your mapped nodes and landmarks by physical azimuth proximity, verifying that the telescope points clear of physical obstructions.

---

### 3. Warp & Align Profiles (3D Tilt Correction)
The most common workflow is to build your horizon profile during the daytime — when you can clearly see trees, rooftops, and other obstructions — before your mount is polar-aligned. Once you perform polar alignment, the mount will inevitably shift, and your saved profile will no longer match the sky. **3D Tilt Correction** allows you to warp and re-align the entire profile using a single reference point. It is also useful if the webcam co-alignment was skipped or done poorly, introducing a systematic offset into the profile, or if you have physically repositioned your mount since the profile was originally built. In either case, 3D Tilt Correction serves as a quick preliminary correction before fine-tuning individual nodes.

Horizon Studio supports two synchronization methods: **Horizon Sync** and **Landmark Sync**.

#### Method A: Horizon Sync (Using a Horizon Pin)
Use this if you want to align your profile using a physical feature that is already part of your horizon line (e.g., a specific chimney or post).
1. Select the pin on the radar that represents your physical landmark.
2. Click **Slew** in the active details card to move your telescope to it.
3. Click **Prepare Sync**.
   * The panel will enter Sync Mode, showing a pulsing purple highlight ring around the target node on the radar.
   * Jogging controls remain active, but pin drops and verification slews are locked for safety.
4. Look at your camera feed. Manually jog the mount until the physical landmark is centered under the crosshairs.
5. Once you jog the mount past the safety threshold ($0.05^\circ$), the **Confirm Sync** button becomes active.
6. Click **Confirm Sync** and approve the warping pop-up. The entire horizon line shifts and warps to match your mount's new alignment.

---

#### Method B: Landmark Sync (Using Terrestrial Landmarks)
Use this if you want to align your profile using one or more highly striking reference landmarks (like an antenna tip or tower peak) that sit above or below your actual horizon. Horizon Studio allows you to create and manage multiple landmarks. To prevent N.I.N.A. from treating them as obstructions, their coordinates are safely embedded as hidden metadata comment headers directly inside the `.hrz` file.

1. **Adding Landmarks:**
   * Slew your telescope and center a striking landmark under your camera crosshairs.
   * On the **🔷 SYNC LANDMARKS** card, click **➕ Add**. A new landmark will be created at your mount's current coordinates.
   * Repeat this for any other landmarks visible from your site.
   * *(Optional)* Click **✏️ Rename** to give your landmarks custom names (e.g., `"Antenna"`, `"Tree"`).
   * Click **Save Horizon Profile** to store all landmarks directly in the file.

2. **Calibrating in a Future Session:**
   * Click **Load Horizon Profile** and load your `.hrz` file. All fuchsia diamonds (🔷) will immediately populate on both radars.
   * Select a landmark from the list or click its diamond on the radar. The active sync area will display the landmark's name and coordinates.
   * Click **Slew** to point your telescope at the landmark's saved position.
   * Click **Prepare Sync**.
   * Look at your camera feed and manually jog the mount to center the physical landmark under the crosshairs.
   * Once you jog the mount past the safety threshold ($0.05^\circ$), click **Confirm Sync**. The entire horizon profile (and all other landmarks in the collection) will shift and warp to match your new alignment!
   * Save your horizon profile to update the coordinates for all landmarks.

---

## 📝 License
This project is licensed under the MIT License.
