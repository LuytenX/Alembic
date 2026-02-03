using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using MonoGame.Framework.WpfInterop.Input;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Server.Physics;
using ACE.Server.Physics.Common;
using ACE.Server.Physics.Util;

using ACViewer.Config;
using ACViewer.Enum;
using ACViewer.Extensions;
using ACViewer.Model;
using ACViewer.Render;
using ACViewer.View;

namespace ACViewer
{
    public class WorldViewer
    {
        public static MainWindow MainWindow => MainWindow.Instance;
        public static WorldViewer Instance { get; set; }
        public static Render.Render Render => GameView.Instance.Render;
        public ACViewer.Render.Buffer Buffer => Render.Buffer;
        public static Camera Camera => GameView.Camera;
        public PhysicsEngine Physics { get; set; }
        public WpfKeyboard Keyboard => GameView.Instance._keyboard;
        public KeyboardState PrevKeyboardState => GameView.Instance.PrevKeyboardState;
        public bool DungeonMode { get; set; }
        public Model.BoundingBox BoundingBox { get; set; }
        public uint SingleBlock { get; set; } = uint.MaxValue;
        public bool InitPlayerMode { get; set; }
        private static bool _freeRoam;
        public static bool FreeRoam 
        { 
            get => _freeRoam; 
            set 
            {
                if (Instance != null && Instance.PlayerMode && !value) return;
                _freeRoam = value;
            }
        }
        public static bool SpectatorMode { get; set; }
        public static bool WorldView { get; set; }
        private int lastLbx = -1;
        private int lastLby = -1;
        private int displayLbx = -1;
        private int displayLby = -1;

        public Dictionary<uint, Landblock> ModifiedLandblocks = new Dictionary<uint, Landblock>();
        public Dictionary<uint, ACE.DatLoader.FileTypes.EnvCell> ModifiedEnvCells = new Dictionary<uint, ACE.DatLoader.FileTypes.EnvCell>();
        public ACE.Server.Physics.Common.LandSurf CachedLandSurf;

        public Model.SaveMeter SaveMeter { get; set; } = new Model.SaveMeter();

        private readonly List<VertexPositionColor> _gridVertices = new List<VertexPositionColor>();
        private VertexPositionColor[] _gridVertexArray = new VertexPositionColor[1024];
        
        public WorldViewer()
        {
            if (Instance != null && Instance.PlayerMode) { Instance.ExitPlayerMode(); InitPlayerMode = true; }
            Instance = this;
        }

        public void JumpToLandblock(string hex)
        {
            if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out uint lbid))
            {
                if (lbid <= 0xFFFF) lbid = (lbid << 16) | 0xFFFF;
                if (!ACE.DatLoader.DatManager.CellDat.AllFiles.ContainsKey(lbid))
                {
                    MainWindow.AddStatusText($"Cannot jump to landblock: Landblock {lbid:X8} does not exist in DAT files.");
                    return;
                }
                MainWindow.AddStatusText($"Jumping to landblock {lbid:X8}...");
                LoadLandblock(lbid, WorldView ? 3u : 1u);
            }
        }

        public void LoadLandblock(uint landblockID, uint radius = 1, bool initCamera = true)
        {
            if (PlayerMode) { ExitPlayerMode(); InitPlayerMode = true; }
            Render.Buffer.ClearBuffer(); TextureCache.Init();
            LScape.unload_landblocks_all();
            var landblock = LScape.get_landblock(landblockID);
            bool disableDungeons = ConfigManager.Config.Toggles.DisableDungeons;
            DungeonMode = landblock.HasDungeon && !disableDungeons;
            var center_lbx = landblockID >> 24; var center_lby = landblockID >> 16 & 0xFF;
            
            lastLbx = (int)center_lbx; 
            lastLby = (int)center_lby;
            displayLbx = (int)center_lbx;
            displayLby = (int)center_lby;

            R_Landblock centerBlock = null;
            for (var lbx = (int)(center_lbx - radius); lbx <= center_lbx + radius; lbx++) {
                if (lbx < 0 || lbx > 254) continue;
                for (var lby = (int)(center_lby - radius); lby <= center_lby + radius; lby++) {
                    if (lby < 0 || lby > 254) continue;
                    var lbid = (uint)(lbx << 24 | lby << 16 | 0xFFFF);
                    var timer = Stopwatch.StartNew(); 
                    if (ModifiedLandblocks.TryGetValue(lbid, out var cached))
                    {
                        landblock = cached;
                        landblock.ConstructUVs(lbid);
                        if (!LScape.Landblocks.ContainsKey(lbid)) LScape.Landblocks.TryAdd(lbid, landblock);
                    }
                    else landblock = LScape.get_landblock(lbid);
                    timer.Stop();
                    var r_lb = new R_Landblock(landblock);
                    if (landblockID == lbid) centerBlock = r_lb;
                    MainWindow.AddStatusText($"Loaded {lbid:X8} in {timer.Elapsed.TotalMilliseconds}ms");
                }
            }
            Render.Buffer.BuildBuffers(); Render.InitEmitters();
            if (initCamera) {
                if (FileExplorer.Instance.TeleportMode) { var zBump = DungeonMode ? 1.775f : 2.775f; if (InitPlayerMode) zBump = 0.0f; Camera.InitTeleport(centerBlock, zBump); FileExplorer.Instance.TeleportMode = false; }
                else if (DungeonMode) { BoundingBox = new Model.BoundingBox(Render.Buffer.RB_EnvCell); Camera.InitDungeon(centerBlock, BoundingBox); }
                else Camera.InitLandblock(centerBlock);
            }
            SingleBlock = landblockID; FreeResources();
        }

        public async void LoadLandblocks(Microsoft.Xna.Framework.Vector2 startBlock, Microsoft.Xna.Framework.Vector2 endBlock)
        {
            if (PlayerMode) { ExitPlayerMode(); InitPlayerMode = true; }
            Render.Buffer.ClearBuffer(); TextureCache.Init();
            LScape.unload_landblocks_all(); DungeonMode = false;

            var minX = (uint)Math.Min(startBlock.X, endBlock.X);
            var maxX = (uint)Math.Max(startBlock.X, endBlock.X);
            var minY = (uint)Math.Min(startBlock.Y, endBlock.Y);
            var maxY = (uint)Math.Max(startBlock.Y, endBlock.Y);

            var centerX = (minX + maxX) / 2;
            var centerY = (minY + maxY) / 2;
            lastLbx = (int)centerX; lastLby = (int)centerY;
            var landblockID = centerX << 24 | centerY << 16 | 0xFFFF;

            MainWindow.AddStatusText($"Loading {(maxX - minX + 1) * (maxY - minY + 1)} landblocks");
            await Task.Delay(1); R_Landblock centerBlock = null;
            
            for (var lbx = minX; lbx <= maxX; lbx++) {
                if (lbx > 254) continue;
                for (var lby = minY; lby <= maxY; lby++) {
                    if (lby > 254) continue;
                    var lbid = lbx << 24 | lby << 16 | 0xFFFF;
                    var timer = Stopwatch.StartNew(); 
                    Landblock landblock;
                    if (ModifiedLandblocks.TryGetValue(lbid, out var cached))
                    {
                        landblock = cached;
                        landblock.ConstructUVs(lbid);
                        if (!LScape.Landblocks.ContainsKey(lbid)) LScape.Landblocks.TryAdd(lbid, landblock);
                    }
                    else landblock = LScape.get_landblock(lbid);
                    timer.Stop();
                    var r_lb = new R_Landblock(landblock); if (lbid == landblockID) centerBlock = r_lb;
                    MainWindow.AddStatusText($"Loaded {lbid:X8} in {timer.Elapsed.TotalMilliseconds}ms");
                }
            }
            Render.Buffer.BuildBuffers(); Render.InitEmitters(); 
            if (centerBlock != null) Camera.InitLandblock(centerBlock); 
            GameView.ViewMode = ViewMode.World; SingleBlock = uint.MaxValue; FreeResources();
        }

        public async void LoadAllLandblocks()
        {
            if (PlayerMode) { ExitPlayerMode(); InitPlayerMode = true; }
            Render.Buffer.ClearBuffer(); TextureCache.Init();
            LScape.unload_landblocks_all(); ModifiedLandblocks.Clear(); DungeonMode = false;
            MainWindow.AddStatusText("Scanning DAT for all available landblocks...");
            var allLBIDs = ACE.DatLoader.DatManager.CellDat.AllFiles.Keys.Where(id => (id & 0xFFFF) == 0xFFFF).OrderBy(id => id).ToList();
            MainWindow.AddStatusText($"Found {allLBIDs.Count} landblocks. Attempting to load...");
            int count = 0;
            foreach (var lbid in allLBIDs) {
                try {
                    var landblock = LScape.get_landblock(lbid);
                    if (landblock != null) { new R_Landblock(landblock); count++; }
                } catch { }
                if (count % 100 == 0) { MainWindow.AddStatusText($"Loaded {count} / {allLBIDs.Count}..."); await Task.Delay(1); }
            }
            Render.Buffer.BuildBuffers(); Render.InitEmitters(); MainWindow.AddStatusText($"Rendering {count} landblocks complete.");
        }

        public void FreeResources() => TextureCache.Init(false);
        public void ShowLocation() { var pos = Camera.GetPosition(); MainWindow.AddStatusText($"Location: {pos?.ToString() ?? "unknown"}"); }
        public bool PlayerMode { get; set; }

        private bool _isRotatingClockwise = false;
        private bool _isRotatingCounterClockwise = false;
        private const float ROTATION_SPEED_PER_FRAME = 2.0f;

        public void Update(Microsoft.Xna.Framework.GameTime time)
        {
            var keyboardState = Keyboard.GetState();
            if (keyboardState.IsKeyDown(Keys.G) && PrevKeyboardState.IsKeyUp(Keys.G))
            {
                ConfigManager.Config.Toggles.ShowCellGrid = !ConfigManager.Config.Toggles.ShowCellGrid;
                MainWindow.AddStatusText($"Cell Grid: {(ConfigManager.Config.Toggles.ShowCellGrid ? "ON" : "OFF")}");
            }
            if (InitPlayerMode) { EnterPlayerMode(); InitPlayerMode = false; }
            if (keyboardState.IsKeyDown(Keys.LeftControl) && keyboardState.IsKeyDown(Keys.S) && PrevKeyboardState.IsKeyUp(Keys.S)) _ = SaveLandblocksAsync();
            if (keyboardState.IsKeyDown(Keys.M) && !PrevKeyboardState.IsKeyDown(Keys.M)) MainMenu.ToggleMinimap();
            if (keyboardState.IsKeyDown(Keys.O) && !PrevKeyboardState.IsKeyDown(Keys.O)) GameView.TogglePreview();
            if (keyboardState.IsKeyDown(Keys.H) && !PrevKeyboardState.IsKeyDown(Keys.H)) MainMenu.ToggleHUD();
            if (keyboardState.IsKeyDown(Keys.L) && !PrevKeyboardState.IsKeyDown(Keys.L)) ShowLocation();
            if (!keyboardState.IsKeyDown(Keys.LeftControl) && keyboardState.IsKeyDown(Keys.C) && !PrevKeyboardState.IsKeyDown(Keys.C)) Picker.ClearSelection();
            if (keyboardState.IsKeyDown(Keys.D1) && !PrevKeyboardState.IsKeyDown(Keys.D1)) ACViewer.Render.Buffer.drawTerrain = !ACViewer.Render.Buffer.drawTerrain;
            if (keyboardState.IsKeyDown(Keys.D2) && !PrevKeyboardState.IsKeyDown(Keys.D2)) ACViewer.Render.Buffer.drawEnvCells = !ACViewer.Render.Buffer.drawEnvCells;
            if (keyboardState.IsKeyDown(Keys.D3) && !PrevKeyboardState.IsKeyDown(Keys.D3)) ACViewer.Render.Buffer.drawStaticObjs = !ACViewer.Render.Buffer.drawStaticObjs;
            if (keyboardState.IsKeyDown(Keys.D4) && !PrevKeyboardState.IsKeyDown(Keys.D4)) ACViewer.Render.Buffer.drawBuildings = !ACViewer.Render.Buffer.drawBuildings;
            if (keyboardState.IsKeyDown(Keys.D5) && !PrevKeyboardState.IsKeyDown(Keys.D5)) ACViewer.Render.Buffer.drawScenery = !ACViewer.Render.Buffer.drawScenery;
            if (keyboardState.IsKeyDown(Keys.D6) && !PrevKeyboardState.IsKeyDown(Keys.D6)) MainMenu.ToggleParticles();
            if (keyboardState.IsKeyDown(Keys.D7) && !PrevKeyboardState.IsKeyDown(Keys.D7)) ACViewer.Render.Buffer.drawInstances = !ACViewer.Render.Buffer.drawInstances;
            if (keyboardState.IsKeyDown(Keys.D8) && !PrevKeyboardState.IsKeyDown(Keys.D8)) ACViewer.Render.Buffer.drawEncounters = !ACViewer.Render.Buffer.drawEncounters;
            if (keyboardState.IsKeyDown(Keys.D0) && !PrevKeyboardState.IsKeyDown(Keys.D0)) ACViewer.Render.Buffer.drawAlpha = !ACViewer.Render.Buffer.drawAlpha;
            if (keyboardState.IsKeyDown(Keys.LeftControl) && keyboardState.IsKeyDown(Keys.V) && !PrevKeyboardState.IsKeyDown(Keys.V)) Picker.AddVisibleCells();
            if (keyboardState.IsKeyDown(Keys.LeftControl) && keyboardState.IsKeyDown(Keys.C) && !PrevKeyboardState.IsKeyDown(Keys.C)) Picker.ShowCollision();
            if (keyboardState.IsKeyDown(Keys.OemCloseBrackets) && !PrevKeyboardState.IsKeyDown(Keys.OemCloseBrackets)) { Picker.BrushSize = Math.Min(Picker.BrushSize + 2.0f, 100.0f); MainWindow.BrushSize = Picker.BrushSize; }
            if (keyboardState.IsKeyDown(Keys.OemOpenBrackets) && !PrevKeyboardState.IsKeyDown(Keys.OemOpenBrackets)) { Picker.BrushSize = Math.Max(Picker.BrushSize - 2.0f, 1.0f); MainWindow.BrushSize = Picker.BrushSize; }
            if (keyboardState.IsKeyDown(Keys.OemPlus) && !PrevKeyboardState.IsKeyDown(Keys.OemPlus) && !keyboardState.IsKeyDown(Keys.LeftAlt)) { Picker.BrushStrength = Math.Min(Picker.BrushStrength + 1, 20); MainWindow.BrushStrength = Picker.BrushStrength; }
            if (keyboardState.IsKeyDown(Keys.OemMinus) && !PrevKeyboardState.IsKeyDown(Keys.OemMinus) && !keyboardState.IsKeyDown(Keys.LeftAlt)) { Picker.BrushStrength = Math.Max(Picker.BrushStrength - 1, 1); MainWindow.BrushStrength = Picker.BrushStrength; }

            if (keyboardState.IsKeyDown(Keys.Q) && !PrevKeyboardState.IsKeyDown(Keys.Q)) { _isRotatingClockwise = true; _isRotatingCounterClockwise = false; }
            else if (!keyboardState.IsKeyDown(Keys.Q) && PrevKeyboardState.IsKeyDown(Keys.Q)) _isRotatingClockwise = false;
            if (keyboardState.IsKeyDown(Keys.E) && !PrevKeyboardState.IsKeyDown(Keys.E)) { _isRotatingCounterClockwise = true; _isRotatingClockwise = false; }
            else if (!keyboardState.IsKeyDown(Keys.E) && PrevKeyboardState.IsKeyDown(Keys.E)) _isRotatingCounterClockwise = false;

            if (_isRotatingClockwise || _isRotatingCounterClockwise) RotateObjectByDegrees(_isRotatingClockwise ? ROTATION_SPEED_PER_FRAME : -ROTATION_SPEED_PER_FRAME);

            if (keyboardState.IsKeyDown(Keys.F4) && !PrevKeyboardState.IsKeyDown(Keys.F4)) 
            { 
                ConfigManager.Config.Toggles.DisableDungeons = !ConfigManager.Config.Toggles.DisableDungeons; 
                MainWindow.AddStatusText($"Dungeons: {(ConfigManager.Config.Toggles.DisableDungeons ? "DISABLED" : "ENABLED")}"); 
                if (SingleBlock != uint.MaxValue) LoadLandblock(SingleBlock, 1, false); 
                else { var pos = Camera.Position; UpdateFreeRoamArea((uint)(pos.X / 192.0f), (uint)(pos.Y / 192.0f)); }
            }
            if (!DungeonMode) UpdateLocationReference();
            if (FreeRoam) CheckFreeRoam();
            if (GameView.ViewMode == ViewMode.World && PlayerMode && Player != null) Player.Update(time); else if (Camera != null) Camera.Update(time);
            Render.UpdateEmitters(); PerfTimer.Update();
        }

        private void RotateObjectByDegrees(float degrees)
        {
            if (Picker.SelectedStab != null)
            {
                bool rotated = false;
                var zRot = MathHelper.ToRadians(degrees);
                var additionalRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, zRot);
                if (Picker.SelectedStab is Stab stab) {
                    if (Picker.SelectedParentLandblock.IsDungeon || Picker.SelectedParentLandblock.StaticObjects.Any(s => s.SourceStab == stab)) {
                        var newOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(stab.Frame.Orientation, additionalRotation));
                        stab.Frame.Init(stab.Frame.Origin, newOrientation); rotated = true;
                    }
                } else if (Picker.SelectedStab is ACE.Server.Physics.Common.EnvCell envCell) {
                    if (Picker.SelectedParentLandblock.IsDungeon) {
                        var newOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(envCell.Pos.Frame.Orientation, additionalRotation));
                        envCell.Pos.Frame.set_rotate(newOrientation); rotated = true;
                    }
                } else if (Picker.SelectedStab is ACE.DatLoader.Entity.BuildInfo buildInfo) {
                    RotationHelper.RotateBuildingWithEnvironmentCells(buildInfo, Picker.SelectedParentLandblock, degrees); rotated = true;
                }
                if (rotated) {
                    Picker.SelectedParentLandblock.IsInfoModified = true;
                    bool wasInPlayerMode = PlayerMode; R_PhysicsObj playerObj = (wasInPlayerMode && Player?.PhysicsObj != null) ? new R_PhysicsObj(Player.PhysicsObj) : null;
                    Picker.SelectedParentLandblock.destroy_static_objects(); Picker.SelectedParentLandblock.destroy_buildings();
                    foreach (var cell in Picker.SelectedParentLandblock.get_envcells()) cell.destroy_static_objects();
                    Picker.SelectedParentLandblock.init_static_objs(); Picker.SelectedParentLandblock.init_buildings();
                    foreach (var cell in Picker.SelectedParentLandblock.get_envcells()) cell.init_static_objects();
                    Render.Buffer.ClearBuffer(); foreach(var lb in LScape.Landblocks.Values) Render.Buffer.AddOutdoor(new R_Landblock(lb as Landblock));
                    if (wasInPlayerMode && playerObj != null) Render.Buffer.AddPlayer(playerObj);
                    Render.Buffer.BuildBuffers();
                }
            } else { Picker.CurrentObjectYaw = (Picker.CurrentObjectYaw + degrees) % 360.0f; if (Picker.CurrentObjectYaw < 0) Picker.CurrentObjectYaw += 360.0f; }
        }

        private void UpdateLocationReference()
        {
            var pos = Camera.Position;
            var lbx = (int)(Math.Floor(pos.X / 192.0f)) & 0xFF;
            var lby = (int)(Math.Floor(pos.Y / 192.0f)) & 0xFF;
            lbx = Math.Max(0, Math.Min(254, lbx)); lby = Math.Max(0, Math.Min(254, lby));
            if (lbx == displayLbx && lby == displayLby) return;
            if (MainWindow.Instance != null && MainWindow.Instance.JumpLbidText != null && !MainWindow.Instance.JumpLbidText.IsFocused) {
                displayLbx = lbx; displayLby = lby; MainWindow.Instance.JumpLbidText.Text = $"{lbx:X2}{lby:X2}";
            }
        }

        private bool _suppressFreeRoamUpdate;
        private void CheckFreeRoam()
        {
            if (_suppressFreeRoamUpdate) return;
            var pos = Camera.Position;
            var lbx = (int)(pos.X / 192.0f);
            var lby = (int)(pos.Y / 192.0f);
            if (lbx != lastLbx || lby != lastLby || LScape.LandblocksCount == 0) {
                if (lbx < 0 || lbx > 254 || lby < 0 || lby > 254) return;
                lastLbx = lbx; lastLby = lby; _suppressFreeRoamUpdate = true;
                MainWindow.AddStatusText($"Free Roam: Entering landblock {lbx:X2}{lby:X2}");
                UpdateFreeRoamArea((uint)lbx, (uint)lby);
                _suppressFreeRoamUpdate = false;
            }
        }

        private void UpdateFreeRoamArea(uint center_lbx, uint center_lby)
        {
            bool wasInPlayerMode = PlayerMode; R_PhysicsObj playerObj = (wasInPlayerMode && Player != null) ? new R_PhysicsObj(Player.PhysicsObj) : null;
            foreach (var lb_obj in LScape.Landblocks.Values) if (lb_obj is Landblock lb && (lb.IsModified || lb.IsInfoModified)) if (!ModifiedLandblocks.ContainsKey(lb.ID)) ModifiedLandblocks.Add(lb.ID, lb);
            uint radius = WorldView ? 3u : 1u;
            Render.Buffer.ClearBuffer(); LScape.unload_landblocks_all();
            var regionDesc = ACE.DatLoader.DatManager.PortalDat.RegionDesc;
            if (regionDesc?.TerrainInfo != null) ACE.Server.Physics.Common.LandSurf.Instance = new ACE.Server.Physics.Common.LandSurf(regionDesc.TerrainInfo.LandSurfaces);
            for (var lbx = (int)(center_lbx - radius); lbx <= center_lbx + radius; lbx++) {
                if (lbx < 0 || lbx > 254) continue;
                for (var lby = (int)(center_lby - radius); lby <= center_lby + radius; lby++) {
                    if (lby < 0 || lby > 254) continue;
                    var lbid = (uint)(lbx << 24 | lby << 16 | 0xFFFF);
                    Landblock landblock;
                    if (ModifiedLandblocks.TryGetValue(lbid, out var cached)) { landblock = cached; landblock.ConstructUVs(lbid); if (!LScape.Landblocks.ContainsKey(lbid)) LScape.Landblocks.TryAdd(lbid, landblock); }
                    else landblock = LScape.get_landblock(lbid);
                    new R_Landblock(landblock);
                }
            }
            Render.Buffer.BuildBuffers(); Render.InitEmitters();
            if (wasInPlayerMode && playerObj != null) { Render.Buffer.AddPlayer(playerObj); Render.Buffer.BuildTextureAtlases(Render.Buffer.AnimatedTextureAtlasChains); Render.Buffer.BuildBuffer(Render.Buffer.RB_Animated); }
        }

        public async Task SaveLandblocksAsync()
        {
            foreach (var lb_obj in LScape.Landblocks.Values)
                if (lb_obj is Landblock lb && (lb.IsModified || lb.IsInfoModified))
                    if (!ModifiedLandblocks.ContainsKey(lb.ID)) ModifiedLandblocks.Add(lb.ID, lb);

            if (ModifiedLandblocks.Count == 0 && ModifiedEnvCells.Count == 0) { MainWindow.Dispatcher.Invoke(() => MainWindow.AddStatusText("No changes to save.")); return; }
            MainWindow.Dispatcher.Invoke(() => MainWindow.AddStatusText($"Saving changes..."));

            SaveMeter.Start();
            int totalSteps = ModifiedLandblocks.Count + ModifiedEnvCells.Count; int currentStep = 0;
            var landblocksToSave = ModifiedLandblocks.Values.ToList(); var envCellsToSave = ModifiedEnvCells.ToList();
            var cellDat = ACE.DatLoader.DatManager.CellDat; string datPath = cellDat.FilePath;

            if (!File.Exists(datPath + ".bak")) File.Copy(datPath, datPath + ".bak");

            await Task.Run(() =>
            {
                try {
                    uint blockSize = cellDat.Header.BlockSize;
                    using (var fileStream = new FileStream(datPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
                        bool globalIterationUpdated = false; uint iterID = 0xFFFF0001;
                        if (!cellDat.AllFiles.TryGetValue(iterID, out var iterEntry)) return;
                        byte[] currentIterData = ReadLogical(fileStream, iterEntry.FileOffset, iterEntry.FileSize, blockSize);
                        int currentTotal = (currentIterData.Length >= 4) ? BitConverter.ToInt32(currentIterData, 0) : 982;
                        uint nextGlobalIteration = (uint)currentTotal + 1;

                        foreach (var lb in landblocksToSave) {
                            currentStep++; SaveMeter.Percent = (float)currentStep / totalSteps;
                            if (lb.IsModified) {
                                if (cellDat.AllFiles.TryGetValue(lb.ID, out var entry)) {
                                    byte[] packedData = PackLandblock(lb._landblock);
                                    SafeWriteLogical(fileStream, entry.FileOffset, packedData, blockSize, false);
                                    SetIterationInDirectory(fileStream, lb.ID, entry.FileOffset, (uint)packedData.Length, nextGlobalIteration, blockSize);
                                    ReflectionCache.DatFileIterationField?.SetValue(entry, nextGlobalIteration);
                                    if (packedData.Length < entry.FileSize) {
                                        UpdateDirectoryEntryLogical(fileStream, lb.ID, entry.FileOffset, entry.FileSize, entry.FileOffset, (uint)packedData.Length, blockSize);
                                        ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)packedData.Length);
                                    }
                                    lb.IsModified = false; cellDat.FileCache.TryRemove(lb.ID, out _); globalIterationUpdated = true;
                                }
                            }
                            if (lb.IsInfoModified && lb.Info != null) {
                                uint infoID = lb.ID - 1; bool newRecord = !cellDat.AllFiles.TryGetValue(infoID, out var entry);
                                byte[] packedData = PackLandblockInfo(lb.Info);
                                bool appendRequired = newRecord || (entry != null && packedData.Length > entry.FileSize);
                                uint currentOff = entry?.FileOffset ?? 0; uint currentSize = entry?.FileSize ?? 0;

                                if (!newRecord && !appendRequired && entry != null) {
                                    try {
                                        SafeWriteLogical(fileStream, entry.FileOffset, packedData, blockSize, true);
                                        UpdateDirectoryEntryLogical(fileStream, infoID, entry.FileOffset, entry.FileSize, entry.FileOffset, (uint)packedData.Length, blockSize);
                                        ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)packedData.Length);
                                    } catch { appendRequired = true; }
                                }
                                if (appendRequired) {
                                    long newOffset = (fileStream.Length + (blockSize - 1)) / blockSize * blockSize;
                                    fileStream.Seek(newOffset, SeekOrigin.Begin); var writer = new BinaryWriter(fileStream);
                                    int remaining = packedData.Length; int dataOffset = 0;
                                    while (remaining > 0) {
                                        long currentBlockStart = fileStream.Position; int dataInBlock = (int)blockSize - 4; int toWrite = Math.Min(remaining, dataInBlock);
                                        if (remaining <= dataInBlock) writer.Write((uint)0); else writer.Write((uint)(currentBlockStart + blockSize));
                                        writer.Write(packedData, dataOffset, toWrite); dataOffset += toWrite; remaining -= toWrite;
                                        int padding = dataInBlock - toWrite; if (padding > 0) writer.Write(new byte[padding]);
                                    }
                                    if (newRecord) {
                                        if (ForceCreateDirectoryEntry(fileStream, infoID, (uint)newOffset, (uint)packedData.Length, blockSize, nextGlobalIteration)) {
                                            var newEntry = new ACE.DatLoader.DatFile(infoID, (uint)newOffset, (uint)packedData.Length, 0, nextGlobalIteration);
                                            cellDat.AllFiles.TryAdd(infoID, newEntry); entry = newEntry; globalIterationUpdated = true;
                                        }
                                    } else {
                                        if (UpdateDirectoryEntryLogical(fileStream, infoID, currentOff, currentSize, (uint)newOffset, (uint)packedData.Length, blockSize)) {
                                            ReflectionCache.DatFileFileOffsetField?.SetValue(entry, (uint)newOffset);
                                            ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)packedData.Length);
                                        }
                                    }
                                    currentOff = (uint)newOffset;
                                }
                                SetIterationInDirectory(fileStream, infoID, currentOff, (uint)packedData.Length, nextGlobalIteration, blockSize);
                                ReflectionCache.DatFileIterationField?.SetValue(entry, nextGlobalIteration);
                                lb.IsInfoModified = false; cellDat.FileCache.TryRemove(infoID, out _); globalIterationUpdated = true;
                            }
                        }
                        foreach (var envCellEntry in envCellsToSave) {
                            currentStep++; SaveMeter.Percent = (float)currentStep / totalSteps;
                            uint envCellId = envCellEntry.Key; var envCell = envCellEntry.Value;
                            byte[] envCellData = SerializeEnvCell(envCell);
                            bool newRecord = !cellDat.AllFiles.TryGetValue(envCellId, out var entry);
                            bool appendRequired = newRecord || (entry != null && envCellData.Length > entry.FileSize);
                            uint currentOff = entry?.FileOffset ?? 0; uint currentSize = entry?.FileSize ?? 0;

                            if (!newRecord && !appendRequired && entry != null) {
                                try {
                                    SafeWriteLogical(fileStream, entry.FileOffset, envCellData, blockSize, true);
                                    UpdateDirectoryEntryLogical(fileStream, envCellId, entry.FileOffset, entry.FileSize, entry.FileOffset, (uint)envCellData.Length, blockSize);
                                    ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)envCellData.Length);
                                } catch { appendRequired = true; }
                            }
                            if (appendRequired) {
                                long newOffset = (fileStream.Length + (blockSize - 1)) / blockSize * blockSize;
                                fileStream.Seek(newOffset, SeekOrigin.Begin); var writer = new BinaryWriter(fileStream);
                                int remaining = envCellData.Length; int dataOffset = 0;
                                while (remaining > 0) {
                                    long currentBlockStart = fileStream.Position; int dataInBlock = (int)blockSize - 4; int toWrite = Math.Min(remaining, dataInBlock);
                                    if (remaining <= dataInBlock) writer.Write((uint)0); else writer.Write((uint)(currentBlockStart + blockSize));
                                    writer.Write(envCellData, dataOffset, toWrite); dataOffset += toWrite; remaining -= toWrite;
                                    int padding = dataInBlock - toWrite; if (padding > 0) writer.Write(new byte[padding]);
                                }
                                if (newRecord) {
                                    if (ForceCreateDirectoryEntry(fileStream, envCellId, (uint)newOffset, (uint)envCellData.Length, blockSize, nextGlobalIteration)) {
                                        var newEntry = new ACE.DatLoader.DatFile(envCellId, (uint)newOffset, (uint)envCellData.Length, 0, nextGlobalIteration);
                                        cellDat.AllFiles.TryAdd(envCellId, newEntry); entry = newEntry; globalIterationUpdated = true;
                                    }
                                } else if (entry != null) {
                                    if (UpdateDirectoryEntryLogical(fileStream, envCellId, currentOff, currentSize, (uint)newOffset, (uint)envCellData.Length, blockSize)) {
                                        ReflectionCache.DatFileFileOffsetField?.SetValue(entry, (uint)newOffset);
                                        ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)envCellData.Length);
                                    }
                                }
                                currentOff = (uint)newOffset;
                            }
                            if (entry != null) {
                                SetIterationInDirectory(fileStream, envCellId, currentOff, (uint)envCellData.Length, nextGlobalIteration, blockSize);
                                ReflectionCache.DatFileIterationField?.SetValue(entry, nextGlobalIteration);
                                cellDat.FileCache.TryRemove(envCellId, out _); globalIterationUpdated = true;
                            }
                        }
                        if (globalIterationUpdated) UpdateGlobalIteration(fileStream, blockSize, nextGlobalIteration);
                    }
                    MainWindow.Dispatcher.Invoke(() => { ModifiedLandblocks.Clear(); ModifiedEnvCells.Clear(); MainWindow.AddStatusText("Saved successfully."); SaveMeter.Stop(); });
                } catch (Exception ex) { MainWindow.Dispatcher.Invoke(() => { MainWindow.AddStatusText($"Error saving: {ex.Message}"); SaveMeter.Stop(); }); }
            });
        }

        private bool ForceCreateDirectoryEntry(FileStream stream, uint id, uint off, uint size, uint blockSize, uint iteration = 0)
        {
            uint rootOffset = ACE.DatLoader.DatManager.CellDat.Header.BTree;
            uint newNodeFirstId; return RotationHelper.InjectRecord(stream, rootOffset, id, off, size, blockSize, out newNodeFirstId);
        }

        private byte[] SerializeEnvCell(ACE.DatLoader.FileTypes.EnvCell envCell)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms)) {
                writer.Write(envCell.Id); writer.Write((uint)envCell.Flags); writer.Write(envCell.Id);
                writer.Write((byte)envCell.Surfaces.Count); writer.Write((byte)envCell.CellPortals.Count); writer.Write((ushort)envCell.VisibleCells.Count);
                foreach (var surface in envCell.Surfaces) writer.Write((ushort)(surface & 0xFFFF));
                writer.Write((ushort)(envCell.EnvironmentId & 0xFFFF)); writer.Write((ushort)envCell.CellStructure);
                writer.Write(envCell.Position.Origin.X); writer.Write(envCell.Position.Origin.Y); writer.Write(envCell.Position.Origin.Z); writer.Write(envCell.Position.Orientation.W); writer.Write(envCell.Position.Orientation.X); writer.Write(envCell.Position.Orientation.Y); writer.Write(envCell.Position.Orientation.Z);
                foreach (var portal in envCell.CellPortals) { writer.Write((ushort)portal.Flags); writer.Write(portal.PolygonId); writer.Write(portal.OtherCellId); writer.Write(portal.OtherPortalId); }
                foreach (var visibleCell in envCell.VisibleCells) writer.Write(visibleCell);
                if (envCell.Flags.HasFlag(ACE.Entity.Enum.EnvCellFlags.HasStaticObjs)) {
                    writer.Write((uint)envCell.StaticObjects.Count);
                    foreach (var so in envCell.StaticObjects) {
                        writer.Write(so.Id); writer.Write(so.Frame.Origin.X); writer.Write(so.Frame.Origin.Y); writer.Write(so.Frame.Origin.Z);
                        writer.Write(so.Frame.Orientation.W); writer.Write(so.Frame.Orientation.X); writer.Write(so.Frame.Orientation.Y); writer.Write(so.Frame.Orientation.Z);
                    }
                }
                if (envCell.Flags.HasFlag(ACE.Entity.Enum.EnvCellFlags.HasRestrictionObj)) writer.Write(envCell.RestrictionObj);
                return ms.ToArray();
            }
        }

        private void SafeWriteLogical(FileStream stream, uint startBlockOffset, byte[] data, uint blockSize, bool killChain = false)
        {
            uint currentBlock = startBlockOffset; int dataIndex = 0;
            while (dataIndex < data.Length) {
                stream.Seek(currentBlock, SeekOrigin.Begin);
                byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4);
                uint nextBlock = BitConverter.ToUInt32(ptrBytes, 0);
                uint physicalOffset = currentBlock + 4; int dataInBlock = (int)blockSize - 4;
                int toWrite = Math.Min(data.Length - dataIndex, dataInBlock);
                stream.Seek(physicalOffset, SeekOrigin.Begin); stream.Write(data, dataIndex, toWrite);
                if (killChain && dataIndex + toWrite >= data.Length) {
                    stream.Seek(currentBlock, SeekOrigin.Begin); stream.Write(BitConverter.GetBytes(0U), 0, 4);
                }
                dataIndex += toWrite;
                if (dataIndex < data.Length) {
                    currentBlock = nextBlock; if (currentBlock == 0) throw new InvalidOperationException("Chain too short.");
                }
            }
        }

        private bool UpdateDirectoryEntryLogical(FileStream stream, uint id, uint oldOff, uint oldSize, uint newOff, uint newSize, uint blockSize)
        {
            uint rootOffset = ACE.DatLoader.DatManager.CellDat.Header.BTree;
            return ScanDirectoryNode(stream, rootOffset, id, oldOff, oldSize, newOff, newSize, blockSize);
        }

        private bool ScanDirectoryNode(FileStream stream, uint nodeOffset, uint id, uint oldOff, uint oldSize, uint newOff, uint newSize, uint blockSize)
        {
            if (nodeOffset == 0) return false;
            byte[] nodeData = ReadLogical(stream, nodeOffset, Math.Max(2048, blockSize), blockSize);
            using (var ms = new MemoryStream(nodeData))
            using (var reader = new BinaryReader(ms)) {
                uint[] branches = new uint[62]; for (int i = 0; i < 62; i++) branches[i] = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();
                for (int i = 0; i < (int)entryCount; i++) {
                    uint entryLogicalOffset = (uint)ms.Position; reader.ReadUInt32();
                    uint entryId = reader.ReadUInt32(); uint entryOff = reader.ReadUInt32(); uint entrySize = reader.ReadUInt32();
                    reader.ReadUInt32(); reader.ReadUInt32();
                    if (entryId == id) {
                        SafeWriteLogicalAt(stream, nodeOffset, entryLogicalOffset + 8, BitConverter.GetBytes(newOff), blockSize);
                        SafeWriteLogicalAt(stream, nodeOffset, entryLogicalOffset + 12, BitConverter.GetBytes(newSize), blockSize);
                        return true;
                    }
                }
                if (branches[0] != 0) {
                    for (int i = 0; i <= (int)entryCount; i++) if (ScanDirectoryNode(stream, branches[i], id, oldOff, oldSize, newOff, newSize, blockSize)) return true;
                }
            }
            return false;
        }

        private void SafeWriteLogicalAt(FileStream stream, uint startBlockOffset, uint logicalOffset, byte[] data, uint blockSize)
        {
            uint currentBlock = startBlockOffset; uint bytesToSkip = logicalOffset; uint dataInBlock = blockSize - 4;
            while (bytesToSkip >= dataInBlock) {
                stream.Seek(currentBlock, SeekOrigin.Begin); byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4);
                currentBlock = BitConverter.ToUInt32(ptrBytes, 0); if (currentBlock == 0) throw new InvalidOperationException("Chain too short.");
                bytesToSkip -= dataInBlock;
            }
            int dataIndex = 0;
            while (dataIndex < data.Length) {
                stream.Seek(currentBlock, SeekOrigin.Begin);
                byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4); uint nextBlock = BitConverter.ToUInt32(ptrBytes, 0);
                uint physicalOffset = currentBlock + 4 + bytesToSkip; int spaceInBlock = (int)(dataInBlock - bytesToSkip);
                int toWrite = Math.Min(data.Length - dataIndex, spaceInBlock);
                stream.Seek(physicalOffset, SeekOrigin.Begin); stream.Write(data, dataIndex, toWrite);
                dataIndex += toWrite; bytesToSkip = 0;
                if (dataIndex < data.Length) {
                    currentBlock = nextBlock; if (currentBlock == 0) throw new InvalidOperationException("Chain too short.");
                }
            }
        }

        private void SetIterationInDirectory(FileStream stream, uint id, uint off, uint size, uint nextIter, uint blockSize)
        {
            uint rootOffset = ACE.DatLoader.DatManager.CellDat.Header.BTree;
            ScanDirectoryAndSet(stream, rootOffset, id, off, size, nextIter, blockSize);
        }

        private bool ScanDirectoryAndSet(FileStream stream, uint nodeOffset, uint id, uint off, uint size, uint nextIter, uint blockSize)
        {
            if (nodeOffset == 0) return false;
            byte[] nodeData = ReadLogical(stream, nodeOffset, Math.Max(2048, blockSize), blockSize);
            using (var ms = new MemoryStream(nodeData))
            using (var reader = new BinaryReader(ms)) {
                uint[] branches = new uint[62]; for (int i = 0; i < 62; i++) branches[i] = reader.ReadUInt32();
                uint entryCount = reader.ReadUInt32();
                for (int i = 0; i < (int)entryCount; i++) {
                    uint entryLogicalOffset = (uint)ms.Position; reader.ReadUInt32();
                    uint entryId = reader.ReadUInt32(); uint entryOff = reader.ReadUInt32(); uint entrySize = reader.ReadUInt32();
                    reader.ReadUInt32(); uint entryIter = reader.ReadUInt32();
                    if (entryId == id) {
                        SafeWriteLogicalAt(stream, nodeOffset, entryLogicalOffset + 20, BitConverter.GetBytes(nextIter), blockSize);
                        return true;
                    }
                }
                if (branches[0] != 0) {
                    for (int i = 0; i <= (int)entryCount; i++) if (ScanDirectoryAndSet(stream, branches[i], id, off, size, nextIter, blockSize)) return true;
                }
            }
            return false;
        }

        private void UpdateGlobalIteration(FileStream stream, uint blockSize, uint nextTotal)
        {
            uint iterID = 0xFFFF0001; if (!ACE.DatLoader.DatManager.CellDat.AllFiles.TryGetValue(iterID, out var entry)) return;
            byte[] nodeData = ReadLogical(stream, entry.FileOffset, entry.FileSize, blockSize);
            using (var rms = new MemoryStream(nodeData))
            using (var reader = new BinaryReader(rms))
            using (var wms = new MemoryStream())
            using (var writer = new BinaryWriter(wms)) {
                int oldTotal = reader.ReadInt32(); writer.Write((int)nextTotal);
                bool extended = false; int iterationCount = oldTotal;
                while (iterationCount > 0) {
                    int rangeConsecutive = reader.ReadInt32(); int rangeStart = reader.ReadInt32(); iterationCount += rangeConsecutive;
                    if (iterationCount == 0 && rangeStart + Math.Abs(rangeConsecutive) == nextTotal) { writer.Write(rangeConsecutive - 1); writer.Write(rangeStart); extended = true; }
                    else { writer.Write(rangeConsecutive); writer.Write(rangeStart); }
                }
                if (!extended) { writer.Write(-1); writer.Write((int)nextTotal); }
                byte[] packedData = wms.ToArray();
                UpdateDirectoryEntryLogical(stream, iterID, entry.FileOffset, entry.FileSize, entry.FileOffset, (uint)packedData.Length, blockSize);
                ReflectionCache.DatFileFileSizeField?.SetValue(entry, (uint)packedData.Length); ReflectionCache.DatFileIterationField?.SetValue(entry, nextTotal);
                SafeWriteLogical(stream, entry.FileOffset, packedData, blockSize, true);
                ACE.DatLoader.DatManager.CellDat.FileCache.TryRemove(iterID, out _);
                MainWindow.Dispatcher.Invoke(() => MainWindow.AddStatusText($"DAT Global Iteration updated to {nextTotal}"));
            }
        }

        private byte[] ReadLogical(FileStream stream, uint offset, uint size, uint blockSize)
        {
            byte[] result = new byte[size]; int resultIdx = 0; uint currentBlock = offset;
            while (resultIdx < (int)size) {
                stream.Seek(currentBlock, SeekOrigin.Begin);
                byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4); uint nextAddr = BitConverter.ToUInt32(ptrBytes, 0);
                int toRead = Math.Min((int)size - resultIdx, (int)blockSize - 4);
                stream.Read(result, resultIdx, toRead); resultIdx += toRead;
                if (resultIdx < (int)size) { if (nextAddr == 0) break; currentBlock = nextAddr; }
            }
            return result;
        }

        private byte[] PackLandblockInfo(LandblockInfo info)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms)) {
                writer.Write(info.Id); writer.Write(info.NumCells); writer.Write((uint)info.Objects.Count);
                foreach (var obj in info.Objects) {
                    writer.Write(obj.Id); writer.Write(obj.Frame.Origin.X); writer.Write(obj.Frame.Origin.Y); writer.Write(obj.Frame.Origin.Z);
                    writer.Write(obj.Frame.Orientation.W); writer.Write(obj.Frame.Orientation.X); writer.Write(obj.Frame.Orientation.Y); writer.Write(obj.Frame.Orientation.Z);
                }
                writer.Write((ushort)info.Buildings.Count); writer.Write((ushort)info.PackMask);
                foreach (var bld in info.Buildings) {
                    writer.Write(bld.ModelId);
                    writer.Write(bld.Frame.Origin.X); writer.Write(bld.Frame.Origin.Y); writer.Write(bld.Frame.Origin.Z);
                    writer.Write(bld.Frame.Orientation.W); writer.Write(bld.Frame.Orientation.X); writer.Write(bld.Frame.Orientation.Y); writer.Write(bld.Frame.Orientation.Z);
                    writer.Write(bld.NumLeaves); writer.Write((uint)bld.Portals.Count);
                    foreach (var port in bld.Portals) {
                        writer.Write((ushort)port.Flags); writer.Write(port.OtherCellId); writer.Write(port.OtherPortalId);
                        writer.Write((ushort)port.StabList.Count); foreach (var stab in port.StabList) writer.Write(stab);
                        if (ms.Position % 4 != 0) writer.Write(new byte[4 - (ms.Position % 4)]);
                    }
                }
                if ((info.PackMask & 1) == 1) {
                    writer.Write((ushort)info.RestrictionTables.Count); writer.Write((ushort)0);
                    foreach (var kvp in info.RestrictionTables) { writer.Write(kvp.Key); writer.Write(kvp.Value); }
                }
                while (ms.Position % 4 != 0) writer.Write((byte)0);
                return ms.ToArray();
            }
        }

        private byte[] PackLandblock(CellLandblock landblock)
        {
            using (var ms = new MemoryStream())
            using (var writer = new BinaryWriter(ms)) {
                writer.Write(landblock.Id); writer.Write(landblock.HasObjects ? 1U : 0U);
                for (int i = 0; i < 81; i++) writer.Write(landblock.Terrain[i]);
                for (int i = 0; i < 81; i++) writer.Write(landblock.Height[i]);
                while (ms.Position % 4 != 0) writer.Write((byte)0);
                return ms.ToArray();
            }
        }

        public void Draw(Microsoft.Xna.Framework.GameTime time) 
        { 
            Render.Draw(); 
            if (ConfigManager.Config.Toggles.ShowCellGrid) DrawCellGrid(); 
            if (MainMenu.ShowHUD) Render.DrawHUD(); 
            if (Player != null && PlayerMode) Player.Draw();
            SaveMeter.Draw();
        }
        private float GetHeightHelper(Landblock lb, float x, float y)
        {
            int ix = (int)Math.Clamp(Math.Round(x / 24.0f), 0, 8);
            int iy = (int)Math.Clamp(Math.Round(y / 24.0f), 0, 8);
            int idx = ix * 9 + iy;
            if (lb.Height != null && idx < lb.Height.Count)
                return ACE.Server.Physics.Common.LandDefs.LandHeightTable[lb.Height[idx]];
            return 0;
        }

        private void DrawCellGrid()
        {
            ACViewer.Render.Render.Effect.CurrentTechnique = ACViewer.Render.Render.Effect.Techniques["Picker"];
            ACViewer.Render.Render.Effect.Parameters["xWorld"].SetValue(Matrix.Identity);
            var cellColor = Color.Lime;
            var blockColor = Color.Red;
            _gridVertices.Clear();
            foreach (var lb_obj in LScape.Landblocks.Values)
            {
                if (!(lb_obj is Landblock lb)) continue;
                uint lbx = lb.ID >> 24; uint lby = (lb.ID >> 16) & 0xFF;
                float baseWorldX = lbx * 192.0f; float baseWorldY = lby * 192.0f;
                for (int i = 0; i <= 8; i++)
                {
                    float offset = i * 24.0f;
                    Color gridColorY = (i == 0 || i == 8) ? blockColor : cellColor;
                    for (int j = 0; j < 8; j++)
                    {
                        float yStart = j * 24.0f; float yEnd = (j + 1) * 24.0f;
                        float z1 = GetHeightHelper(lb, offset, yStart);
                        float z2 = GetHeightHelper(lb, offset, yEnd);
                        _gridVertices.Add(new VertexPositionColor(new Vector3(baseWorldX + offset, baseWorldY + yStart, z1 + 0.1f), gridColorY));
                        _gridVertices.Add(new VertexPositionColor(new Vector3(baseWorldX + offset, baseWorldY + yEnd, z2 + 0.1f), gridColorY));
                    }
                    for (int j = 0; j < 8; j++)
                    {
                        float xStart = j * 24.0f; float xEnd = (j + 1) * 24.0f;
                        float z1 = GetHeightHelper(lb, xStart, offset);
                        float z2 = GetHeightHelper(lb, xEnd, offset);
                        _gridVertices.Add(new VertexPositionColor(new Vector3(baseWorldX + xStart, baseWorldY + offset, z1 + 0.1f), gridColorY));
                        _gridVertices.Add(new VertexPositionColor(new Vector3(baseWorldX + xEnd, baseWorldY + offset, z2 + 0.1f), gridColorY));
                    }
                }
            }
            if (_gridVertices.Count > 0)
            {
                if (_gridVertexArray.Length < _gridVertices.Count)
                    _gridVertexArray = new VertexPositionColor[_gridVertices.Count * 2];
                for (int i = 0; i < _gridVertices.Count; i++) _gridVertexArray[i] = _gridVertices[i];
                foreach (var pass in ACViewer.Render.Render.Effect.CurrentTechnique.Passes) { pass.Apply(); GameView.Instance.GraphicsDevice.DrawUserPrimitives(PrimitiveType.LineList, _gridVertexArray, 0, _gridVertices.Count / 2); }
            }
        }

        public Player Player { get; set; }
        public bool EnterPlayerMode() {
            Player = new Player(true); var cameraPos = Camera.GetPosition(); if (cameraPos == null) return false;
            var success = Player.WorldObject.AddPhysicsObj(cameraPos); if (!success) return false;
            var r_PhysicsObj = new R_PhysicsObj(Player.PhysicsObj); Buffer.AddPlayer(r_PhysicsObj);
            Buffer.BuildTextureAtlases(Buffer.AnimatedTextureAtlasChains); Buffer.BuildBuffer(Buffer.RB_Animated);
            Camera.Locked = true; PlayerMode = true; FreeRoam = true; return true;
        }
        public void ExitPlayerMode() {
            Camera.Locked = false; Camera.Dir = Vector3.Transform(Vector3.UnitY, Player.PhysicsObj.Position.Frame.Orientation.ToXna());
            PlayerMode = false; Player = null;
            Buffer.ClearBuffer(Buffer.RB_Animated); Buffer.RB_Animated = new Dictionary<GfxObjTexturePalette, GfxObjInstance_Shared>();
            if (GameView.WorldViewer != null && GameView.WorldViewer != this) GameView.WorldViewer.ExitPlayerMode();
        }
    }
}