using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using Microsoft.Xna.Framework;

using ACE.DatLoader;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Entity;
using ACE.Server.Physics;
using ACE.Server.Physics.Common;
using ACE.Server.Physics.Util;

using ACViewer.Render;

namespace ACViewer
{
    public static class RotationHelper
    {
        public static ushort GetNextSafeRoomId(uint landblockId)
        {
            ushort maxId = 0x0FF; // Rooms start at 0x100
            uint prefix = landblockId & 0xFFFF0000;
            foreach (var id in DatManager.CellDat.AllFiles.Keys) {
                if ((id & 0xFFFF0000) == prefix) {
                    ushort low = (ushort)(id & 0xFFFF);
                    if (low >= 0x100 && low < 0xFFFF && low > maxId) maxId = low;
                }
            }
            return (ushort)(maxId + 1);
        }

        public static void RotateBuildingWithEnvironmentCells(ACE.DatLoader.Entity.BuildInfo buildInfo, Landblock lb, float degrees)
        {
            if (buildInfo == null || lb == null) return;
            var building = lb.Buildings.FirstOrDefault(b => b.SourceStab == buildInfo);
            if (building == null) return;
            var zRot = MathHelper.ToRadians(degrees);
            var additionalRotation = System.Numerics.Quaternion.CreateFromAxisAngle(System.Numerics.Vector3.UnitZ, zRot);
            var buildingOrigin = buildInfo.Frame.Origin;
            var newOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(buildInfo.Frame.Orientation, additionalRotation));
            buildInfo.Frame.Init(buildingOrigin, newOrientation);
            foreach (var cell in building.get_building_cells()) {
                var cellLocalPos = cell.Pos.Frame.Origin - buildingOrigin;
                var rotatedLocalPos = System.Numerics.Vector3.Transform(cellLocalPos, additionalRotation);
                var newCellPos = buildingOrigin + rotatedLocalPos;
                var newCellOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(cell.Pos.Frame.Orientation, additionalRotation));
                cell.Pos.Frame.Origin = newCellPos; cell.Pos.Frame.set_rotate(newCellOrientation);
                cell._envCell.Position.Init(newCellPos, newCellOrientation);
                if (cell.StaticObjects != null) foreach (var staticObj in cell.StaticObjects) {
                    var objLocalPos = staticObj.Position.Frame.Origin - buildingOrigin;
                    var rotatedObjPos = System.Numerics.Vector3.Transform(objLocalPos, additionalRotation);
                    var newObjPos = buildingOrigin + rotatedObjPos;
                    var newObjOrientation = System.Numerics.Quaternion.Normalize(System.Numerics.Quaternion.Multiply(staticObj.Position.Frame.Orientation, additionalRotation));
                    staticObj.Position.Frame.Origin = newObjPos; staticObj.Position.Frame.set_rotate(newObjOrientation);
                    if (staticObj.SourceStab is Stab stab) stab.Frame.Init(newObjPos, newObjOrientation);
                }
                MarkEnvCellAsModified(cell);
            }
        }

        public static void MoveBuildingWithEnvironmentCells(ACE.DatLoader.Entity.BuildInfo buildInfo, Landblock lb, System.Numerics.Vector3 newLocalPos)
        {
            if (buildInfo == null || lb == null) return;
            var building = lb.Buildings.FirstOrDefault(b => b.SourceStab == buildInfo);
            if (building == null) return;
            var delta = newLocalPos - buildInfo.Frame.Origin;
            buildInfo.Frame.Init(newLocalPos, buildInfo.Frame.Orientation);
            foreach (var cell in building.get_building_cells()) {
                var newCellPos = cell.Pos.Frame.Origin + delta;
                cell.Pos.Frame.Origin = newCellPos; cell._envCell.Position.Init(newCellPos, cell.Pos.Frame.Orientation);
                if (cell.StaticObjects != null) foreach (var staticObj in cell.StaticObjects) {
                    staticObj.Position.Frame.Origin += delta;
                    if (staticObj.SourceStab is Stab stab) stab.Frame.Init(staticObj.Position.Frame.Origin, staticObj.Position.Frame.Orientation);
                }
                MarkEnvCellAsModified(cell);
            }
        }

        public static void MigrateBuildingWithCells(ACE.DatLoader.Entity.BuildInfo buildInfo, Landblock sourceLb, Landblock targetLb, System.Numerics.Vector3 newLocalPos)
        {
            if (buildInfo == null || sourceLb == null || targetLb == null) return;
            var building = sourceLb.Buildings.FirstOrDefault(b => b.SourceStab == buildInfo);
            if (building == null) return;
            
            // STRICT ISOLATION: Get exact Shell ID (e.g. ABB4001C)
            uint shellCellId = building.CurCell?.ID ?? 0;
            building.ClearBuildingCells();
            var migratingRooms = building.get_building_cells().ToList();
            
            // Only move static objects that are explicitly in the same shell landcell
            var migratingObjects = sourceLb.StaticObjects.Where(o => o.CurCell?.ID == shellCellId && o.SourceStab is Stab).ToList();
            
            var delta = newLocalPos - buildInfo.Frame.Origin;
            ushort nextSafeId = GetNextSafeRoomId(targetLb.ID); // Avoid collisions
            
            var roomMap = new Dictionary<ushort, ushort>();
            var datFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            
            foreach (var cell in migratingRooms) {
                ushort oldLow = (ushort)(cell.ID & 0xFFFF); ushort newLow = nextSafeId++;
                roomMap[oldLow] = newLow; sourceLb.LandCells.TryRemove((int)oldLow, out _);
                uint oldFull = cell.ID; cell.ID = (targetLb.ID & 0xFFFF0000) | newLow;
                cell.Pos.Frame.Origin += delta; cell.Pos.ObjCellID = cell.ID;
                cell._envCell.Position.Init(cell.Pos.Frame.Origin, cell.Pos.Frame.Orientation);
                typeof(ACE.DatLoader.FileTypes.FileType).GetField("<Id>k__BackingField", datFlags)?.SetValue(cell._envCell, cell.ID);
                if (cell.StaticObjects != null) foreach (var so in cell.StaticObjects) { so.Position.Frame.Origin += delta; so.Position.ObjCellID = cell.ID; if (so.SourceStab is Stab s) s.Frame.Init(so.Position.Frame.Origin, so.Position.Frame.Orientation); }
                cell.CurLandblock = targetLb; targetLb.LandCells.TryAdd((int)newLow, cell);
                if (WorldViewer.Instance != null) {
                    WorldViewer.Instance.ModifiedEnvCells.Remove(oldFull);
                    WorldViewer.Instance.ModifiedEnvCells[cell.ID] = cell._envCell;
                    // WorldViewer.Instance.DeletedEnvCells.Add(oldFull);
                }
            }
            
            foreach (var obj in migratingObjects) {
                if (obj.SourceStab is Stab stab) {
                    sourceLb.Info.Objects.Remove(stab);
                    stab.Frame.Init(stab.Frame.Origin + delta, stab.Frame.Orientation);
                    if (targetLb.Info == null) { targetLb.Info = new LandblockInfo(); typeof(ACE.DatLoader.FileTypes.FileType).GetField("<Id>k__BackingField", datFlags)?.SetValue(targetLb.Info, targetLb.ID - 1); targetLb._landblock.HasObjects = true; }
                    targetLb.Info.Objects.Add(stab);
                }
            }

            foreach (var pRec in buildInfo.Portals) if (roomMap.TryGetValue(pRec.OtherCellId, out ushort nL)) typeof(ACE.DatLoader.Entity.CBldPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(pRec, nL);
            if (building.Portals != null) foreach (var lp in building.Portals) if (roomMap.TryGetValue(lp.OtherCellId, out ushort nL)) lp.OtherCellId = nL;
            
            foreach (var cell in migratingRooms) {
                if (cell._envCell.VisibleCells != null) {
                    var sanitizedVisible = new List<ushort>();
                    foreach (var vId in cell._envCell.VisibleCells) if (roomMap.TryGetValue(vId, out ushort vL)) sanitizedVisible.Add(vL);
                    cell._envCell.VisibleCells.Clear(); cell._envCell.VisibleCells.AddRange(sanitizedVisible);
                }
                if (cell._envCell.CellPortals != null) foreach (var cp in cell._envCell.CellPortals) {
                    if (cp.OtherCellId >= 0x100 && cp.OtherCellId < 0xFFFE) {
                        if (roomMap.TryGetValue(cp.OtherCellId, out ushort pL)) {
                            // VALIDATION: Ensure target room has a valid portal at this index to prevent crashes
                            if (targetLb.LandCells.TryGetValue(pL, out var targetCell) && targetCell is ACE.Server.Physics.Common.EnvCell tEnv && cp.OtherPortalId < tEnv._envCell.CellPortals.Count)
                                typeof(ACE.DatLoader.Entity.CellPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(cp, pL);
                            else
                                typeof(ACE.DatLoader.Entity.CellPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(cp, (ushort)0xFFFF);
                        } else
                            typeof(ACE.DatLoader.Entity.CellPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(cp, (ushort)0xFFFF);
                    }
                }
            }
            
            sourceLb.Info.Buildings.Remove(buildInfo); sourceLb.Buildings.Remove(building);
            if (targetLb.Info == null) { targetLb.Info = new LandblockInfo(); typeof(ACE.DatLoader.FileTypes.FileType).GetField("<Id>k__BackingField", datFlags)?.SetValue(targetLb.Info, targetLb.ID - 1); targetLb._landblock.HasObjects = true; targetLb.BlockInfoExists = true; }
            targetLb.Info.Buildings.Add(buildInfo); buildInfo.Frame.Init(newLocalPos, buildInfo.Frame.Orientation);
            building.set_cell_id((targetLb.ID & 0xFFFF0000) | (building.CurCell?.ID & 0xFFFF ?? 0)); targetLb.Buildings.Add(building);
            
            typeof(ACE.DatLoader.FileTypes.LandblockInfo).GetField("<NumCells>k__BackingField", datFlags)?.SetValue(sourceLb.Info, (uint)sourceLb.LandCells.Values.Count(c => (c.ID & 0xFFFF) >= 0x100));
            typeof(ACE.DatLoader.FileTypes.LandblockInfo).GetField("<NumCells>k__BackingField", datFlags)?.SetValue(targetLb.Info, (uint)targetLb.LandCells.Values.Count(c => (c.ID & 0xFFFF) >= 0x100));
            typeof(ACE.Server.Physics.Common.Landblock).GetField("envcells", datFlags)?.SetValue(sourceLb, null);
            typeof(ACE.Server.Physics.Common.Landblock).GetField("envcells", datFlags)?.SetValue(targetLb, null);
            sourceLb.IsInfoModified = true; targetLb.IsInfoModified = true;
            ForceLandblockReload(sourceLb.ID); ForceLandblockReload(targetLb.ID);
        }

        public static void ReindexLandblockCells(Landblock lb)
        {
            if (lb == null || lb.Info == null) return;
            var allRooms = lb.LandCells.Values.OfType<ACE.Server.Physics.Common.EnvCell>().Where(c => (c.ID & 0xFFFF) >= 0x100).OrderBy(c => c.ID).ToList();
            var keysToRemove = lb.LandCells.Keys.Where(k => k >= 0x100).ToList();
            foreach (var k in keysToRemove) lb.LandCells.TryRemove(k, out _);
            var lowMap = new Dictionary<ushort, ushort>();
            var datFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            for (int i = 0; i < allRooms.Count; i++) {
                var cell = allRooms[i]; ushort oldL = (ushort)(cell.ID & 0xFFFF); ushort newL = (ushort)(0x100 + i);
                lowMap[oldL] = newL; cell.ID = (lb.ID & 0xFFFF0000) | newL; cell.Pos.ObjCellID = cell.ID;
                typeof(ACE.DatLoader.FileTypes.FileType).GetField("<Id>k__BackingField", datFlags)?.SetValue(cell._envCell, cell.ID);
                lb.LandCells.TryAdd((int)newL, cell); MarkEnvCellAsModified(cell);
            }
            foreach (var cell in allRooms) {
                if (cell._envCell.VisibleCells != null) for (int j = 0; j < cell._envCell.VisibleCells.Count; j++) if (lowMap.TryGetValue(cell._envCell.VisibleCells[j], out ushort nV)) cell._envCell.VisibleCells[j] = nV;
                if (cell._envCell.CellPortals != null) foreach (var cp in cell._envCell.CellPortals) if (lowMap.TryGetValue(cp.OtherCellId, out ushort nP)) typeof(ACE.DatLoader.Entity.CellPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(cp, nP);
                typeof(ACE.Server.Physics.Common.EnvCell).GetField("PostInitted", datFlags)?.SetValue(cell, false);
            }
            foreach (var bld in lb.Info.Buildings) foreach (var p in bld.Portals) if (lowMap.TryGetValue(p.OtherCellId, out ushort nSD)) typeof(ACE.DatLoader.Entity.CBldPortal).GetField("<OtherCellId>k__BackingField", datFlags)?.SetValue(p, nSD);
            typeof(ACE.DatLoader.FileTypes.LandblockInfo).GetField("<NumCells>k__BackingField", datFlags)?.SetValue(lb.Info, (uint)allRooms.Count);
            typeof(ACE.Server.Physics.Common.Landblock).GetField("envcells", datFlags)?.SetValue(lb, null);
            lb.IsInfoModified = true; if (WorldViewer.Instance != null) WorldViewer.Instance.ModifiedLandblocks[lb.ID] = lb;
        }

        public static void ForceLandblockReload(uint landblockId)
        {
            var instance = WorldViewer.Instance; if (instance == null) return;
            LScape.Landblocks.TryRemove(landblockId, out var oldLbObj);
            if (oldLbObj is Landblock oldLb) { oldLb.destroy_static_objects(); oldLb.destroy_buildings(); }
            if (instance.ModifiedLandblocks.TryGetValue(landblockId, out var cachedLb)) {
                if (cachedLb.LandCells == null || cachedLb.LandCells.Count < 64) { if (cachedLb.Polygons == null) cachedLb.InitPVArrays(); cachedLb.ConstructVertices(); cachedLb.ConstructPolygons(cachedLb.ID); }
                cachedLb.destroy_static_objects(); cachedLb.destroy_buildings();
                foreach (var c in cachedLb.get_envcells()) c.destroy_static_objects();
                cachedLb.init_buildings(); cachedLb.init_static_objs();
                foreach (var c in cachedLb.get_envcells()) c.init_static_objects();
                LScape.Landblocks.TryAdd(landblockId, cachedLb);
            } else LScape.get_landblock(landblockId);
            RefreshVisuals();
        }

        private static void RefreshVisuals() {
            var instance = WorldViewer.Instance; if (instance?.Buffer == null) return;
            instance.Buffer.ClearBuffer();
            foreach (var lb in LScape.Landblocks.Values.OfType<Landblock>().OrderBy(l => l.ID)) instance.Buffer.AddOutdoor(new R_Landblock(lb));
            if (instance.PlayerMode && instance.Player != null) instance.Buffer.AddPlayer(new R_PhysicsObj(instance.Player.PhysicsObj));
            instance.Buffer.BuildBuffers();
        }

        public static void MigrateObject(ACE.DatLoader.Entity.Stab stab, Landblock sourceLb, Landblock targetLb)
        {
            sourceLb.Info.Objects.Remove(stab);
            var datFlags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            if (targetLb.Info == null) { targetLb.Info = new LandblockInfo(); typeof(ACE.DatLoader.FileTypes.FileType).GetField("<Id>k__BackingField", datFlags)?.SetValue(targetLb.Info, targetLb.ID - 1); targetLb._landblock.HasObjects = true; targetLb.IsModified = true; }
            targetLb.Info.Objects.Add(stab); sourceLb.IsInfoModified = true; targetLb.IsInfoModified = true;
            ForceLandblockReload(sourceLb.ID); ForceLandblockReload(targetLb.ID);
        }

        public static void MarkEnvCellAsModified(ACE.Server.Physics.Common.EnvCell envCell) { if (envCell != null && WorldViewer.Instance != null) WorldViewer.Instance.ModifiedEnvCells[envCell.ID] = envCell._envCell; }

        public static void UpdateVisualFeedback(PhysicsObj obj) {
            if (obj?.PartArray == null) return;
            foreach (var part in obj.PartArray.Parts) if (part.Buffer != null) part.Buffer.UpdateInstance(part.BufferIdx, new Microsoft.Xna.Framework.Vector3(part.Pos.GetWorldPos().X, part.Pos.GetWorldPos().Y, part.Pos.GetWorldPos().Z), part.Pos.Frame.Orientation.ToXna(), new Microsoft.Xna.Framework.Vector3(part.GfxObjScale.X, part.GfxObjScale.Y, part.GfxObjScale.Z));
        }

        public static bool InjectRecord(FileStream fs, uint nodeByteOffset, uint id, uint dataByteOffset, uint size, uint blockSize, out uint newNodeFirstId)
        {
            newNodeFirstId = 0; if (nodeByteOffset == 0 || id == 0) return false;
            byte[] nodeData = ReadLogical(fs, nodeByteOffset, 2048, blockSize);
            uint entryCount = BitConverter.ToUInt32(nodeData, 248);
            if (BitConverter.ToUInt32(nodeData, 0) == 0) { // Leaf
                if (entryCount >= 74) return false;
                int insertIdx = 0; while (insertIdx < (int)entryCount && id > BitConverter.ToUInt32(nodeData, 252 + insertIdx * 24 + 4)) insertIdx++;
                uint insertLogicalOffset = 252 + (uint)(insertIdx * 24);
                uint bytesToMove = (entryCount - (uint)insertIdx) * 24;
                if (insertLogicalOffset + bytesToMove > 2048) return false;
                if (bytesToMove > 0) { byte[] moveData = new byte[bytesToMove]; Array.Copy(nodeData, (int)insertLogicalOffset, moveData, 0, (int)bytesToMove); SafeWriteLogicalAt(fs, nodeByteOffset, insertLogicalOffset + 24, moveData, blockSize); }
                byte[] newEntry = new byte[24]; using (var ems = new MemoryStream(newEntry)) using (var writer = new BinaryWriter(ems)) { writer.Write((uint)0x00030000); writer.Write(id); writer.Write(dataByteOffset); writer.Write(size); writer.Write((uint)0); writer.Write((uint)1000); }
                SafeWriteLogicalAt(fs, nodeByteOffset, insertLogicalOffset, newEntry, blockSize);
                SafeWriteLogicalAt(fs, nodeByteOffset, 248, BitConverter.GetBytes(entryCount + 1), blockSize);
                newNodeFirstId = (insertIdx == 0) ? id : BitConverter.ToUInt32(nodeData, 252 + 4);
                return true;
            }
            int branchIdx = 0; for (int i = 0; i < (int)entryCount; i++) { if (id < BitConverter.ToUInt32(nodeData, 252 + i * 24 + 4)) break; branchIdx = i + 1; }
            uint[] branches = new uint[62]; for (int j = 0; j < 62; j++) branches[j] = BitConverter.ToUInt32(nodeData, j * 4);
            uint nextNodeFirstId; if (InjectRecord(fs, branches[branchIdx] * 0x100, id, dataByteOffset, size, blockSize, out nextNodeFirstId)) {
                byte[] updatedData = ReadLogical(fs, nodeByteOffset, 2048, blockSize);
                if (branchIdx > 0 && nextNodeFirstId != BitConverter.ToUInt32(updatedData, 252 + (branchIdx - 1) * 24 + 4)) SafeWriteLogicalAt(fs, nodeByteOffset, (uint)(252 + (branchIdx - 1) * 24 + 4), BitConverter.GetBytes(nextNodeFirstId), blockSize);
                newNodeFirstId = (branchIdx == 0) ? nextNodeFirstId : BitConverter.ToUInt32(updatedData, 252 + 4);
                return true;
            }
            return false;
        }

        public static void SafeWriteLogicalAt(FileStream stream, uint startByteOffset, uint logicalOffset, byte[] data, uint blockSize)
        {
            uint currentBlock = startByteOffset; uint bytesToSkip = logicalOffset; uint dataInBlock = blockSize - 4;
            while (bytesToSkip >= dataInBlock) {
                stream.Seek(currentBlock, SeekOrigin.Begin); byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4);
                uint nextBlockIndex = BitConverter.ToUInt32(ptrBytes, 0); if (nextBlockIndex == 0) throw new InvalidOperationException("Chain too short.");
                currentBlock = nextBlockIndex * 0x100; bytesToSkip -= dataInBlock;
            }
            int dataIndex = 0; while (dataIndex < data.Length) {
                stream.Seek(currentBlock, SeekOrigin.Begin); byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4); uint nextBlock = BitConverter.ToUInt32(ptrBytes, 0) * 0x100;
                uint physicalOffset = currentBlock + 4 + bytesToSkip; int toWrite = Math.Min(data.Length - dataIndex, (int)(dataInBlock - bytesToSkip));
                stream.Seek(physicalOffset, SeekOrigin.Begin); stream.Write(data, dataIndex, toWrite);
                dataIndex += toWrite; bytesToSkip = 0; if (dataIndex < data.Length) currentBlock = nextBlock;
            }
        }

        public static byte[] ReadLogical(FileStream stream, uint startByteOffset, uint size, uint blockSize)
        {
            byte[] result = new byte[size]; int resultIdx = 0; uint currentBlock = startByteOffset;
            while (resultIdx < (int)size) {
                stream.Seek(currentBlock, SeekOrigin.Begin);
                byte[] ptrBytes = new byte[4]; stream.Read(ptrBytes, 0, 4); uint nextAddr = BitConverter.ToUInt32(ptrBytes, 0) * 0x100;
                int toRead = Math.Min((int)size - resultIdx, (int)blockSize - 4);
                stream.Read(result, resultIdx, toRead); resultIdx += toRead;
                if (resultIdx < (int)size) { if (nextAddr == 0) break; currentBlock = nextAddr; }
            }
            return result;
        }
    }
}