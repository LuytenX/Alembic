# The Alembic

**The Alembic is a tool for viewing and editing objects contained within the Asheron's Call game DAT files.**
- Latest client DAT files supported.
- Currently intended for developers.
- High-precision world editing and object placement functionality.

---

## Disclaimer
**This project is for educational and non-commercial purposes only.**
- Asheron's Call was a registered trademark of Turbine, Inc. and WB Games Inc which has since expired.
- ACEmulator is not associated or affiliated in any way with Turbine, Inc. or WB Games Inc.

---

## Getting Started
DO NOT MOVE BUILDINGS!! They can move within their own landblock but I have not figured out the re-indexing of envcells and the packing (might) work but it is unstable. Buidlings can be moved within the same landblock.
Use the grid (G) to make sure the building is in one cell and not crossing into another cell. 

### The Alembic Key Bindings

#### General Controls (World Viewer)

| Key | Action |
|-----|--------|
| **W, A, S, D** | Move camera (Forward, Left, Backward, Right) |
| **Q, E** | Rotate selected object or set placement rotation |
| **M** | Toggle Minimap |
| **O** | Toggle Model Preview |
| **H** | Toggle HUD (Head-Up Display) |
| **L** | Show current location in status text |
| **C** | Clear current selection (when Ctrl is NOT held) |
| **F3** | Toggle Z-Slicing (Height-based rendering filter) |
| **F4** | Toggle Dungeons (Enabled/Disabled) |
| **Alt + +/-** | Adjust Current Z-Level (when Z-Slicing is enabled) |
| **Ctrl + C** | Show collision primitives for the selected object |
| **Ctrl + V** | Add visible cells (dungeons) |
| **G** | Show cell grid |

#### Render Toggles (World Viewer)

| Key | Toggle Element |
|-----|----------------|
| **1** | Terrain |
| **2** | Environment Cells (Dungeons) |
| **3** | Static Objects (Landscape objects) |
| **4** | Buildings |
| **5** | Scenery |
| **6** | Particles / Emitters |
| **7** | Instances |
| **8** | Encounters |
| **0** | Alpha / Transparency rendering |

#### Editor Controls (Terrain & Objects)

| Key / Action | Mode | Action |
|--------------|------|--------|
| **Left Click** | All | Select object / Room |
| **Ctrl + Left Click** | Edit/Paint | Apply Brush (Raise terrain / Paint texture) |
| **Shift + Ctrl + Left Click** | Edit | Lower terrain |
| **Shift + Click** | Object | Delete selected object |
| **[ / ]** | All | Decrease / Increase Brush Size |
| **+ / -** | All | Increase / Decrease Brush Strength |

#### Player Mode Controls

| Key | Action |
|-----|--------|
| **W, S, A, D** | Move / Turn character |
| **Up, Down, Left, Right** | Move / Turn character (Alternate) |
| **NumPad 8, 2, 4, 6** | Move / Turn character (Alternate) |
| **Z / C** | Strafe Left / Right |
| **Space** | Jump (Hold to charge power) |
| **Left Shift** | Hold to Walk (disables Run) |
| **I** | Toggle Ethereal mode (no-clip) |
| **Right Click + Drag** | Look around (rotates character/camera) |
| **Mouse Wheel** | Zoom camera in/out |

#### Specialized Viewers


**Map Viewer**
- **W, A, S, D / Arrows**: Pan Map
- **Escape**: Cancel drag operation

This project is a **modified version** of:
- [ACViewer](https://github.com/ACEmulator/ACViewer/)
