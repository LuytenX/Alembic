using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using Microsoft.Win32;

namespace ACViewer
{
    public static class MapGenerator
    {
        private const uint BLOCK_SIZE = 256;

        private class RecordInfo {
            public uint Id;
            public uint FileOffset;
            public uint Size;
        }

        public static async Task CreateNewMap()
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                Filter = "DAT Files (*.dat)|*.dat",
                FileName = "client_cell_1.dat",
                Title = "Create New Blank Map"
            };

            if (sfd.ShowDialog() != true) return;

            string path = sfd.FileName;
            WorldViewer.MainWindow.AddStatusText($"Generating pointer-correct production world...");

            await Task.Run(() =>
            {
                try {
                    List<RecordInfo> allRecords = new List<RecordInfo>();
                    using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
                    using (var writer = new BinaryWriter(fs)) {
                        // 1. RESERVE HEADER
                        writer.Write(new byte[0x140]);

                        // 2. DUMP GLOBAL RECORDS
                        byte[] iterData = new byte[12];
                        using (var ms = new MemoryStream(iterData))
                        using (var iw = new BinaryWriter(ms)) { iw.Write(982); iw.Write(-982); iw.Write(982); }
                        allRecords.Add(DumpRecord(fs, 0xFFFF0001, iterData, 10));

                        // 3. DUMP FULL GRID (X: 0-254, Y: 0-254)
                        for (uint x = 0; x <= 254; x++) {
                            if (x % 10 == 0) WorldViewer.MainWindow.Dispatcher.Invoke(() => WorldViewer.MainWindow.AddStatusText($"Dumping coordinate X={x}/254..."));
                            for (uint y = 0; y <= 254; y++) {
                                uint lbid = (x << 24) | (y << 16) | 0xFFFF;
                                allRecords.Add(DumpRecord(fs, lbid, GenerateFlatLandblock(lbid), 8));
                                
                                uint infoId = lbid - 1;
                                allRecords.Add(DumpRecord(fs, infoId, GenerateMinimalInfo(infoId), 1));
                            }
                        }

                        // 4. BUILD BALANCED B-TREE (BOTTOM-UP)
                        WorldViewer.MainWindow.Dispatcher.Invoke(() => WorldViewer.MainWindow.AddStatusText("Building balanced index..."));
                        var sorted = allRecords.OrderBy(r => r.Id).ToList();
                        uint rootOffset = BuildBTreeBottomUp(fs, sorted, BLOCK_SIZE);

                        // 5. FINALIZE HEADER
                        uint finalSize = (uint)fs.Length;
                        fs.Seek(0x140, SeekOrigin.Begin);
                        writer.Write(0x5442u);       // BT
                        writer.Write(BLOCK_SIZE);    // 256
                        writer.Write(finalSize);     // physical file size
                        writer.Write(2u);            // Cell
                        writer.Write(1u);            // subset
                        writer.Write(0u); writer.Write(0u); writer.Write(0u);
                        writer.Write(rootOffset);    // ROOT BYTE OFFSET
                    }
                    WorldViewer.MainWindow.Dispatcher.Invoke(() => WorldViewer.MainWindow.AddStatusText("Industrial map foundation complete!"));
                } catch (Exception ex) {
                    WorldViewer.MainWindow.Dispatcher.Invoke(() => WorldViewer.MainWindow.AddStatusText($"Error: {ex.Message}"));
                }
            });
        }

        private static RecordInfo DumpRecord(FileStream fs, uint id, byte[] data, int forcedBlocks)
        {
            long startOffset = (fs.Length + (BLOCK_SIZE - 1)) / BLOCK_SIZE * BLOCK_SIZE;
            uint fileOffset = (uint)startOffset;
            fs.Seek(startOffset, SeekOrigin.Begin);
            var writer = new BinaryWriter(fs);
            int dataWritten = 0;
            for (int i = 0; i < forcedBlocks; i++) {
                uint currentBlockStart = (uint)(startOffset + i * BLOCK_SIZE);
                int payloadSize = (int)BLOCK_SIZE - 4;
                
                // POINTER TO NEXT BLOCK HEADER (BYTE OFFSET)
                if (i < forcedBlocks - 1) writer.Write(currentBlockStart + BLOCK_SIZE);
                else writer.Write(0u);
                
                int toWrite = Math.Min(data.Length - dataWritten, payloadSize);
                if (toWrite > 0) { writer.Write(data, dataWritten, toWrite); dataWritten += toWrite; }
                
                int padding = payloadSize - (toWrite > 0 ? toWrite : 0);
                if (padding > 0) writer.Write(new byte[padding]);
            }
            return new RecordInfo { Id = id, FileOffset = fileOffset, Size = (uint)data.Length };
        }

        private static uint BuildBTreeBottomUp(FileStream fs, List<RecordInfo> records, uint blockSize)
        {
            // Redundant B-Tree: Every record is in a leaf. 
            // Branch nodes contain copies of records to act as separator keys.
            
            List<BTreeNodeInfo> currentLevel = new List<BTreeNodeInfo>();

            // 1. Create Leaf Nodes
            for (int i = 0; i < records.Count; i += 61) {
                int count = Math.Min(61, records.Count - i);
                var leafRecords = records.GetRange(i, count);
                
                byte[] nodeData = new byte[1716];
                Array.Copy(BitConverter.GetBytes(count), 0, nodeData, 248, 4); // EntryCount
                for (int j = 0; j < count; j++) {
                    var r = leafRecords[j];
                    int eOff = 252 + j * 24;
                    using (var ms = new MemoryStream()) using (var bw = new BinaryWriter(ms)) {
                        bw.Write(0x00030000u); bw.Write(r.Id); bw.Write(r.FileOffset); bw.Write(r.Size); bw.Write(0u); bw.Write(1000u);
                        Array.Copy(ms.ToArray(), 0, nodeData, eOff, 24);
                    }
                }
                long startOff = (fs.Length + (blockSize - 1)) / blockSize * blockSize;
                WriteLogicalNode(fs, (uint)startOff, nodeData, blockSize);
                currentLevel.Add(new BTreeNodeInfo { Offset = (uint)startOff, FirstRecord = leafRecords[0] });
            }

            // 2. Build Branch Levels
            while (currentLevel.Count > 1) {
                List<BTreeNodeInfo> nextLevel = new List<BTreeNodeInfo>();
                for (int i = 0; i < currentLevel.Count; i += 62) {
                    int childrenCount = Math.Min(62, currentLevel.Count - i);
                    int entryCount = childrenCount - 1;
                    
                    byte[] nodeData = new byte[1716];
                    Array.Copy(BitConverter.GetBytes(entryCount), 0, nodeData, 248, 4);
                    
                    for (int j = 0; j < childrenCount; j++) {
                        // Write branch pointer
                        Array.Copy(BitConverter.GetBytes(currentLevel[i + j].Offset), 0, nodeData, j * 4, 4);
                        
                        // Write separator (copy of the first record of the child to the right)
                        if (j > 0) {
                            var r = currentLevel[i + j].FirstRecord;
                            int eOff = 252 + (j - 1) * 24;
                            using (var ms = new MemoryStream()) using (var bw = new BinaryWriter(ms)) {
                                bw.Write(0x00030000u); bw.Write(r.Id); bw.Write(r.FileOffset); bw.Write(r.Size); bw.Write(0u); bw.Write(1000u);
                                Array.Copy(ms.ToArray(), 0, nodeData, eOff, 24);
                            }
                        }
                    }
                    long startOff = (fs.Length + (blockSize - 1)) / blockSize * blockSize;
                    WriteLogicalNode(fs, (uint)startOff, nodeData, blockSize);
                    nextLevel.Add(new BTreeNodeInfo { Offset = (uint)startOff, FirstRecord = currentLevel[i].FirstRecord });
                }
                currentLevel = nextLevel;
            }
            return currentLevel[0].Offset;
        }

        private class BTreeNodeInfo {
            public uint Offset;
            public RecordInfo FirstRecord;
        }

        private static void WriteLogicalNode(FileStream fs, uint startOff, byte[] data, uint blockSize) {
            fs.Seek(startOff, SeekOrigin.Begin);
            int written = 0;
            while (written < data.Length) {
                uint currentBlockStart = (uint)fs.Position;
                int payloadSize = (int)blockSize - 4;
                if (written + payloadSize < data.Length) fs.Write(BitConverter.GetBytes(currentBlockStart + blockSize), 0, 4);
                else fs.Write(BitConverter.GetBytes(0u), 0, 4);
                int toW = Math.Min(data.Length - written, payloadSize);
                fs.Write(data, written, toW); written += toW;
                int pad = payloadSize - toW; if (pad > 0) fs.Write(new byte[pad], 0, pad);
            }
        }

        private static byte[] GenerateFlatLandblock(uint id) {
            using (var ms = new MemoryStream()) using (var writer = new BinaryWriter(ms)) {
                writer.Write(id); writer.Write(0u);
                for (int i = 0; i < 81; i++) writer.Write((ushort)0x0003); // Grass
                for (int i = 0; i < 81; i++) writer.Write((byte)0);      // Flat
                while (ms.Position % 4 != 0) writer.Write((byte)0);
                return ms.ToArray();
            }
        }

        private static byte[] GenerateMinimalInfo(uint id) {
            using (var ms = new MemoryStream()) using (var writer = new BinaryWriter(ms)) {
                writer.Write(id); writer.Write(0u); writer.Write(0u); writer.Write((ushort)0); writer.Write((ushort)0);
                return ms.ToArray();
            }
        }
    }
}