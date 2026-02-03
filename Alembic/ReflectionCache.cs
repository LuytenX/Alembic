using System;
using System.Collections.Generic;
using System.Reflection;
using ACE.DatLoader.Entity;
using ACE.DatLoader.FileTypes;
using ACE.Server.Physics.Common;

namespace ACViewer
{
    public static class ReflectionCache
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;
        private static readonly BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;

        // ACE.DatLoader.FileTypes.FileType
        public static readonly FieldInfo FileTypeIdField = typeof(FileType).GetField("<Id>k__BackingField", PrivateInstance);

        // ACE.DatLoader.FileTypes.EnvCell
        public static readonly FieldInfo EnvCellCellStructureField = typeof(ACE.DatLoader.FileTypes.EnvCell).GetField("<CellStructure>k__BackingField", PrivateInstance);

        // ACE.DatLoader.FileTypes.EnvCell
        public static readonly FieldInfo EnvCellEnvironmentIdField = typeof(ACE.DatLoader.FileTypes.EnvCell).GetField("<EnvironmentId>k__BackingField", PrivateInstance);

        // ACE.DatLoader.Entity.CBldPortal
        public static readonly FieldInfo BldPortalOtherCellIdField = typeof(CBldPortal).GetField("<OtherCellId>k__BackingField", PrivateInstance);

        // ACE.DatLoader.Entity.CellPortal
        public static readonly FieldInfo CellPortalOtherCellIdField = typeof(CellPortal).GetField("<OtherCellId>k__BackingField", PrivateInstance);

        // ACE.Server.Physics.Common.EnvCell
        public static readonly FieldInfo EnvCellCurrentlyBuildingField = typeof(ACE.Server.Physics.Common.EnvCell).GetField("_currentlyBuilding", PrivateStatic);
        public static readonly FieldInfo EnvCellPostInittedField = typeof(ACE.Server.Physics.Common.EnvCell).GetField("_postInitted", PrivateInstance);

        // ACE.DatLoader.FileTypes.LandblockInfo
        public static readonly FieldInfo LandblockInfoNumCellsField = typeof(LandblockInfo).GetField("<NumCells>k__BackingField", PrivateInstance);

        // ACE.DatLoader.Entity.Stab
        public static readonly FieldInfo StabIdField = typeof(Stab).GetField("<Id>k__BackingField", PrivateInstance);

        // ACE.Server.Physics.Common.Landblock
        public static readonly FieldInfo LandblockEnvCellsField = typeof(Landblock).GetField("envcells", PrivateInstance);
        public static readonly FieldInfo LandblockBuildingsField = typeof(Landblock).GetField("<Buildings>k__BackingField", PublicInstance | PrivateInstance);
        public static readonly FieldInfo LandblockStaticObjectsField = typeof(Landblock).GetField("<StaticObjects>k__BackingField", PublicInstance | PrivateInstance);

        // ACE.DatLoader.DatFile
        public static readonly FieldInfo DatFileIterationField = typeof(ACE.DatLoader.DatFile).GetField("<Iteration>k__BackingField", PrivateInstance);
        public static readonly FieldInfo DatFileFileSizeField = typeof(ACE.DatLoader.DatFile).GetField("<FileSize>k__BackingField", PrivateInstance);
        public static readonly FieldInfo DatFileFileOffsetField = typeof(ACE.DatLoader.DatFile).GetField("<FileOffset>k__BackingField", PrivateInstance);
    }
}
