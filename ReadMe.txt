================================================================================
                               RAVEN - Restoration And Visual ENhancement
================================================================================

                                     README

--------------------------------------------------------------------------------
## 1. Overview
--------------------------------------------------------------------------------

Welcome to the RAVEN (Restoration And Visual ENhancement)!

This program is designed for the rapid processing and enhancement of scanned images, typically from books or documents. It provides a powerful dual-pane interface to view a source JPG image (in color or grayscale) while performing manipulations on a corresponding bi-tonal TIF image.

The primary goal is to produce clean, high-quality bi-tonal (black and white) TIF images from the source JPGs. Key features include advanced thresholding, cropping, deskewing, line removal, and batch processing capabilities.

--------------------------------------------------------------------------------
## 2. How It Works: A Quick Start
--------------------------------------------------------------------------------

The program is designed for an efficient, keyboard-driven workflow.

### Launching the Program
You can start the program in one of three ways:
1.  **Standalone App:** Simply run `RAVEN.exe`. You will be prompted to enter or browse to a directory containing your image pairs (JPG/TIF).
2.  **Command Line (Directory):** Run `RAVEN.exe "C:\path\to\your\images"`. The program will automatically load all images from that directory.
3.  **Command Line (Specific File):** Run `RAVEN.exe "C:\path\to\your\images\0001.tif"`. The program will load the entire directory and jump directly to that image.

### The Basic Workflow
1.  **Open Images:** Launch the application and load a directory of images. You'll see the color JPG on the right and the bi-tonal TIF on the left.
2.  **Navigate:** Use `Page Down` / `N` / `Space` to go to the next image and `Page Up` / `U` to go to the previous one.
3.  **Convert (Threshold):** The main task is converting the image to black and white.
    * Press one of the function keys (`F2`, `F3`, `F4`, etc.) to apply a preset conversion setting to the entire image.
    * If you only want to convert a specific area, draw a box with the right mouse button and then press the desired function key.
4.  **Adjust & Refine:**
    * After applying a threshold, you can fine-tune the contrast by pressing `D` to darken or `S` to lighten.
    * Use the mouse wheel to rapidly adjust the highlighted setting in the Threshold Settings window.
5.  **Manipulate:** Perform other actions as needed using the shortcut keys:
    * `Y` to Deskew the image.
    * `C` to Crop.
    * `Right-Click` a vertical line to remove it.
6.  **Save & Continue:** Changes are saved to the TIF file automatically. Move to the next image to continue your work.

--------------------------------------------------------------------------------
## 3. Advanced Concepts
--------------------------------------------------------------------------------

### Concept 1: Threshold Settings & The INI File
The power of RAVEN comes from its configurable threshold presets. The settings for each function key (`F2` through `F6`) are stored in a text file named **settings.ini**, located in the same directory as `RAVEN.exe`.

This file allows you to define the default `Darkness`, `Lightness`, `Despeckle`, and other parameters for each hotkey. For specific jobs, a pre-configured `settings.ini` file can be copied to each user's station to ensure consistent processing. You can edit these settings on-the-fly using the separate **Threshold Settings** window.

### Concept 2: Primary Thresholding
This is the standard method you will use for most conversions.
* Pressing a function key (e.g., `F2`) applies its defined settings.
* If you need to make quick adjustments, press `D` to increase the contrast of the *last-used* setting by 1, or `S` to decrease it by 1.
* **Pro Tip:** For noisy images, using a high contrast setting combined with a high despeckle value is often very effective.

### Concept 3: Refine Threshold
For exceptionally poor-quality areas, the **Refine Threshold** provides a more aggressive conversion option. It is not a standard threshold but a special filter.
* **How it Works:** Think of it as first converting the image with extremely dark settings, and then using the original JPG image as a "despeckle filter" to bring back detail that would have been lost.
* **Warning:** This method will look strange on the screen and does result in some information loss. Use it only on areas that cannot be cleaned up with the primary threshold options.
* **Recommended Settings:** `Tolerance: 10`, `Despeckle: 7-12`, `Contrast Step-up: 5`.

### Concept 4: Bulk Enhance Mode (Convert Rest of Book)
This feature allows you to define conversion areas on one page and apply them to all subsequent pages in the book, which is useful for repetitive watermarks or headers/footers.
1.  Press `[` to cycle through the modes: **Every Image**, **Every Other Image**, or **Off**.
2.  On the current image, draw one or more selection boxes over the areas you want to convert.
3.  For each box, press the `F2`, `F3`, or `F4` key that corresponds to the conversion settings you want to apply to that specific area.
4.  Once all your areas are defined, press `]` to begin the process.
5.  The program will start converting the defined areas on all subsequent images in the background. You can continue to work on the current or other images while it runs.

### Concept 5: Handling Photostats (Negative Images)
The program includes special logic to correctly process photostats (negative images where the text is white and the background is black).
* In the `settings.ini` file, you can set `NegativeImage=Y` for any of the F-key profiles.
* When you use a hotkey configured for negative images, the program will automatically invert the colors during the thresholding process, resulting in a standard TIF with black text on a white background.
* The automatic border detection (`Q`) also uses different logic when working with photostats.

--------------------------------------------------------------------------------
## 4. Shortcut Commands
--------------------------------------------------------------------------------

| Key                          | Action                                                                   |
|------------------------------|--------------------------------------------------------------------------|
| **NAVIGATION** |                                                                          |
| Enter                        | Go / Open directory & load first image                                   |
| N or Pg Down or Space        | Next Image                                                               |
| U or Pg Up or Mouse Wheel Up | Prev Image                                                               |
| Home                         | Jump to start of book                                                    |
| End                          | Jump to end of book                                                      |
| J                            | Jump to specific page                                                    |
| A / Shift A                  | Move secondary highlight for rapid wheel change conversion forward/back    |
| Esc                          | Exit Application                                                         |
| ~                            | Toggle view JPG                                                          |
| Insert                       | Insert Image Note                                                        |
|                              |                                                                          |
| **BI-TONAL MANIPULATION** |                                                                          |
| Right Click                  | Remove Line                                                              |
| Right Click (cropbox)        | Moves cropbox                                                            |
| Shift + Right Click (cropbox)| Expands closest cropbox corner to mouse pointer                          |
| L                            | Enter/Exit Line Removal Mode (removes same line on following images)     |
| R                            | Invert Area                                                              |
| W                            | White Out Area                                                           |
| Shift + W                    | Add Whiteout Region for Batch Whiteout (see '5' in MISC)                 |
| E                            | Loads last batch whiteout region (specific to even/odd pages)            |
| Y                            | Deskew                                                                   |
| Q                            | Detects Borders, Set Cropbox Location                                    |
| Shift + Q                    | Detect Border, Set Global Cropbox Size, Set Even/Odd Cropbox Location    |
| X                            | Pulls up preset cropbox                                                  |
| Shift + X (After selecting)  | Sets Crop Box (Global Size & Even/Odd Location)                          |
| D                            | +1 Prev Threshold Contrast / Or pulls up prior crop/whiteout box        |
| S                            | -1 Prev Threshold Contrast / Or pulls up 2-images-ago crop/whiteout box |
| C                            | Crop (Selected area, visible cropbox, or whiteout outside box)           |
| Z                            | Undo                                                                     |
| Shift + Z                    | Redo                                                                     |
|                              |                                                                          |
| **SELECTION / ZOOM** |                                                                          |
| Draw box w Right Mouse       | Select Area to modify                                                    |
| Draw box w Left Mouse        | Zoom Area                                                                |
|                              |                                                                          |
| **THRESHOLD** |                                                                          |
| Mouse Scroll Wheel           | Fine Rotate (in rotate mode) / Rapidly change & apply highlighted value  |
| F1                           | Load this help file                                                      |
| F2 / F3 / F4 / F5 / F6       | Apply the corresponding Threshold Setting                                |
|                              |                                                                          |
| **EXTERNAL** | (Customizable .ps1 scripts)                                              |
| 2                            | Runs `2.ps1` to open Irfanview                                             |
| 3                            | Runs `3.ps1` to open Phreview                                            |
|                              |                                                                          |
| **MISC** |                                                                          |
| Shift + Insert               | Inserts/deletes blank page after the current page                        |
| [                            | Cycle Bulk Enhance Mode (Every Image, Every Other, Off)                  |
| ]                            | Start Bulk Enhance operation                                             |
| 1                            | Switch Mouse Wheel between Fine Rotate & Rapid Threshold change          |
| 4                            | Toggle "Whiteout Outside Box" mode for the Crop (C) key                  |
| 5                            | Toggle Batch Whiteout Mode on/off                                        |
| 6                            | Toggle Draw Line Mode on/off (for setting persistent margins)            |