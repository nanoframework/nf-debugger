//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class DeploymentBlock
    {
        /// <summary>
        /// Start address of block
        /// </summary>
        public int StartAddress { get; }

        /// <summary>
        /// Block size
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Data to deploy to the device
        /// </summary>
        public byte[] DeploymentData { get { return _deploymentData; } }
        private byte[] _deploymentData;

        /// <summary>
        /// Available space in this block
        /// </summary>
        public int AvailableSpace
        {
            get
            {
                return Size - DeploymentData.Length;
            }
        }

        /// <summary>
        /// Required aliment of programming word.
        /// </summary>
        public int ProgramAligment { get; } = 0;

        /// <summary>
        /// Creates a new <see cref="DeploymentBlock"/> starting at <paramref name="startAddress"/> with <paramref name="size"/> size.
        /// </summary>
        /// <param name="startAddress">Start address of the block.</param>
        /// <param name="size">Size of the block.</param>
        /// <param name="programAligment">Alignment size for programming.</param>
        public DeploymentBlock(int startAddress, int size, int programAligment)
        {
            // empty deployment data
            _deploymentData = new byte[0];

            StartAddress = startAddress;
            Size = size;
            ProgramAligment = programAligment;
        }

        internal void AddDeploymentData(byte[] buffer)
        {
            // resize deployment data array
            int previousLenght = _deploymentData.Length;
            Array.Resize(ref _deploymentData, previousLenght + buffer.Length);

            Array.Copy(buffer, 0, _deploymentData, previousLenght, buffer.Length);
        }
    }
}
