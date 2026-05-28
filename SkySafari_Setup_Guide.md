# Comprehensive Guide: Loading and Activating Your Custom Horizon in SkySafari (Android via PC)

This step-by-step guide walks you through transferring your generated `skysafari_horizon.png` file from your computer to your Android device using a USB cable, and then configuring SkySafari to display it correctly.

---

## Part 1: Transferring the File via USB

Because modern Android security restricts on-device file managers from accessing app-specific data folders, using a computer is the cleanest way to bypass this restriction.

### Step 1: Connect Your Phone to Your Computer

1. Plug your Android phone into your computer using a high-quality USB cable.

### Step 2: Enable File Transfer Mode on Your Phone

By default, Android devices usually connect in "Charging Only" mode. You must manually grant your computer permission to view your phone's folders:

1. Wake up your phone screen and swipe down from the top edge to open the **Notification Shade**.
2. Look for a notification that says something like **"USB charging this device"**, **"USB for charging"**, or **"Charging via USB"**. Tap it.
3. Under the *Use USB for* settings panel that pops up, select **File Transfer** (on some devices, this may be labeled **MTP** or **Transfer files**).

### Step 3: Locate the SkySafari Destination Folder on Your PC

1. On your computer, open **File Explorer** (Windows) or **Finder** (Mac).
*(Note for Mac Users: You will need a free utility like **Android File Transfer** or **OpenMTP** installed on your Mac to view Android files).*
2. Navigate into your phone's storage directory by following this exact path:
`This PC > [Your Phone Name] > Internal Storage > Android > data > com.simulationcurriculum.skysafari[version] > files > Horizon Panoramas`

> [!NOTE]
> **Note on `[version]`:** The exact name of the folder depends on which edition of SkySafari you have installed. For example:
> * SkySafari 7 Plus will be named: `com.simulationcurriculum.skysafari7plus`
> * SkySafari 7 Pro will be named: `com.simulationcurriculum.skysafari7pro`
> * SkySafari 8 Pro will be named: `com.simulationcurriculum.skysafari8pro`

### Step 4: Copy Your Horizon File

1. Locate your generated `skysafari_horizon.png` file on your computer.
2. **Drag and drop** (or copy and paste) the `.png` file directly into that **Horizon Panoramas** folder.
3. Once the transfer completes, you can safely unplug the USB cable from your phone.

---

## Part 2: Activating the Horizon in SkySafari

Now that the file is in the correct directory, you need to tell SkySafari to look for it, switch to the right coordinate grid, and turn on the panorama layout.

### Step 1: Restart SkySafari

If SkySafari was running in the background, it won't notice the new file immediately.

1. Close SkySafari and swipe it away from your phone’s recent apps menu to completely kill the process.
2. Launch SkySafari fresh so it forces a scan of its local folders.

### Step 2: Switch to Horizon Coordinates (Crucial)

If SkySafari is configured to display an Equatorial coordinate grid, panoramic custom horizons will glitch and turn the lower hemisphere completely black.

1. Look at the toolbar at the bottom of the main sky chart.
2. Tap on the **Coordinates** icon. (If you don't see it on your main bar, tap **Display** or navigate to **Settings > Coordinates**).
3. Ensure that **Horizon** (Alt-Az) is selected instead of Equatorial or Ecliptic.

### Step 3: Select Your Custom Panorama Landscape

1. Tap the **Settings** gear icon on the bottom toolbar.
2. Under the *Sky and Horizon* category, tap **Horizon & Sky**.
3. Scroll down past the toggle switches to the section labeled **Horizon Panorama**.
4. You will see a list of pre-installed landscapes (e.g., *Misty Mountain*, *Desert*). Your custom file name (e.g., `skysafari_horizon`) will now be listed among them. **Tap on your custom file name** to select it.
5. Exit out of the settings back to the main sky view.

### Step 4: Optional Tweak for Clearer Daytime Viewing

If you are setting this up during the day and notice a strange blue haze or masking anomalies where your custom horizon meets the sky:

1. Go back to **Settings > Horizon & Sky**.
2. Look under the **Horizon Display** checkboxes.
3. Temporarily uncheck **Show Daylight** and **Show Horizon Glow**.

Your translucent backyard obstructions will now be perfectly aligned with true North, South, East, and West, allowing you to accurately track exactly when deep-sky targets rise above your local tree and rooflines!
