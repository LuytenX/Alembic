using System;
using System.IO;

namespace ACE.DatLoader
{
    public class DatReader
    {
        public byte[] Buffer { get; }

        public DatReader(string datFilePath, uint offset, uint size, uint blockSize)
        {
            using (var stream = new FileStream(datFilePath, FileMode.Open, FileAccess.Read))
            {
                Buffer = ReadDat(stream, offset, size, blockSize);

                stream.Close();
            }
        }

        public DatReader(FileStream stream, uint offset, uint size, uint blockSize)
        {
            Buffer = ReadDat(stream, offset, size, blockSize);
        }

        private static byte[] ReadDat(FileStream stream, uint offset, uint size, uint blockSize)
        {
            var buffer = new byte[size];

            stream.Seek(offset, SeekOrigin.Begin);

            // Dat "file" is broken up into sectors that are not neccessarily congruous. Next address is stored in first four bytes of each sector.
            uint nextAddress = GetNextAddress(stream, 0);

            int bufferOffset = 0;
            int remaining = (int)size;

            while (remaining > 0)
            {
                int toRead = Math.Min(remaining, (int)blockSize - 4);
                stream.Read(buffer, bufferOffset, toRead);
                bufferOffset += toRead;
                remaining -= toRead;

                if (remaining > 0)
                {
                    if (nextAddress == 0) throw new InvalidOperationException("Chain too short for FileSize.");
                    stream.Seek(nextAddress, SeekOrigin.Begin);
                    nextAddress = GetNextAddress(stream, 0);
                }
            }

            return buffer;
        }

        private static uint GetNextAddress(FileStream stream, int relOffset)
        {
            // The location of the start of the next sector is the first four bytes of the current sector. This should be 0x00000000 if no next sector.
            byte[] nextAddressBytes = new byte[4];

            if (relOffset != 0)
                stream.Seek(relOffset, SeekOrigin.Current); // To be used to back up 4 bytes from the origin at the start

            stream.Read(nextAddressBytes, 0, 4);

            return BitConverter.ToUInt32(nextAddressBytes, 0);
        }
    }
}
