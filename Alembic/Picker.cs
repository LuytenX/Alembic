using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Entity.Enum;
using ACE.Server.Physics;
using ACE.Server.Physics.Common;
using ACE.Server.Physics.Util;
using ACE.Server.WorldObjects;

using ACViewer.Enum;
using ACViewer.Model;
using ACViewer.Primitives;
using ACViewer.Render;
using ACViewer.View;
using ACViewer.Extensions;

namespace ACViewer
{
    public static class Picker
    {
        private static Viewport Viewport => GameView.Instance.GraphicsDevice.Viewport;
        private static Camera Camera => GameView.Camera;

        public static bool BuildingInspectionMode { get; set; } = false;

        private static int _lastRawMouseX;
        private static int _lastRawMouseY;

        public static void HandleLeftClick(int mouseX, int mouseY)
        {
            MouseEx.GetCursorPos(out var p);
            _lastRawMouseX = p.X;
            _lastRawMouseY = p.Y;

            var projectionInverse = Matrix.Invert(Camera.ProjectionMatrix);
            var viewMatrix = Matrix.CreateLookAt(Vector3.Zero, Camera.Dir, Camera.Up);
            var viewInverse = Matrix.Invert(viewMatrix);
            var transform = projectionInverse * viewInverse;

            var nx = mouseX * 2.0f / Viewport.Width - 1.0f;
            var ny = 1.0f - mouseY * 2.0f / Viewport.Height;

            var viewportCoordNorm = new Vector3(nx, ny, 1.0f);
            var dir = Vector3.Normalize(Vector3.Transform(viewportCoordNorm, transform));

            TryFindObject(dir);
        }

        private static readonly uint setupId = 0x02000124;
        private static readonly ObjectGuid pickerGuid = new ObjectGuid(0x80000000);

        public static Vector3 Dir { get; set; }
        public static PickResult PickResult { get; set; }
        public static PickResult LastPickResult { get; set; }

        public static bool EditMode { get; set; } = false;
        public static bool PaintMode { get; set; } = false;
        public static bool ObjectMode { get; set; } = false;

        public static float BrushSize { get; set; } = 1.0f;
        public static int BrushStrength { get; set; } = 1;

        public static ushort CurrentTerrainType { get; set; } = 0;
        public static bool EyeDropperMode { get; set; } = false;
        public static float CurrentObjectScale => BrushSize;

        public static uint CurrentObjectId { get; set; } = 0;
        public static float CurrentObjectYaw { get; set; } = 0.0f;

        public static object SelectedStab { get; set; }
        public static Landblock SelectedParentLandblock { get; set; }
        public static bool SuppressSelectionHighlight { get; set; } = false;

        public static bool IsDragging { get; set; } = false;
        public static bool IsRotating { get; set; } = false;
        public static float GhostYaw { get; set; } = 0.0f;

        public static System.Numerics.Vector3 DragOffset { get; set; } = System.Numerics.Vector3.Zero;
        private static object DraggedStab { get; set; }
        private static Landblock DraggedOriginLb { get; set; }
        private static System.Numerics.Vector3 DragOriginWorldPos { get; set; }

        public static System.Numerics.Vector3? LastBrushHitPos { get; set; }
        private static DateTime LastApplyTime { get; set; } = DateTime.MinValue;

        public static (object, Landblock) FindStabForPhysicsObj(PhysicsObj target)
        {
            if (target == null || target.IsDestroyed) return (null, null);
            if (target is BuildingObj building)
            {
                foreach (var lb_obj in LScape.Landblocks.Values)
                {
                    if (lb_obj is Landblock lb && lb.Buildings != null && lb.Buildings.Contains(building))
                    {
                        if (lb.Info != null)
                        {
                            var buildingPos = building.Position.Frame.Origin;
                            ACE.DatLoader.Entity.BuildInfo bestMatch = null;
                            float minDistance = float.MaxValue;
                            foreach (var buildInfo in lb.Info.Buildings)
                            {
                                if (buildInfo.ModelId == building.ID)
                                {
                                    float dist = (buildInfo.Frame.Origin - buildingPos).Length();
                                    if (dist < minDistance) { minDistance = dist; bestMatch = buildInfo; }
                                }
                            }
                            if (bestMatch != null) return (bestMatch, lb);
                        }
                    }
                }
            }
            if (target.SourceStab is Stab stab)
            {
                foreach (var lb_obj in LScape.Landblocks.Values)
                {
                    if (lb_obj is Landblock lb) {
                        if (lb.Info != null && lb.Info.Objects.Contains(stab)) return (stab, lb);
                        if (lb.IsDungeon) {
                            foreach (var cell in lb.get_envcells())
                                if (cell._envCell.StaticObjects.Contains(stab)) return (stab, lb);
                        }
                    }
                }
            }
            return (null, null);
        }

        public static void RotateSelected(float degrees)
        {
            if (SelectedStab == null || SelectedParentLandblock == null || Math.Abs(degrees) < 0.01f) return;
            bool rotated = false; uint objId = 0;
            var zRot = MathHelper.ToRadians(degrees);
            var additionalRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, zRot);
            if (SelectedStab is Stab stab && (SelectedParentLandblock.IsDungeon || SelectedParentLandblock.StaticObjects.Any(s => s.SourceStab == stab))) {
                var newOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(stab.Frame.Orientation, additionalRotation));
                stab.Frame.Init(stab.Frame.Origin, newOrientation);
                objId = stab.Id;
                rotated = true;
            } else if (SelectedStab is ACE.Server.Physics.Common.EnvCell envCell && SelectedParentLandblock.IsDungeon) {
                var newOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(envCell.Pos.Frame.Orientation, additionalRotation));
                envCell.Pos.Frame.set_rotate(newOrientation);
                envCell._envCell.Position.Init(envCell.Pos.Frame.Origin, envCell.Pos.Frame.Orientation);
                objId = envCell.ID;
                rotated = true;
            } else if (SelectedStab is ACE.DatLoader.Entity.BuildInfo buildInfo) {
                RotationHelper.RotateBuildingWithEnvironmentCells(buildInfo, SelectedParentLandblock, degrees);
                objId = buildInfo.ModelId;
                rotated = true;
            }
            if (!rotated) return;
            SelectedParentLandblock.IsInfoModified = true;
            if (!WorldViewer.Instance.ModifiedLandblocks.ContainsKey(SelectedParentLandblock.ID))
                WorldViewer.Instance.ModifiedLandblocks.Add(SelectedParentLandblock.ID, SelectedParentLandblock);
            
            RefreshVisuals();
            MainWindow.Instance.AddStatusText($"Rotated {objId:X8}");
        }

        public static void MoveSelected(System.Numerics.Vector3 worldPos, Landblock targetLb, bool isFinalDrop = false)
        {
            if (SelectedStab == null || SelectedParentLandblock == null || targetLb == null) return;
            if (!isFinalDrop) { DragOffset = worldPos - DragOriginWorldPos; IsDragging = true; return; }
            
            IsDragging = false; DragOffset = System.Numerics.Vector3.Zero;
            var targetLbx = targetLb.ID >> 24; var targetLby = targetLb.ID >> 16 & 0xFF;
            var localTargetPos = worldPos - new System.Numerics.Vector3(targetLbx * 192.0f, targetLby * 192.0f, 0);
            
            if (targetLb.ID != SelectedParentLandblock.ID) {
                MainWindow.Instance.AddStatusText($"Migrating {SelectedStab.GetType().Name} to {targetLb.ID:X8}...");
                if (SelectedStab is Stab stab) RotationHelper.MigrateObject(stab, SelectedParentLandblock, targetLb);
                else if (SelectedStab is ACE.DatLoader.Entity.BuildInfo buildInfo) RotationHelper.MigrateBuildingWithCells(buildInfo, SelectedParentLandblock, targetLb, localTargetPos);
                SelectedParentLandblock = targetLb;
            } else {
                if (SelectedStab is Stab s) {
                    s.Frame.Init(localTargetPos, s.Frame.Orientation);
                    var physObj = SelectedParentLandblock.StaticObjects.FirstOrDefault(obj => obj.SourceStab == s);
                    if (physObj != null) { physObj.Position.Frame.Origin = localTargetPos; if (physObj.PartArray != null) physObj.PartArray.SetFrame(physObj.Position.Frame); RotationHelper.UpdateVisualFeedback(physObj); }
                } else if (SelectedStab is ACE.Server.Physics.Common.EnvCell envCell && SelectedParentLandblock.IsDungeon) {
                    envCell.Pos.Frame.Origin = localTargetPos;
                    envCell._envCell.Position.Init(localTargetPos, envCell.Pos.Frame.Orientation);
                } else if (SelectedStab is ACE.DatLoader.Entity.BuildInfo buildInfo) {
                    RotationHelper.MoveBuildingWithEnvironmentCells(buildInfo, SelectedParentLandblock, localTargetPos);
                }
            }
            if (SelectedParentLandblock != null) {
                SelectedParentLandblock.IsInfoModified = true;
                if (WorldViewer.Instance != null && !WorldViewer.Instance.ModifiedLandblocks.ContainsKey(SelectedParentLandblock.ID))
                    WorldViewer.Instance.ModifiedLandblocks.Add(SelectedParentLandblock.ID, SelectedParentLandblock);
            }
            RefreshVisuals();
            ClearSelection();
        }

        private static void RefreshVisuals() {
            var instance = WorldViewer.Instance;
            if (instance?.Buffer == null) return;

            // STABLE RE-INIT: Ensure visual and physics pointers are fully updated for every block
            var wasServer = PhysicsEngine.Instance.Server; PhysicsEngine.Instance.Server = false;
            foreach (var lb_obj in LScape.Landblocks.Values) {
                if (lb_obj is Landblock lb) {
                    lb.destroy_static_objects();
                    lb.destroy_buildings();
                    foreach (var cell in lb.get_envcells()) cell.destroy_static_objects();
                    
                    lb.init_static_objs();
                    lb.init_buildings();
                    foreach (var cell in lb.get_envcells()) cell.init_static_objects();
                }
            }
            PhysicsEngine.Instance.Server = wasServer;

            instance.Buffer.ClearBuffer();
            foreach (var lb in LScape.Landblocks.Values.OfType<Landblock>().OrderBy(l => l.ID)) {
                instance.Buffer.AddOutdoor(new R_Landblock(lb));
            }
            if (instance.PlayerMode && instance.Player != null) instance.Buffer.AddPlayer(new R_PhysicsObj(instance.Player.PhysicsObj));
            instance.Buffer.BuildBuffers();
        }

        public static void ApplyRaiseBrush(Vector3 dir, bool shift, bool ctrl)
        {
            MouseEx.GetCursorPos(out var p);
            int deltaX = p.X - _lastRawMouseX;
            _lastRawMouseX = p.X;
            _lastRawMouseY = p.Y;

            if (IsRotating && SelectedStab != null)
            {
                GhostYaw += deltaX * 0.5f; 
                return;
            }

            if (IsDragging && SelectedStab != null && SelectedParentLandblock != null)
            {
                var rayOrigin = Camera.Position.ToNumerics();
                var rayDir = dir.ToNumerics();
                Landblock dragLb = null; System.Numerics.Vector3? dragPos = null; float minDragDist = float.MaxValue;
                foreach (var lb_obj in LScape.Landblocks.Values) {
                    if (lb_obj is Landblock lb) {
                        var worldOff = new System.Numerics.Vector3((lb.ID >> 24) * 192.0f, (lb.ID >> 16 & 0xFF) * 192.0f, 0);
                        for (int cellIdx = 0; cellIdx < 64; cellIdx++) {
                            if (lb.LandCells.TryGetValue(cellIdx, out var cell_obj) && cell_obj is LandCell cell) {
                                if (cell.Polygons == null) continue;
                                foreach (var poly in cell.Polygons) {
                                    var localRayOrigin = rayOrigin - worldOff; float time = 0;
                                    if (poly.polygon_hits_ray(new ACE.Server.Physics.Ray(localRayOrigin, rayDir, 1000.0f), ref time)) if (time < minDragDist) { minDragDist = time; dragLb = lb; dragPos = localRayOrigin + rayDir * time + worldOff; }
                                }
                            }
                        }
                    }
                }
                if (dragPos.HasValue && dragLb != null) { LastBrushHitPos = dragPos; MoveSelected(dragPos.Value, dragLb, false); return; }
            }

            if ((DateTime.Now - LastApplyTime).TotalMilliseconds < 100) return;

            if (ObjectMode) {
                if ((IsDragging || IsRotating) && SelectedStab != null) return;
                TryFindObject(dir);
                if (PickResult?.PhysicsObj != null) {
                    var (hitObj, targetLb) = FindStabForPhysicsObj(PickResult.PhysicsObj);
                    if (hitObj != null) {
                        if (shift) {
                            bool removed = false; if (hitObj is Stab s) { if (targetLb.Info != null) removed = targetLb.Info.Objects.Remove(s); if (!removed && targetLb.IsDungeon) foreach(var cell in targetLb.get_envcells()) if (cell._envCell.StaticObjects.Remove(s)) { removed = true; break; } }
                            targetLb.IsInfoModified = true; RefreshVisuals(); ClearSelection(); LastApplyTime = DateTime.Now; return;
                        } else {
                            SelectedStab = hitObj; SelectedParentLandblock = targetLb;
                            if (SelectedStab is Stab s) { var obj = SelectedParentLandblock.StaticObjects.FirstOrDefault(o => o.SourceStab == s); DragOriginWorldPos = obj != null ? obj.Position.GetWorldPos().ToNumerics() : System.Numerics.Vector3.Zero; }
                            else if (SelectedStab is ACE.DatLoader.Entity.BuildInfo b) { var obj = SelectedParentLandblock.Buildings.FirstOrDefault(o => o.SourceStab == b); DragOriginWorldPos = obj != null ? obj.Position.GetWorldPos().ToNumerics() : System.Numerics.Vector3.Zero; }
                            if (ctrl) { IsRotating = true; IsDragging = false; GhostYaw = 0; } else { IsDragging = true; IsRotating = false; }
                            LastApplyTime = DateTime.Now; return;
                        }
                    }
                }
            }

            Landblock hitLbFallback = null; System.Numerics.Vector3? hitPosFallback = null; float minRayDist = float.MaxValue;
            foreach (var lb_obj in LScape.Landblocks.Values) { if (lb_obj is Landblock lb) { var worldOff = new System.Numerics.Vector3((lb.ID >> 24) * 192.0f, (lb.ID >> 16 & 0xFF) * 192.0f, 0); for (int cellIdx = 0; cellIdx < 64; cellIdx++) { if (lb.LandCells.TryGetValue(cellIdx, out var cell_obj) && cell_obj is LandCell cell) { if (cell.Polygons == null) continue; foreach (var poly in cell.Polygons) { var localRayOrigin = Camera.Position.ToNumerics() - worldOff; float time = 0; if (poly.polygon_hits_ray(new ACE.Server.Physics.Ray(localRayOrigin, dir.ToNumerics(), 1000.0f), ref time)) if (time < minRayDist) { minRayDist = time; hitLbFallback = lb; hitPosFallback = localRayOrigin + dir.ToNumerics() * time + worldOff; } } } } } }
            if (hitLbFallback != null && hitPosFallback.HasValue) {
                var hitLb = hitLbFallback; var hitPos = hitPosFallback;
                if (LastBrushHitPos.HasValue && (hitPos.Value - LastBrushHitPos.Value).Length() < Math.Max(0.1f, BrushSize * 0.25f)) { HighlightBrush(hitPos.Value); return; }
                LastBrushHitPos = hitPos; bool anyModified = false;

                if (ObjectMode && !shift && CurrentObjectId != 0) {
                    uint type = CurrentObjectId >> 24; if (type != 0x01 && type != 0x02) return;
                    if (!ACE.DatLoader.DatManager.PortalDat.AllFiles.ContainsKey(CurrentObjectId)) return;
                    if (hitLb.Info == null) { hitLb.Info = new LandblockInfo(); ReflectionCache.FileTypeIdField?.SetValue(hitLb.Info, hitLb.ID - 1); uint numCells = 0; while (ACE.DatLoader.DatManager.CellDat.AllFiles.ContainsKey((hitLb.ID & 0xFFFF0000) | (0x100 + numCells))) numCells++; ReflectionCache.LandblockInfoNumCellsField?.SetValue(hitLb.Info, numCells); hitLb._landblock.HasObjects = true; hitLb.IsModified = true; }
                    var localPos = hitPos.Value - new System.Numerics.Vector3((hitLb.ID >> 24) * 192.0f, (hitLb.ID >> 16 & 0xFF) * 192.0f, 0);
                    var newObj = new Stab(); ReflectionCache.StabIdField?.SetValue(newObj, CurrentObjectId); var rot = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, MathHelper.ToRadians(CurrentObjectYaw)); newObj.Frame.Init(localPos, rot); hitLb.Info.Objects.Add(newObj); hitLb.IsInfoModified = true; anyModified = true; SelectedStab = newObj; SelectedParentLandblock = hitLb;
                } else if (PaintMode || EditMode) {
                    float minVertexDist = float.MaxValue; System.Numerics.Vector3 snappedPos = hitPos.Value; var worldOff = new System.Numerics.Vector3((hitLb.ID >> 24) * 192.0f, (hitLb.ID >> 16 & 0xFF) * 192.0f, 0);
                    for (int i = 0; i < 81; i++) { var vPos = hitLb.VertexArray.Vertices[i].Origin + worldOff; float d = (vPos - hitPos.Value).Length(); if (d < minVertexDist) { minVertexDist = d; snappedPos = vPos; } } hitPos = snappedPos;
                    if (PaintMode) {
                        foreach (var lbo in LScape.Landblocks.Values) { if (lbo is Landblock lb) { var lworldOff = new System.Numerics.Vector3((lb.ID >> 24) * 192.0f, (lb.ID >> 16 & 0xFF) * 192.0f, 0); bool lbModified = false; for (int i = 0; i < 81; i++) if ((lb.VertexArray.Vertices[i].Origin + lworldOff - hitPos.Value).Length() <= BrushSize) if (lb._landblock.Terrain[i] != CurrentTerrainType) { lb._landblock.Terrain[i] = CurrentTerrainType; lb.IsModified = true; lbModified = true; anyModified = true; } if (lbModified && WorldViewer.Instance != null && !WorldViewer.Instance.ModifiedLandblocks.ContainsKey(lb.ID)) WorldViewer.Instance.ModifiedLandblocks.Add(lb.ID, lb); } }
                    } else if (EditMode) {
                        int change = shift ? -BrushStrength : BrushStrength;
                        foreach (var lbo in LScape.Landblocks.Values) { if (lbo is Landblock lb) { var lworldOff = new System.Numerics.Vector3((lb.ID >> 24) * 192.0f, (lb.ID >> 16 & 0xFF) * 192.0f, 0); bool lbModified = false; for (int i = 0; i < 81; i++) if ((lb.VertexArray.Vertices[i].Origin + lworldOff - hitPos.Value).Length() <= BrushSize) { int newH = Math.Clamp(lb._landblock.Height[i] + change, 0, 255); if (lb._landblock.Height[i] != newH) { lb._landblock.Height[i] = (byte)newH; lb.IsModified = true; lbModified = true; anyModified = true; } } if (lbModified && WorldViewer.Instance != null && !WorldViewer.Instance.ModifiedLandblocks.ContainsKey(lb.ID)) WorldViewer.Instance.ModifiedLandblocks.Add(lb.ID, lb); } }
                    }
                }
                if (anyModified) {
                    ACE.Server.Physics.Common.LandSurf.Instance = new ACE.Server.Physics.Common.LandSurf(DatManager.PortalDat.RegionDesc.TerrainInfo.LandSurfaces);
                    if (WorldViewer.Instance != null) WorldViewer.Instance.CachedLandSurf = ACE.Server.Physics.Common.LandSurf.Instance;
                    
                    var wasServer = PhysicsEngine.Instance.Server; PhysicsEngine.Instance.Server = false;
                    var modifiedIDs = LScape.Landblocks.Values.OfType<Landblock>().Where(l => l.IsModified).Select(l => l.ID).ToList();
                    
                    foreach (var lbo in LScape.Landblocks.Values) {
                        if (lbo is Landblock lb) {
                            bool needsReconstruct = false;
                            uint lbx = lb.ID >> 24; uint lby = (lb.ID >> 16) & 0xFF;
                            // Reconstruct if THIS block was modified OR if ANY neighbor was modified (Shared Edge Sync)
                            foreach(var mID in modifiedIDs) {
                                uint mx = mID >> 24; uint my = (mID >> 16) & 0xFF;
                                if (Math.Abs((int)mx - (int)lbx) <= 1 && Math.Abs((int)my - (int)lby) <= 1) { needsReconstruct = true; break; }
                            }

                            if (needsReconstruct) {
                                if (lb.Polygons == null) lb.InitPVArrays();
                                lb.ConstructVertices(); lb.ConstructPolygons(lb.ID); lb.ConstructNormals(); lb.ConstructUVs(lb.ID); lb.CalcWater();
                            }
                            // Re-init visuals for every active block to keep the world visible
                            lb.destroy_static_objects(); lb.destroy_buildings();
                            foreach (var cell in lb.get_envcells()) cell.destroy_static_objects();
                            lb.init_static_objs(); lb.init_buildings();
                            foreach (var cell in lb.get_envcells()) cell.init_static_objects();
                        }
                    }
                    PhysicsEngine.Instance.Server = wasServer;
                    RefreshVisuals(); 
                    LastApplyTime = DateTime.Now;
                }
                HighlightBrush(hitPos.Value);
            }
        }

        public static void ResetBrush()
        {
            try {
                if (IsRotating && SelectedStab != null) { RotateSelected(GhostYaw); ClearSelection(); }
                else if (IsDragging && SelectedStab != null && LastBrushHitPos.HasValue) {
                    Landblock targetLb = null; foreach (var lb_obj in LScape.Landblocks.Values) { if (lb_obj is Landblock lb) { var worldOff = new System.Numerics.Vector3((lb.ID >> 24) * 192.0f, (lb.ID >> 16 & 0xFF) * 192.0f, 0); if (LastBrushHitPos.Value.X >= worldOff.X && LastBrushHitPos.Value.X < worldOff.X + 192.0f && LastBrushHitPos.Value.Y >= worldOff.Y && LastBrushHitPos.Value.Y < worldOff.Y + 192.0f) { targetLb = lb; break; } } }
                    if (targetLb != null) MoveSelected(LastBrushHitPos.Value, targetLb, true); else ClearSelection();
                }
            } finally { IsDragging = false; IsRotating = false; GhostYaw = 0; DragOffset = System.Numerics.Vector3.Zero; LastBrushHitPos = null; }
        }

        public static void HighlightBrush(System.Numerics.Vector3 pos)
        {
            float s = BrushSize > 0 ? BrushSize : 1.0f;
            var center = pos.ToXna() + new Vector3(0, 0, 0.5f);
            HitVertices = new VertexPositionColor[] {
                new VertexPositionColor(center + new Vector3(-s, 0, 0), Color.Cyan),
                new VertexPositionColor(center + new Vector3(s, 0, 0), Color.Cyan),
                new VertexPositionColor(center + new Vector3(0, -s, 0), Color.Cyan),
                new VertexPositionColor(center + new Vector3(0, s, 0), Color.Cyan)
            };
            HitIndices = new int[] { 0, 1, 2, 3 };
            PickResult = new PickResult { Type = PickType.LandCell };
        }

        public static void TryFindObject(Vector3 dir)
        {
            Dir = dir; LastPickResult = PickResult; ClearSelection(); PickResult = new PickResult();
            var startPos = Camera.GetPosition(); if (startPos == null) return;
            var maxSteps = 500; var stepSize = 1.0f; var i = 0; var stepDir = (dir * stepSize).ToNumerics();
            var singleBlock = WorldViewer.Instance.SingleBlock;
            if (singleBlock != uint.MaxValue && WorldViewer.Instance.DungeonMode) {
                var landblock = LScape.get_landblock(singleBlock);
                if (landblock.IsDungeon) {
                    if (startPos.Landblock != singleBlock >> 16) startPos.Reframe(singleBlock);
                    var adjustCell = AdjustCell.Get(startPos.Landblock);
                    for ( ; i < maxSteps; i++) { var foundCell = adjustCell.GetCell(startPos.Frame.Origin); if (foundCell != null) { startPos.ObjCellID = foundCell.Value; break; } startPos.Frame.Origin += stepDir; }
                }
            }
            var pickerObj = PhysicsObj.makeObject(setupId, pickerGuid.Full, true);
            pickerObj.State |= PhysicsState.PathClipped; pickerObj.State &= ~PhysicsState.Gravity;
            pickerObj.set_object_guid(pickerGuid);
            var worldObj = new WorldObject(); var weenie = new WeenieObject(worldObj); pickerObj.set_weenie_obj(weenie);
            PhysicsObj.IsPicking = true; var spawned = false;
            for ( ; i < maxSteps; i++) {
                if (!spawned) { var success = pickerObj.enter_world(startPos); if (!success) { startPos.Frame.Origin += stepDir; continue; } else spawned = true; }
                var nextPos = new ACE.Server.Physics.Common.Position(pickerObj.Position); nextPos.Frame.Origin += stepDir;
                var transition = pickerObj.transition(pickerObj.Position, nextPos, false);
                if (transition == null) break;
                else if (transition.CollisionInfo.CollidedWithEnvironment || transition.CollisionInfo.CollideObject.Count > 0) {
                    bool hitBuilding = transition.CollisionInfo.CollideObject.Any(obj => obj is BuildingObj);
                    if (hitBuilding) { PickResult.PhysicsObj = transition.CollisionInfo.CollideObject.First(obj => obj is BuildingObj); PickResult.Type = PickType.GfxObj; }
                    BuildHitPolys(); break;
                } else pickerObj.SetPositionInternal(transition);
            }
            PhysicsObj.IsPicking = false; pickerObj.DestroyObject();
            if (BuildingInspectionMode && PickResult?.ObjCell is ACE.Server.Physics.Common.EnvCell envCell) AnalyzeBuildingStructure(envCell);
        }

        public static VertexPositionColor[] HitVertices { get; set; }
        public static int[] HitIndices { get; set; }

        public static void AnalyzeBuildingStructure(ACE.Server.Physics.Common.EnvCell envCell)
        {
            try {
                var building = FindBuildingForCell(envCell);
                string structureInfo = (building != null) ? GenerateBuildingStructureReport(building, envCell) : GenerateCellStructureReport(envCell);
                var inspector = new View.BuildingInspector(); inspector.SetBuildingStructure(structureInfo); inspector.Owner = View.MainWindow.Instance; inspector.ShowDialog();
            } catch (Exception) { }
        }

        private static PhysicsObj FindBuildingForCell(ACE.Server.Physics.Common.EnvCell envCell)
        {
            var landblock = envCell.CurLandblock;
            if (landblock?.Buildings != null) foreach (var building in landblock.Buildings) if (building is ACE.Server.Physics.Common.BuildingObj buildingObj) if (buildingObj.get_building_cells().Contains(envCell)) return building;
            return null;
        }

        private static string GenerateBuildingStructureReport(PhysicsObj building, ACE.Server.Physics.Common.EnvCell clickedCell)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"BUILDING STRUCTURE ANALYSIS"); sb.AppendLine($"========================="); sb.AppendLine();
            sb.AppendLine($"CLICKED CELL: {clickedCell.ID:X8}"); sb.AppendLine($"BUILDING ROOT: {building.ID:X8}"); sb.AppendLine();
            var buildingCells = (building is ACE.Server.Physics.Common.BuildingObj buildingObj) ? buildingObj.get_building_cells() : new List<ACE.Server.Physics.Common.EnvCell>();
            sb.AppendLine($"BUILDING ROOMS ({buildingCells.Count} total):"); sb.AppendLine();
            int roomIndex = 0;
            foreach (var cell in buildingCells.OrderBy(c => c.ID)) {
                sb.AppendLine($"├── Room {roomIndex + 1}: 0x{cell.ID:X8}");
                sb.AppendLine($"│   ├── EnvironmentID: 0x{cell.EnvironmentID:X8}");
                sb.AppendLine($"│   ├── CellStructure: 0x{cell.CellStructureID:X4}");
                if (cell.Portals != null) for (int i = 0; i < cell.Portals.Count; i++) sb.AppendLine($"│   │   └── Portal {i}: OtherCellID:0x{cell.Portals[i].OtherCellId:X4}");
                if (cell.VisibleCellIDs != null) sb.AppendLine($"│   ├── VisibleCells: {string.Join(", ", cell.VisibleCellIDs.Select(id => $"0x{id:X4}"))}");
                if (cell.ID == clickedCell.ID) sb.AppendLine($"│   └── [CLICKED CELL]"); else sb.AppendLine($"│   └── ");
                roomIndex++;
            }
            return sb.ToString();
        }

        private static string GenerateCellStructureReport(ACE.Server.Physics.Common.EnvCell envCell)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"CELL STRUCTURE ANALYSIS"); sb.AppendLine($"======================"); sb.AppendLine();
            sb.AppendLine($"CELL ID: 0x{envCell.ID:X8}"); sb.AppendLine($"ENVIRONMENT ID: 0x{envCell.EnvironmentID:X8}"); sb.AppendLine($"CELL STRUCTURE: 0x{envCell.CellStructureID:X4}");
            if (envCell.Portals != null) for (int i = 0; i < envCell.Portals.Count; i++) sb.AppendLine($"├── Portal {i}: OtherCellID:0x{envCell.Portals[i].OtherCellId:X4}");
            if (envCell.StaticObjects != null) {
                sb.AppendLine($"STATIC OBJECTS: {envCell.StaticObjects.Count} object(s)");
                for (int i = 0; i < Math.Min(envCell.StaticObjects.Count, 10); i++) sb.AppendLine($"├── 0x{envCell.StaticObjects[i].ID:X8}: {envCell.StaticObjects[i].Position.Frame.Origin}");
            }
            return sb.ToString();
        }

        public static void BuildHitPolys()
        {
            if (PickResult == null) return;
            if (PickResult.Type == PickType.Undef && PickResult.PhysicsObj is BuildingObj bld) PickResult.Type = PickType.GfxObj;
            var hitVertices = new List<VertexPositionColor>(); var hitIndices = new List<int>(); var i = 0;
            switch (PickResult.Type)
            {
                case PickType.LandCell:
                    var landCell = PickResult.ObjCell as LandCell; var landblockPos = new ACE.Server.Physics.Common.Position(landCell.Pos); landblockPos.Frame.Origin = System.Numerics.Vector3.Zero;
                    var transform = landblockPos.ToXna();
                    foreach (var polygon in landCell.Polygons) { var startIdx = i; foreach (var v in polygon.Vertices) { hitVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.OrangeRed)); hitIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (hitIndices.Count > 0) { hitIndices.RemoveAt(hitIndices.Count - 1); hitIndices.Add(startIdx); } }
                    var landblock = DatManager.CellDat.ReadFromDat<CellLandblock>(landCell.CurLandblock.ID);
                    if (landblock != null) FileInfo.Instance.SetInfo(new FileTypes.CellLandblock(landblock).BuildTree());
                    break;
                case PickType.EnvCell:
                    var envCell = PickResult.ObjCell as ACE.Server.Physics.Common.EnvCell; transform = envCell.Pos.ToXna();
                    foreach (var polygon in envCell.CellStructure.Polygons.Values) { var startIdx = i; foreach (var v in polygon.Vertices) { hitVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.OrangeRed)); hitIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (hitIndices.Count > 0) { hitIndices.RemoveAt(hitIndices.Count - 1); hitIndices.Add(startIdx); } }
                    var _envCell = DatManager.CellDat.ReadFromDat<ACE.DatLoader.FileTypes.EnvCell>(envCell.ID);
                    if (_envCell != null) FileInfo.Instance.SetInfo(new FileTypes.EnvCell(_envCell).BuildTree());
                    break;
                case PickType.GfxObj:
                    foreach (var part in PickResult.PhysicsObj.PartArray.Parts) {
                        if (part.GfxObj.ID == 0x010001ec) continue; transform = part.Pos.ToXna(); if (part.GfxObjScale != System.Numerics.Vector3.Zero) transform = Matrix.CreateScale(part.GfxObjScale.ToXna()) * transform;
                        foreach (var polygon in part.GfxObj.Polygons.Values) { var startIdx = i; foreach (var v in polygon.Vertices) { hitVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.OrangeRed)); hitIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (hitIndices.Count > 0) { hitIndices.RemoveAt(hitIndices.Count - 1); hitIndices.Add(startIdx); } }
                    }
                    var partArray = PickResult.PhysicsObj.PartArray; var setupID = partArray.Setup._dat.Id;
                    if (setupID >> 24 == 0x2) { var setup = DatManager.PortalDat.ReadFromDat<SetupModel>(setupID); if (setup != null) FileInfo.Instance.SetInfo(new FileTypes.Setup(setup).BuildTree()); }
                    else { var gfxObjId = partArray.Parts[0].GfxObj._dat.Id; var gfxObj = DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.GfxObj>(gfxObjId); if (gfxObj != null) FileInfo.Instance.SetInfo(new FileTypes.GfxObj(gfxObj).BuildTree()); }
                    if (PickResult.PhysicsObj.WeenieObj?.WorldObject != null) { var wo = PickResult.PhysicsObj.WeenieObj.WorldObject; FileInfo.Instance.SetInfo(new FileTypes.WorldObject(wo).BuildTree()); if (wo is Portal portal && LastPickResult?.PhysicsObj != null && wo.PhysicsObj == LastPickResult.PhysicsObj && PickResult.ClickTime - LastPickResult.ClickTime < PickResult.DoubleClickTime) if (portal.Destination != null) { Teleport.Origin = portal.Destination.Pos; Teleport.Orientation = portal.Destination.Rotation; Teleport.teleport(portal.Destination.Cell); } }
                    break;
            }
            HitVertices = hitVertices.ToArray(); HitIndices = hitIndices.ToArray();
        }

        public static void AddVisibleCells()
        {
            var envCell = PickResult?.ObjCell as ACE.Server.Physics.Common.EnvCell; if (envCell == null) return;
            var hitVertices = new List<VertexPositionColor>(HitVertices); var hitIndices = new List<int>(HitIndices); var i = hitVertices.Count;
            foreach (var visibleCell in envCell.VisibleCells.Values) {
                var transform = visibleCell.Pos.ToXna();
                foreach (var polygon in visibleCell.CellStructure.Polygons.Values) { var startIdx = i; foreach (var v in polygon.Vertices) { hitVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.Orange)); hitIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (hitIndices.Count > 0) { hitIndices.RemoveAt(hitIndices.Count - 1); hitIndices.Add(startIdx); } }
            }
            HitVertices = hitVertices.ToArray(); HitIndices = hitIndices.ToArray();
        }

        private static SpherePrimitive _spherePrimitive;
        private static SpherePrimitive SpherePrimitive => _spherePrimitive ??= new SpherePrimitive(GameView.Instance.GraphicsDevice, 1.0f, 10);

        public static List<Matrix> SphereTransforms { get; set; }
        public static List<Matrix> CylinderTransforms { get; set; }
        public static VertexPositionColor[] PhysicsVertices { get; set; }
        public static int[] PhysicsIndices { get; set; }

        public static void ShowCollision()
        {
            if (PickResult == null) return; SphereTransforms = null; CylinderTransforms = null; PhysicsVertices = null; PhysicsIndices = null;
            if (PickResult.PhysicsObj != null) {
                if (PickResult.PhysicsObj.PartArray.GetNumSphere() > 0) {
                    SphereTransforms = new List<Matrix>(); var worldPos = PickResult.PhysicsObj.Position.GetWorldPos();
                    foreach (var sphere in PickResult.PhysicsObj.PartArray.GetSphere()) { var transform = Matrix.CreateScale(sphere.Radius * 2 * PickResult.PhysicsObj.Scale) * Matrix.CreateTranslation(sphere.Center.ToXna()) * Matrix.CreateFromQuaternion(PickResult.PhysicsObj.Position.Frame.Orientation.ToXna()) * Matrix.CreateTranslation(worldPos); SphereTransforms.Add(transform); }
                }
                if (PickResult.PhysicsObj.State.HasFlag(PhysicsState.HasPhysicsBSP)) {
                    var physicsVertices = new List<VertexPositionColor>(); var physicsIndices = new List<int>(); var i = 0;
                    foreach (var part in PickResult.PhysicsObj.PartArray.Parts) {
                        if (part.GfxObj.ID == 0x010001ec) continue; var transform = part.Pos.ToXna(); if (part.GfxObjScale != System.Numerics.Vector3.Zero) transform = Matrix.CreateScale(part.GfxObjScale.ToXna()) * transform;
                        foreach (var polygon in part.GfxObj.PhysicsPolygons.Values) { var startIdx = i; foreach (var v in polygon.Vertices) { physicsVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.Orange)); physicsIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (physicsIndices.Count > 0) { physicsIndices.RemoveAt(physicsIndices.Count - 1); physicsIndices.Add(startIdx); } }
                    }
                    PhysicsVertices = physicsVertices.ToArray(); PhysicsIndices = physicsIndices.ToArray();
                }
            } else if (PickResult.ObjCell is ACE.Server.Physics.Common.EnvCell envCell) {
                var transform = envCell.Pos.ToXna(); var physicsVertices = new List<VertexPositionColor>(); var physicsIndices = new List<int>(); var i = 0;
                foreach (var polygon in envCell.CellStructure.PhysicsPolygons.Values) { var startIdx = i; foreach (var v in polygon.Vertices) { physicsVertices.Add(new VertexPositionColor(Vector3.Transform(v.Origin.ToXna(), transform), Color.Orange)); physicsIndices.AddRange(new List<int>() { i, i + 1 }); i++; } if (physicsIndices.Count > 0) { physicsIndices.RemoveAt(physicsIndices.Count - 1); physicsIndices.Add(startIdx); } }
                PhysicsVertices = physicsVertices.ToArray(); PhysicsIndices = physicsIndices.ToArray();
            }
        }

        private static GraphicsDevice GraphicsDevice => GameView.Instance.GraphicsDevice;
        private static Effect Effect => ACViewer.Render.Render.Effect;

        public static void DrawHitPoly()
        {
            if (HitVertices == null || HitVertices.Length == 0) return;
            if (!IsDragging && !IsRotating && (PickResult.PhysicsObj?.IsDestroyed ?? false)) { ClearSelection(); return; }
            var rs = new RasterizerState { CullMode = Microsoft.Xna.Framework.Graphics.CullMode.None, FillMode = FillMode.WireFrame };
            GraphicsDevice.RasterizerState = rs;
            
            Matrix worldTransform = Matrix.Identity;
            if (IsDragging) worldTransform = Matrix.CreateTranslation(DragOffset.ToXna());
            else if (IsRotating) {
                var origin = DragOriginWorldPos.ToXna();
                worldTransform = Matrix.CreateTranslation(-origin) * Matrix.CreateRotationZ(MathHelper.ToRadians(GhostYaw)) * Matrix.CreateTranslation(origin);
            }

            if (SphereTransforms == null && CylinderTransforms == null && PhysicsVertices == null) {
                Effect.CurrentTechnique = Effect.Techniques["Picker"]; 
                Effect.Parameters["xWorld"].SetValue(worldTransform);
                foreach (var pass in Effect.CurrentTechnique.Passes) { pass.Apply(); GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.LineList, HitVertices, 0, HitVertices.Length, HitIndices, 0, HitIndices.Length / 2); }
                Effect.Parameters["xWorld"].SetValue(Matrix.Identity);
            } else {
                if (SphereTransforms != null) foreach (var sphereTransform in SphereTransforms) SpherePrimitive.Draw(sphereTransform * worldTransform, Camera.ViewMatrix, Camera.ProjectionMatrix, Color.Orange);
                if (PhysicsVertices != null) { 
                    Effect.CurrentTechnique = Effect.Techniques["Picker"]; 
                    Effect.Parameters["xWorld"].SetValue(worldTransform);
                    foreach (var pass in Effect.CurrentTechnique.Passes) { pass.Apply(); GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.LineList, PhysicsVertices, 0, PhysicsVertices.Length, PhysicsIndices, 0, PhysicsIndices.Length / 2); }
                    Effect.Parameters["xWorld"].SetValue(Matrix.Identity);
                }
            }
            if (RenderLinks != null) { if (!RenderLinks.Head.WorldObject.PhysicsObj.IsDestroyed) RenderLinks.Draw(); else { RenderLinks.Dispose(); RenderLinks = null; } }
        }

        public static RenderLinks RenderLinks { get; set; }
        public static void BuildLinks(WorldObject wo) { if (RenderLinks != null) RenderLinks.Dispose(); RenderLinks = null; var node = new LinkNode(wo); node.AddParentChains(); node.AddChildTrees(); if (node.Parent != null || node.Children != null) RenderLinks = new RenderLinks(node); }

        public static void ClearSelection() { SelectedStab = null; SelectedParentLandblock = null; SuppressSelectionHighlight = false; HitVertices = null; HitIndices = null; SphereTransforms = null; CylinderTransforms = null; PhysicsVertices = null; PhysicsIndices = null; DragOffset = System.Numerics.Vector3.Zero; GhostYaw = 0; if (RenderLinks != null) { RenderLinks.Dispose(); RenderLinks = null; } }
    }
}