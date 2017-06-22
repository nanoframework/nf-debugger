//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class DeploymentSector
    {
        /// <summary>
        /// Start address of sector
        /// </summary>
        public int StartAddress { get { return Blocks.First().StartAddress; } }

        /// <summary>
        /// Number of blocks in this sector
        /// </summary>
        public int NumBlocks { get { return Blocks.Count; } }

        /// <summary>
        /// Bytes per block in this sector
        /// </summary>
        public int BytesPerBlock { get { return Blocks.First().Size; } }

        public int Size
        {
            get
            {
                return Blocks.Sum(b => b.Size);
            }
        }


        public List<DeploymentBlock> Blocks { get; internal set; }

        public byte[] DeploymentData
        {

            set
            {
                int remainingBytes = value.Length;
                int currentPosition = 0;

                // find first block with available space
                while (remainingBytes > 0)
                {
                    var block = Blocks.First(b => b.AvailableSpace > 0);
                    int currentDataSize = block.DeploymentData.Length;

                    int bytesToCopy = Math.Min(block.AvailableSpace, remainingBytes);

                    byte[] tempBuffer = new byte[bytesToCopy];

                    Array.Copy(value, currentPosition, tempBuffer, 0, bytesToCopy);
                    block.AddDeploymentData(tempBuffer);

                    remainingBytes -= bytesToCopy;
                    currentPosition += bytesToCopy;
                }

            }
        }

        /// <summary>
        /// Available space in this sector
        /// </summary>
        public int AvailableSpace
        {
            get
            {
                int sum = 0;

                Blocks.Select(b => sum += b.Size - b.DeploymentData.Length).ToList();

                return sum;
            }
        }


        public DeploymentSector(List<DeploymentBlock> blocks)
        {
            Blocks = blocks;
        }
    }
}
