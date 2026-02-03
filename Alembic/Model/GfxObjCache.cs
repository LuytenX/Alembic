using System;
using System.Collections.Generic;

namespace ACViewer.Model
{
    public static class GfxObjCache
    {
        public static Dictionary<uint, GfxObj> Cache { get; set; }

        static GfxObjCache()
        {
            Init();
        }

        public static void Init()
        {
            Cache = new Dictionary<uint, GfxObj>();
        }

        public static GfxObj Get(uint gfxObjID)
        {
            // Resolve SetupModel (0x02) to its first GfxObj part
            if ((gfxObjID >> 24) == 0x02)
            {
                var setup = ACE.DatLoader.DatManager.PortalDat.ReadFromDat<ACE.DatLoader.FileTypes.SetupModel>(gfxObjID);
                if (setup != null && setup.Parts.Count > 0)
                    gfxObjID = setup.Parts[0];
            }

            if (!Cache.TryGetValue(gfxObjID, out var gfxObj))
            {
                //Console.WriteLine($"- Loading {gfxObjID:X8}");
                gfxObj = new GfxObj(gfxObjID);
                Cache.Add(gfxObjID, gfxObj);
            }
            return gfxObj;
        }
    }
}
