# Horizon Studio (for N.I.N.A.)

**Horizon Studio** is the definitive tool for creating, editing, and calibrating local horizon profiles in Nighttime Imaging 'N' Astronomy (N.I.N.A.). 

Instead of guessing where trees, rooftops, or distant mountains intersect the night sky, Horizon Studio allows you to trace your actual, physical horizon during the daytime or dawn using a live video feed, generating a native `.hrzn` file that prevents your telescope from slewing into obstacles.

## ✨ Key Features

* **Live Visual Mapping:** Jog your mount along the horizon while viewing a live wide angled webcam feed (or your primary imaging camera) with an overlaid, rotatable targeting crosshair.
* **Intuitive Alt-Az Jogging for EQ Mounts:** The on-screen movement controls abstract away complex RA/Dec coordinates, allowing your equatorial mount to behave exactly like an Alt-Az mount. This is crucial for effortlessly tracking along a flat or jagged horizon line.
* **Profile Verification:** Physically slew your mount back to previously mapped points using simple Next/Prev controls to visually verify their accuracy against the real-world horizon.
* **Advanced Profile Editor:** Load existing `.hrzn` files to review, delete, or replace specific coordinate nodes without having to re-map the entire sky.
* **3D Tilt Synchronization:** Correct for tripod bumps, cone error, or polar alignment changes. By visually centering and syncing just *one* known point, Horizon Studio mathematically warps the entire 3D horizon profile to instantly correct the offset across the entire sky.
* **Smart Auto-Exposure:** When using your main astronomical camera during dawn or daytime, the plugin dynamically adjusts gain and exposure (similar to the Flat Wizard) to prevent the image from blowing out, keeping the horizon silhouette crisp.

## 🛠️ Requirements
* **N.I.N.A.** (Version 3.0 or higher)
* An equatorial mount connected via ASCOM/Alpaca
* A wide angled DirectShow Webcam **OR** a connected ASCOM/native primary camera

## 🚀 Basic Workflow

### Creating a New Profile
1. Connect your Mount and Camera in N.I.N.A.
2. Open the Horizon Studio dockable tab and select your video feed.
3. Co-align the on-screen targeting crosshair with your telescope's main optics.
4. Use the on-screen jog controls to move your mount to the peak of an obstacle (e.g., the top of a tree).
5. Click **Drop Pin** to record the Alt/Az coordinate.
6. Move to the next obstacle and repeat. The radar display will draw your horizon dome in real-time.
7. Click **Save** to export your custom `.hrzn` file.

### Editing, Fixing, or Syncing Profiles
You can continue refining your current mapping session, or click **Load Horizon Profile** to load a saved `.hrzn` file. You have access to the full suite of editing tools for both new and existing profiles:
* **Add & Delete Pins:** Delete inaccurate data points or drop new pins directly into the active sequence.
* **Verify Points:** Use Next/Prev controls to automatically slew the mount to saved points and visually verify their accuracy.
* **Sync Entire Profile:** If you bumped your tripod or adjusted your polar alignment, you can mathematically correct the entire profile without remapping:
  1. Slew to one of the known peaks in your profile.
  2. Click **Prepare Sync**, then manually jog the mount until the physical peak is perfectly centered in your camera feed.
  3. Click **Confirm Sync**. Horizon Studio calculates the offset and warps the rest of the 3D profile to match your new alignment.

## 📝 License
Copyright (c) Nir Zonshine. All rights reserved.
