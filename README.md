# Horizon Studio (for N.I.N.A.)

**Horizon Studio** is a professional-grade tool for creating, editing, and calibrating local horizon profiles in Nighttime Imaging 'N' Astronomy (N.I.N.A.).

Instead of guessing where trees, rooftops, or distant mountains intersect the night sky, Horizon Studio allows you to trace your actual, physical horizon using a live video feed, generating a native `.hrzn` file that prevents your telescope from slewing into obstructions.

---

## ✨ Implemented Features

### 🎥 Live Eyepiece & HUD Overlay
* **Circular Eyepiece View:** Rotates and crops the live camera feed into a clean circular telescope eyepiece view, hiding tilted black borders during mount tracking.
* **AR HUD Overlay:** Superimposes a transparent Sky Dome Radar grid, card directions, the mapped horizon line, and mount position directly over the live camera feed.
* **Click-to-Slew:** Double-click near the horizon overlay line on the video feed to automatically slew your mount to that position.

### 📷 Main Camera Integration
* **Live Main Camera Feed:** Use your primary imaging camera as the video source instead of (or alongside) a webcam, with full N.I.N.A. equipment integration via `IImagingMediator`.
* **Auto-Exposure ADU Scaling:** Automatically adjusts exposure time to keep the image within an optimal ADU range, ensuring clear visibility in varying sky conditions.
* **Real-Time Star Detection HUD:** A translucent eyepiece overlay displays live telemetry — star count, median HFR, and ADU level — so you can verify optical alignment at a glance.
* **Camera Safety:** Implements hardware capture-block serialization and physical exposure aborts to prevent driver lockouts when stopping or switching feeds.

### 🔄 Webcam Co-Alignment & Rotation
* **Co-Alignment Assistant:** Center a landmark in your main imaging telescope, click the same target in the webcam feed, and the plugin locks the optical offset.
* **Equatorial Counter-Rotation:** Automatically rotates the camera feed in real-time based on your mount's position, keeping your physical horizon level. Handles meridian flips automatically.

### 🗺️ Interactive Sky Dome Radar
* **Shaded Obstruction Zone:** Displays a smooth, shaded representation of your blocked low-altitude sky.
* **Radar Slewing:** Click anywhere on the radar view to slew the mount to that sky position. Clicking near a saved node snaps selection to it.

### 🕹️ Verification & Traversal
* **Traversal Controls:** Step through your saved horizon nodes clockwise (`Slew CW ▶`) or counter-clockwise (`◀ Slew CCW`) to physically verify that your mapping is accurate.
* **Safety Safeguards:** Disables jogging and traversal controls during active slews, and prompts for confirmation before performing any large azimuth moves (> 45°).

### 🛠️ Horizon Pin Management
* **Dynamic Pin Dropping:** Drop nodes at your mount's position to build the horizon profile in real-time. Pins are automatically kept sorted by Azimuth.
* **Point Editing & Deletion:** Select any node to view its coordinates, slew to it, or delete it from the profile.

---

## 🛠️ Requirements
* **N.I.N.A.** (Version 3.0 or higher)
* An equatorial mount connected via ASCOM/Alpaca
* A wide-angled DirectShow USB Webcam **OR** your main imaging camera

---

## 🚀 Step-by-Step User Guide

### 1. Creating a New Horizon Profile
1. Connect your Mount and Camera in N.I.N.A., then open the **Horizon Studio** tab.
2. Align your camera crosshairs with your main telescope using the co-alignment helper.
3. Use the jogging controls to move the mount to the peak of a local obstacle (e.g. a tree top or roof line).
4. Click **Drop Pin** to save that horizon point.
5. Move the mount to the next obstacle along the horizon and click **Drop Pin** again. Repeat until you have mapped your sky.
6. Click **Save Horizon Profile** to save your `.hrzn` file.

---

### 2. Loading & Editing Profiles
* **Load Profile:** Click **Load Horizon Profile** to load a previously saved `.hrzn` file.
* **Add/Modify Pins:** Slew to any area and click **Drop Pin** to add new nodes.
* **Delete Pins:** Click a node on the radar to select it, then click **Delete Node** to remove it.
* **Verify Coords:** Click `◀ Slew CCW` or `Slew CW ▶` to step through your mapped nodes, verifying that the telescope points clear of physical obstructions.

---

### 3. Warp & Align Profiles (3D Tilt Correction)
The most common workflow is to build your horizon profile during the daytime — when you can clearly see trees, rooftops, and other obstructions — before your mount is polar-aligned. Once you perform polar alignment, the mount will inevitably shift, and your saved profile will no longer match the sky. **3D Tilt Correction** allows you to warp and re-align the entire profile using a single reference point. It is also useful if you have physically repositioned your mount since the profile was originally built; in that case, 3D Tilt Correction serves as a quick preliminary correction before fine-tuning individual nodes.

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

#### Method B: Landmark Sync (Using a Special Landmark)
Use this if you want to align using a highly striking reference landmark (like an antenna tip or tower peak) that sits above or below your actual horizon. To prevent N.I.N.A. from treating this landmark as an obstruction, Horizon Studio encodes the coordinates directly into the profile's filename instead of writing them into the `.hrzn` file.
1. Slew the telescope and center the striking landmark under your camera crosshairs.
2. On the **🔷 Special Sync Landmark** card, click **Set Mount as Landmark**. A fuchsia diamond 🔷 appears on the radar at those coordinates.
3. Click **Save Horizon Profile**. The plugin appends the landmark data to the filename: `Profile_sync_Az124.50_Alt15.20.hrzn`.
4. **Calibrating in a Future Session:**
   * Click **Load Horizon Profile** and load your `Profile_sync_Az124.50_Alt15.20.hrzn` file. The fuchsia diamond 🔷 immediately appears on the radar.
   * Click **Slew** on the landmark card to slew your telescope to the landmark.
   * Click **🔷 Select Landmark** on the card (or click directly on the fuchsia diamond 🔷 on the radar) to select it.
   * Click **Prepare Sync** on the details card.
   * Jog the mount to center the physical landmark under your crosshairs.
   * Click **Confirm Sync** once enabled to warp your horizon profile.
   * Save your horizon profile to update the filename with your new calibrated landmark coordinates.

---

## 📝 License
This project is licensed under the MIT License.
