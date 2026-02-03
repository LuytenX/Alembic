using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using ACE.Entity.Enum;
using ACE.Server.Physics.BSP;
using ACE.Server.Physics.Animation;
using ACE.Server.Physics.Collision;
using ACE.Server.Physics.Extensions;

using ACViewer;
using ACViewer.Enum;

namespace ACE.Server.Physics.Common
{
    public class EnvCell: ObjCell, IEquatable<EnvCell>
    {
        public int NumSurfaces;
        //public List<Surface> Surfaces;
        public CellStruct CellStructure;
        public uint CellStructureID;
        //public Environment Env;
        public int NumPortals;
        public List<DatLoader.Entity.CellPortal> Portals;
        public int NumStaticObjects;
        public List<uint> StaticObjectIDs;
        public List<AFrame> StaticObjectFrames;
        public List<PhysicsObj> StaticObjects;
        public List<ushort> LightArray;
        public int InCellTimestamp;
        public List<ushort> VisibleCellIDs;
        public new Dictionary<uint, EnvCell> VisibleCells;
        public EnvCellFlags Flags;
        public uint EnvironmentID;
        public DatLoader.FileTypes.EnvCell _envCell;
        public DatLoader.FileTypes.Environment Environment;

        public ACViewer.Render.InstanceBatch Buffer { get; set; }
        public int BufferIdx { get; set; }

        public EnvCell() : base()
        {
            Init();
        }

        public EnvCell(DatLoader.FileTypes.EnvCell envCell): base()
        {
            _envCell = envCell;

            Flags = envCell.Flags;
            ID = envCell.Id;
            ShadowObjectIDs = envCell.Surfaces;
            Pos = new Position(ID, new AFrame(envCell.Position));
            Portals = envCell.CellPortals;
            NumPortals = Portals.Count;
            StaticObjectIDs = new List<uint>();
            StaticObjectFrames = new List<AFrame>();
            NumStaticObjects = envCell.StaticObjects.Count;
            foreach (var staticObj in envCell.StaticObjects)
            {
                StaticObjectIDs.Add(staticObj.Id);
                StaticObjectFrames.Add(new AFrame(staticObj.Frame));
            }
            NumStabs = StaticObjectIDs.Count;
            VisibleCellIDs = envCell.VisibleCells;
            RestrictionObj = envCell.RestrictionObj;
            SeenOutside = envCell.SeenOutside;

            EnvironmentID = envCell.EnvironmentId;

            if (EnvironmentID != 0)
                Environment = DBObj.GetEnvironment(EnvironmentID);

            CellStructureID = envCell.CellStructure;    // environment can contain multiple?

            if (Environment?.Cells != null && Environment.Cells.TryGetValue(CellStructureID, out var cellStruct))
                CellStructure = new CellStruct(cellStruct);

            NumSurfaces = envCell.Surfaces.Count;
        }

        private bool _postInitted = false;
        public void PostInit()
        {
            if (_postInitted || ID == 0) return;
            _postInitted = true;

            // RE-INITIALIZE GEOMETRY: Ensure the room's physical structure is correctly linked
            // to the (possibly new) EnvironmentID and CellStructureID after re-indexing.
            if (EnvironmentID != 0)
                Environment = DBObj.GetEnvironment(EnvironmentID);

            // After re-indexing, the CellStructureID in the physics EnvCell might be out of sync with the updated _envCell
            // Update the CellStructureID to match the current _envCell.CellStructure value
            if (_envCell != null)
            {
                CellStructureID = _envCell.CellStructure; // Sync the CellStructureID with the updated _envCell
                System.Console.WriteLine($"[DEBUG] PostInit: Cell {ID:X8} has _envCell.CellStructure = {CellStructureID:X8}");
            }

            // Check if the current CellStructureID exists in the Environment.Cells
            if (Environment?.Cells != null && Environment.Cells.TryGetValue(CellStructureID, out var cellStruct))
            {
                CellStructure = new CellStruct(cellStruct);
            }
            else
            {
                // If the CellStructureID doesn't exist in Environment.Cells, this indicates a data integrity issue
                // This can happen after re-indexing where IDs have changed but environment data wasn't properly updated
                System.Console.WriteLine($"[WARNING] CellStructureID {CellStructureID:X8} not found in Environment.Cells after re-indexing. This indicates a data synchronization issue.");

                // For now, try to find any available CellStructure as a fallback, but log the issue
                if (Environment?.Cells != null)
                {
                    // Look for any available CellStructure in the environment
                    var firstCellStruct = Environment.Cells.Values.FirstOrDefault();
                    if (firstCellStruct != null)
                    {
                        System.Console.WriteLine($"[INFO] Using fallback CellStruct for cell ID {CellStructureID:X8}");
                        CellStructure = new CellStruct(firstCellStruct);
                    }
                    else
                    {
                        // If no CellStructure is available, we have a serious data integrity issue
                        System.Console.WriteLine($"[ERROR] No CellStruct available in Environment for cell ID {CellStructureID:X8}. Physics operations may fail.");
                        CellStructure = null; // Keep as null to maintain sensitivity to the issue
                    }
                }
                else
                {
                    // If no Environment is available, we have a serious data integrity issue
                    System.Console.WriteLine($"[ERROR] No Environment available for cell ID {CellStructureID:X8}. Physics operations may fail.");
                    CellStructure = null; // Keep as null to maintain sensitivity to the issue
                }
            }

            build_visible_cells();
            init_static_objects();
        }

        /// <summary>
        /// Validates that the cell structure is properly initialized to prevent runtime crashes
        /// </summary>
        public bool ValidateCellStructure()
        {
            return CellStructure != null;
        }

        /// <summary>
        /// Repairs the cell structure by ensuring Environment data is properly synchronized with CellStructureID
        /// This should be called after reindexing operations to maintain data integrity
        /// </summary>
        public void RepairCellStructure()
        {
            // First, ensure the CellStructureID is synced with the underlying _envCell
            if (_envCell != null)
            {
                CellStructureID = _envCell.CellStructure;
            }

            // Validate that we have valid IDs before attempting repair
            if (EnvironmentID == 0)
            {
                // System.Console.WriteLine($"[REPAIR] Cannot repair cell {ID:X8}, EnvironmentID is 0");
                return;
            }

            // Attempt to reload the environment and resynchronize the cell structure
            var freshEnvironment = DBObj.GetEnvironment(EnvironmentID);
            if (freshEnvironment?.Cells != null && freshEnvironment.Cells.TryGetValue(CellStructureID, out var cellStruct))
            {
                CellStructure = new CellStruct(cellStruct);
                System.Console.WriteLine($"[REPAIR] Successfully repaired CellStructure for cell ID {CellStructureID:X8}");
            }
            else
            {
                System.Console.WriteLine($"[REPAIR] Failed to repair CellStructure for cell ID {CellStructureID:X8}, still missing from Environment.Cells. CellStructureID={CellStructureID:X8}, EnvironmentID={EnvironmentID:X8}");

                // As a last resort, try to find any available CellStruct as a fallback
                if (freshEnvironment?.Cells != null)
                {
                    var firstCellStruct = freshEnvironment.Cells.Values.FirstOrDefault();
                    if (firstCellStruct != null)
                    {
                        CellStructure = new CellStruct(firstCellStruct);
                        System.Console.WriteLine($"[REPAIR] Used fallback CellStruct for cell ID {ID:X8}");
                    }
                }
            }
        }

        public override TransitionState FindEnvCollisions(Transition transition)
        {
            var transitState = check_entry_restrictions(transition);

            if (transitState != TransitionState.OK)
                return transitState;

            transition.SpherePath.ObstructionEthereal = false;

            // Add null check for CellStructure to prevent crashes
            if (CellStructure != null && CellStructure.PhysicsBSP != null)
            {
                transition.SpherePath.CacheLocalSpaceSphere(Pos, 1.0f);

                if (transition.SpherePath.InsertType == InsertType.InitialPlacement)
                    transitState = CellStructure.PhysicsBSP.placement_insert(transition);
                else
                    transitState = CellStructure.PhysicsBSP.find_collisions(transition, 1.0f);

                if (transitState != TransitionState.OK && !transition.ObjectInfo.State.HasFlag(ObjectInfoState.Contact))
                {
                    transition.CollisionInfo.CollidedWithEnvironment = true;

                    if (PhysicsObj.IsPicking)
                    {
                        // HitPoly set in BSPLeaf
                        Picker.PickResult.Type = PickType.EnvCell;
                        Picker.PickResult.ObjCell = this;
                    }
                }
            }
            return transitState;
        }

        public override TransitionState FindCollisions(Transition transition)
        {
            var transitionState = FindEnvCollisions(transition);
            if (transitionState == TransitionState.OK)
                transitionState = FindObjCollisions(transition);
            return transitionState;
        }

        private static HashSet<uint> _currentlyBuilding = new HashSet<uint>();
        public void build_visible_cells()
        {
            if (_currentlyBuilding.Contains(ID)) return;
            
            _currentlyBuilding.Add(ID);
            try
            {
                VisibleCells = new Dictionary<uint, EnvCell>();

                foreach (var visibleCellID in VisibleCellIDs)
                {
                    // CRITICAL: Check the same key (visibleCellID) that we are about to add
                    if (VisibleCells.ContainsKey(visibleCellID)) continue;
                    
                    var blockCellID = ID & 0xFFFF0000 | visibleCellID;
                    var cell = LScape.get_landcell(blockCellID);
                    if (cell is EnvCell envCell)
                    {
                        VisibleCells.Add(visibleCellID, envCell);
                    }
                }
            }
            finally
            {
                _currentlyBuilding.Remove(ID);
            }
        }

        public void check_building_transit(ushort portalId, Position pos, int numSphere, List<Sphere> spheres, CellArray cellArray, SpherePath path)
        {
            //if (portalId == 0) return;
            if (portalId == ushort.MaxValue) return;

            if (CellStructure == null || Pos == null || Pos.Frame == null) return;

            foreach (var sphere in spheres)
            {
                if (sphere == null) continue;
                var globSphere = new Sphere(Pos.Frame.GlobalToLocal(sphere.Center), sphere.Radius);
                if (CellStructure.sphere_intersects_cell(globSphere) == BoundingType.Outside)
                    continue;

                if (path != null)
                    path.HitsInteriorCell = true;

                cellArray.add_cell(ID, this);
            }
        }

        public void check_building_transit(int portalId, int numParts, List<PhysicsPart> parts, CellArray cellArray)
        {
            //if (portalId == 0) return;
            if (portalId == ushort.MaxValue) return;

            // SAFETY GUARD: Verify portalId is within valid range for both logical portals and physical geometry
            if (portalId < 0 || Portals == null || portalId >= Portals.Count || CellStructure == null || CellStructure.Portals == null || portalId >= CellStructure.Portals.Count)
                return;

            var portal = Portals[portalId];
            var portalPoly = CellStructure.Portals[portalId];

            foreach (var part in parts)
            {
                if (part == null) continue;

                Sphere boundingSphere = null;
                if (part.GfxObj.PhysicsSphere != null)
                    boundingSphere = part.GfxObj.PhysicsSphere;
                else
                    boundingSphere = part.GfxObj.DrawingSphere;

                if (boundingSphere == null) continue;

                var center = Pos.LocalToLocal(part.Pos, boundingSphere.Center);
                var rad = boundingSphere.Radius + PhysicsGlobals.EPSILON;

                var diff = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                if (portal.PortalSide)
                {
                    if (diff > rad) continue;
                }
                else
                {
                    if (diff < -rad) continue;
                }

                var box = new BBox();
                box.LocalToLocal(part.GetBoundingBox(), part.Pos, Pos);
                var intersect = portalPoly.Plane.intersect_box(box);
                if (intersect == Sidedness.Crossing || intersect == Sidedness.Positive && portal.PortalSide || intersect == Sidedness.Negative && !portal.PortalSide)
                {
                    if (!CellStructure.box_intersects_cell(box))
                        continue;

                    cellArray.add_cell(ID, this);
                    find_transit_cells(numParts, parts, cellArray);
                    return;
                }
            }
        }

        public ObjCell find_visible_child_cell(Vector3 origin, bool searchCells)
        {
            if (point_in_cell(origin))
                return this;

            if (searchCells)
            {
                foreach (var visibleCell in VisibleCells.Values)
                {
                    if (visibleCell == null) continue;

                    var envCell = GetVisible(visibleCell.ID & 0xFFFF);
                    if (envCell != null && envCell.point_in_cell(origin))
                        return envCell;
                }
            }
            else
            {
                foreach (var portal in Portals)
                {
                    var envCell = GetVisible(portal.OtherCellId);
                    if (envCell != null && envCell.point_in_cell(origin))
                        return envCell;
                }
            }
            return null;
        }

        public new EnvCell GetVisible(uint cellID)
        {
            if (VisibleCells == null) return null;
            EnvCell envCell = null;
            if (VisibleCells.TryGetValue(cellID, out envCell))
                return envCell;
            
            // Fallback: Check if we can find it in the global landscape
            var blockCellID = ID & 0xFFFF0000 | cellID;
            return LScape.get_landcell(blockCellID) as EnvCell;
        }

        public new void Init()
        {
            CellStructure = new CellStruct();
            StaticObjectIDs = new List<uint>();
            StaticObjectFrames = new List<AFrame>();
            //StaticObjects = new List<PhysicsObj>();
            VisibleCells = new Dictionary<uint, EnvCell>();
        }

        public EnvCell add_visible_cell(uint cellID)
        {
            if (VisibleCells == null) VisibleCells = new Dictionary<uint, EnvCell>();
            var envCell = DBObj.GetEnvCell(cellID);
            if (envCell != null)
                VisibleCells[cellID] = envCell;
            return envCell;
        }

        public override void find_transit_cells(int numParts, List<PhysicsPart> parts, CellArray cellArray)
        {
            // SAFETY CHECK: Ensure geometry is initialized before calculating transitions
            if (CellStructure == null || CellStructure.Polygons == null || Portals == null) return;

            var checkOutside = false;

            foreach (var portal in Portals)
            {
                if (!CellStructure.Polygons.TryGetValue(portal.PolygonId, out var portalPoly)) continue;

                foreach (var part in parts)
                {
                    if (part == null) continue;
                    var sphere = part.GfxObj.PhysicsSphere;
                    if (sphere == null)
                        sphere = part.GfxObj.DrawingSphere;
                    if (sphere == null)
                        continue;

                    var center = Pos.LocalToLocal(part.Pos, sphere.Center);
                    var rad = sphere.Radius + PhysicsGlobals.EPSILON;

                    var dist = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                    if (portal.PortalSide)
                    {
                        if (dist < -rad)
                            continue;
                    }
                    else
                    {
                        if (dist > rad)
                            continue;
                    }

                    var bbox = part.GetBoundingBox();
                    var box = new BBox();
                    box.LocalToLocal(bbox, part.Pos, Pos);
                    var sidedness = portalPoly.Plane.intersect_box(box);
                    if (sidedness == Sidedness.Positive && !portal.PortalSide || sidedness == Sidedness.Negative && portal.PortalSide)
                        continue;

                    if (portal.OtherCellId == ushort.MaxValue)
                    {
                        checkOutside = true;
                        break;
                    }

                    // LoadCells
                    var otherCell = GetVisible(portal.OtherCellId);
                    if (otherCell == null || otherCell.CellStructure == null)
                    {
                        if (otherCell == null) cellArray.add_cell(portal.OtherCellId, null);
                        break;
                    }

                    var cellBox = new BBox();
                    cellBox.LocalToLocal(bbox, part.Pos, otherCell.Pos);
                    if (otherCell.CellStructure.box_intersects_cell(cellBox))
                    {
                        cellArray.add_cell(otherCell.ID, otherCell);
                        break;
                    }
                }
            }
            if (checkOutside)
                LandCell.add_all_outside_cells(numParts, parts, cellArray, ID);
        }

        public override void find_transit_cells(Position position, int numSphere, List<Sphere> spheres, CellArray cellArray, SpherePath path)
        {
            // SAFETY CHECK: Ensure geometry is initialized before calculating transitions
            if (CellStructure == null || CellStructure.Polygons == null) return;

            var checkOutside = false;

            foreach (var portal in Portals)
            {
                if (!CellStructure.Polygons.TryGetValue(portal.PolygonId, out var portalPoly)) continue;

                if (portal.OtherCellId == ushort.MaxValue)
                {
                    foreach (var sphere in spheres)
                    {
                        var rad = sphere.Radius + PhysicsGlobals.EPSILON;
                        var center = Pos.Frame.GlobalToLocal(sphere.Center);

                        var dist = Vector3.Dot(center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                        if (dist > -rad && dist < rad)
                        {
                            checkOutside = true;
                            break;
                        }
                    }
                }
                else
                {
                    var otherCell = GetVisible(portal.OtherCellId);
                    if (otherCell != null && otherCell.CellStructure != null)
                    {
                        foreach (var sphere in spheres)
                        {
                            var center = otherCell.Pos.Frame.GlobalToLocal(sphere.Center);
                            var _sphere = new Sphere(center, sphere.Radius);

                            var boundingType = otherCell.CellStructure.sphere_intersects_cell(_sphere);
                            if (boundingType != BoundingType.Outside)
                            {
                                cellArray.add_cell(otherCell.ID, otherCell);
                                break;
                            }
                        }
                    }
                    else if (otherCell == null)
                    {
                        foreach (var sphere in spheres)
                        {
                            var center = Pos.Frame.GlobalToLocal(sphere.Center);
                            var _sphere = new Sphere(center, sphere.Radius + PhysicsGlobals.EPSILON);
                            var portalSide = portal.PortalSide;
                            var dist = Vector3.Dot(_sphere.Center, portalPoly.Plane.Normal) + portalPoly.Plane.D;
                            if (dist > -_sphere.Radius && portalSide || dist < _sphere.Radius && !portalSide)
                            {
                                cellArray.add_cell(portal.OtherCellId, null);
                                break;
                            }
                        }
                    }
                }
            }
            if (checkOutside)
                LandCell.add_all_outside_cells(position, numSphere, spheres, cellArray);
        }

        public void destroy_static_objects()
        {
            if (StaticObjects == null) return;
            foreach (var obj in StaticObjects)
                obj.leave_world();

            StaticObjects.Clear();
            StaticObjects = null;
        }

        public void init_static_objects()
        {
            if (StaticObjects != null)
            {
                foreach (var staticObj in StaticObjects)
                {
                    if (!staticObj.is_completely_visible())
                        staticObj.calc_cross_cells_static();
                }
            }
            else
            {
                StaticObjects = new List<PhysicsObj>();

                for (var i = 0; i < NumStaticObjects; i++)
                {
                    var staticObj = PhysicsObj.makeObject(StaticObjectIDs[i], 0, false);
                    staticObj.DatObject = true;
                    staticObj.SourceStab = _envCell.StaticObjects[i];
                    staticObj.add_obj_to_cell(this, StaticObjectFrames[i]);
                    if (staticObj.CurCell == null)
                    {
                        //Console.WriteLine($"EnvCell {ID:X8}: failed to add {staticObj.ID:X8}");
                        staticObj.DestroyObject();
                        continue;
                    }

                    StaticObjects.Add(staticObj);
                }

                //Console.WriteLine($"{ID:X8}: loaded {NumStaticObjects} static objects");
            }
        }

        public static ObjCell get_visible(uint cellID)
        {
            var cell = (EnvCell)LScape.get_landcell(cellID);
            return cell.VisibleCells.Values.First();
        }

        public void grab_visible(List<uint> stabs)
        {
            foreach (var stab in stabs)
                add_visible_cell(stab);
        }

        public override bool point_in_cell(Vector3 point)
        {
            // SAFETY CHECK: Ensure geometry is ready before testing position
            if (CellStructure == null) return false;

            var localPoint = Pos.Frame.GlobalToLocal(point);
            return CellStructure.point_in_cell(localPoint);
        }

        public void release_visible(List<uint> stabs)
        {
            foreach (var stab in stabs)
                VisibleCells.Remove(stab);
        }

        public override bool Equals(object obj)
        {
            if (obj is EnvCell envCell)
                return ID == envCell.ID;
            
            return false;
        }

        public bool Equals(EnvCell envCell)
        {
            if (envCell == null)
                return false;

            return ID == envCell.ID;
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public bool IsVisibleIndoors(ObjCell cell)
        {
            var blockDist = PhysicsObj.GetBlockDist(ID, cell.ID);

            // if landblocks equal
            if (blockDist == 0)
            {
                // check env VisibleCells
                var cellID = cell.ID & 0xFFFF;
                if (VisibleCells.ContainsKey(cellID))
                    return true;
            }
            return SeenOutside && blockDist <= 1;
        }

        public override bool handle_move_restriction(Transition transition)
        {
            return true;
        }
    }
}
