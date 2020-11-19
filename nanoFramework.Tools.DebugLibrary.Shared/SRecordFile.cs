//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    public class SRecordFile
    {
        public class Block
        {
            public uint address;
            public MemoryStream data;
            public bool executable;
        }

        static public List<Block> Parse(string file)
        {
            // validate if file exists
            if (!File.Exists(file))
            {
                throw new FileNotFoundException($"Can't open {file}.");
            }

            uint entrypoint = 0;
            List<Block> blocks = new List<Block>();

            var textLines = File.ReadAllLines(file);

            foreach (string line in textLines)
            {
                int lineNum = 0;

                char[] lineBytes = line.ToCharArray();
                int len = lineBytes.Length;
                int i;

                lineNum++; if (len == 0) continue;

                // we only accept S0, S3 and S7 records (header, memory loadable data, execution address)
                if (
                    (char.ToLower(lineBytes[0]) != 's') ||
                    (lineBytes[1] != '0' && lineBytes[1] != '3' && lineBytes[1] != '7')
                    )
                {
                    throw new ArgumentException($"Unknown format at line {lineNum} of {file}:\n {line}");
                }

                // we discard S0 records
                if ((char.ToLower(lineBytes[0]) == 's') && (lineBytes[1] == '0'))
                {
                    continue;
                }

                int num = byte.Parse(new string(lineBytes, 2, 2), System.Globalization.NumberStyles.HexNumber);
                if (num != ((len / 2) - 2))
                {
                    throw new ArgumentException($"Incorrect length at line {lineNum} of {file}: {num}");
                }

                byte crc = (byte)num;

                for (i = 4; i < len - 2; i += 2)
                {
                    crc += byte.Parse(new string(lineBytes, i, 2), System.Globalization.NumberStyles.HexNumber);
                }

                byte checksum = byte.Parse(new string(lineBytes, len - 2, 2), System.Globalization.NumberStyles.HexNumber);

                if ((checksum ^ crc) != 0xFF)
                {
                    throw new ArgumentException($"Incorrect crc at line {lineNum} of {file}: got {crc:X2}, expected {checksum:X2}");
                }

                num -= 5;

                uint address = uint.Parse(new string(lineBytes, 4, 8), System.Globalization.NumberStyles.HexNumber);

                if (lineBytes[1] == '7')
                {
                    entrypoint = address;
                    for (i = 0; i < blocks.Count; i++)
                    {
                        Block bl = blocks[i];
                        if (bl.address == entrypoint)
                        {
                            bl.executable = true;
                        }
                    }
                    break;
                }
                else
                {
                    Block bl = new Block
                    {
                        address = address,
                        data = new MemoryStream(),
                        executable = false
                    };

                    for (i = 0; i < num; i++)
                    {
                        bl.data.WriteByte(byte.Parse(new string(lineBytes, 12 + i * 2, 2), System.Globalization.NumberStyles.HexNumber));
                    }

                    for (i = 0; i < blocks.Count; i++)
                    {
                        Block bl2 = blocks[i];
                        int num2 = (int)bl2.data.Length;

                        if (bl2.address + num2 == bl.address)
                        {
                            byte[] data = bl.data.ToArray();

                            bl2.data.Write(data, 0, data.Length);

                            bl = null;
                            break;
                        }

                        if (bl.address + num == bl2.address)
                        {
                            byte[] data = bl2.data.ToArray();

                            bl.data.Write(data, 0, data.Length);

                            bl2.address = bl.address;
                            bl2.data = bl.data;

                            bl = null;
                            break;
                        }

                        if (bl.address < bl2.address)
                        {
                            blocks.Insert(i, bl);

                            bl = null;
                            break;
                        }
                    }

                    if (bl != null)
                    {
                        blocks.Add(bl);
                    }
                }
            }

            return blocks;
        }

        static public void Encode(Stream stream, byte[] buf, uint address)
        {
            StreamWriter writer = new StreamWriter(stream, Encoding.UTF8);

            uint len = (uint)buf.Length;
            int offset = 0;

            while (len > 0)
            {
                uint size = len > 16 ? 16 : len;
                byte crc = (byte)(size + 5);

                writer.Write("S3{0:X2}{1:X8}", size + 5, address);

                crc += (byte)(address >> 0);
                crc += (byte)(address >> 8);
                crc += (byte)(address >> 16);
                crc += (byte)(address >> 24);

                for (uint i = 0; i < size; i++)
                {
                    byte v = buf[offset++];

                    writer.Write("{0:X2}", v);

                    crc += v;
                }

                address += size;
                len -= size;

                writer.WriteLine("{0:X2}", (byte)~crc);
            }

            writer.Flush();
        }
    }
}
