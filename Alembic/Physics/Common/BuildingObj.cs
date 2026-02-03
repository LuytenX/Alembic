using System.Collections.Generic;
using System.Linq;

using ACE.DatLoader.Entity;
using ACE.Server.Physics.Animation;

namespace ACE.Server.Physics.Common
{
    public class BuildingObj : PhysicsObj
    {
        public List<EnvCell> BuildingCells;
        public List<BldPortal> Portals;
        public List<PartCell> LeafCells;
        public List<ShadowPart> ShadowList;
        public uint NumLeaves;

        public uint LandblockID { get => CurCell != null ? (CurCell.ID | 0xFFFF) : 0; }

        public BuildingObj() : base()
        {
            Portals = new List<BldPortal>();
            LeafCells = new List<PartCell>();
            ShadowList = new List<ShadowPart>();
        }

        public void add_to_cell(SortCell newCell)
        {
            newCell.add_building(this);
            set_cell_id(newCell.ID);
            CurCell = newCell;
        }

        public void add_to_stablist(ref List<ushort> blockStabList, ref uint maxSize, ref uint stabNum)
        {
            foreach (var portal in Portals)
                portal.add_to_stablist(ref blockStabList, ref maxSize, ref stabNum);
        }

        public TransitionState find_building_collisions(Transition transition)
        {
            if (PartArray == null)
                return TransitionState.OK;

            transition.SpherePath.BuildingCheck = true;
            var transitionState = PartArray.Parts[0].FindObjCollisions(transition, 0);
            transition.SpherePath.BuildingCheck = false;

            if (transitionState != TransitionState.OK && !transition.ObjectInfo.State.HasFlag(ObjectInfoState.Contact))
                transition.CollisionInfo.CollidedWithEnvironment = true;

            return transitionState;
        }

        public void find_building_transit_cells(Position pos, int numSphere, List<Sphere> sphere, CellArray cellArray, SpherePath path)
        {
            foreach (var portal in Portals)
            {
                var otherCell = portal.GetOtherCell(CurCell.ID);
                if (otherCell != null)
                    otherCell.check_building_transit(portal.OtherPortalId, pos, numSphere, sphere, cellArray, path);
            }
        }

        public void find_building_transit_cells(int numParts, List<PhysicsPart> parts, CellArray cellArray)
        {
            foreach (var portal in Portals)
            {
                var otherCell = portal.GetOtherCell(CurCell.ID);
                if (otherCell != null)
                    otherCell.check_building_transit(portal.OtherPortalId, numParts, parts, cellArray);
            }
        }

        public void ClearBuildingCells()
        {
            BuildingCells = null;
        }

        public List<EnvCell> get_building_cells()
        {
            if (BuildingCells != null) return BuildingCells;

            BuildingCells = new List<EnvCell>();

            // entry points into the building,
            // aka cells touching the outdoor landblock
            foreach (var portal in Portals)
            {
                var entrypoint = portal.GetOtherCell(LandblockID);
                add_cells_recursive(entrypoint);
            }
            return BuildingCells;
        }

        public void add_cells_recursive(EnvCell cell)
        {
            if (cell == null || BuildingCells.Contains(cell)) return;

            // TREE ISOLATION: Only move cells that belong to this landblock
            if ((cell.ID >> 16) != (LandblockID >> 16)) return;

            BuildingCells.Add(cell);

            // Follow Visiblity
            if (cell.VisibleCells != null) {
                foreach (var visibleCell in cell.VisibleCells.Values)
                    add_cells_recursive(visibleCell);
            }

            // Follow Portals (CRITICAL: captures rooms around corners)
            if (cell._envCell?.CellPortals != null) {
                foreach (var portal in cell._envCell.CellPortals) {
                    if (portal.OtherCellId >= 0x100 && portal.OtherCellId < 0xFFFE) {
                        uint neighborId = (LandblockID & 0xFFFF0000) | portal.OtherCellId;
                        var neighbor = LScape.get_landcell(neighborId) as EnvCell;
                        if (neighbor != null) add_cells_recursive(neighbor);
                    }
                }
            }
        }

        public PhysicsObj get_object(int objectID)
        {
            // visited cells?
            return null;
        }

        public static BuildingObj makeBuilding(uint buildingID, List<CBldPortal> portals, uint numLeaves)
        {
            var building = new BuildingObj();

            if (!building.InitObjectBegin(0, false) || !building.InitPartArrayObject(buildingID, true))
                return null;

            building.ID = buildingID;

            building.NumLeaves = numLeaves;
            building.LeafCells = new List<PartCell>();
            for (var i = 0; i < numLeaves; i++)
                building.LeafCells.Add(null);

            building.Portals = new List<BldPortal>();
            foreach (var portal in portals)
                building.Portals.Add(new BldPortal(portal));

            if (!building.InitObjectEnd())
                return null;

            return building;
        }

        public float GetMinZ()
        {
            get_building_cells();

            var minZ = float.MaxValue;

            foreach (var buildingCell in BuildingCells.Where(i => i.Environment != null))
            {
                if (buildingCell.Environment == null) continue;

                foreach (var cellStruct in buildingCell.Environment.Cells.Values)
                {
                    foreach (var vertex in cellStruct.VertexArray.Vertices.Values)
                        if (vertex.Origin.Z < minZ)
                            minZ = vertex.Origin.Z;
                }
            }
            return minZ;
        }

        public void remove()
        {
            if (CurCell == null) return;
            
            var sortCell = (SortCell)CurCell;
            sortCell.remove_building();
            set_cell_id(0);
            CurCell = null;
        }
    }
}
