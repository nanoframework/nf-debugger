//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH the enums in nanoHAL_ConfigurationManager.h (in nf-interpreter) !!! //
    ////////////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Operation on a configuration block, executed by the device.
    /// </summary>
    public enum ConfigurationOperation : uint
    {
        /// <summary>
        /// Add a block, it gets the next index (the current count).
        /// </summary>
        Add = 1,

        /// <summary>
        /// Replace the block at the index with the one provided, keeping the index.
        /// </summary>
        Replace = 2,

        /// <summary>
        /// Delete the block at the index. The indexes of the blocks that follow shift down by one.
        /// </summary>
        Delete = 3,
    }

    /// <summary>
    /// Flags of a configuration operation.
    /// </summary>
    [Flags]
    public enum ConfigurationOperationFlags : uint
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// First chunk of the block (set by the library).
        /// </summary>
        First = 0x01,

        /// <summary>
        /// Last chunk of the block (set by the library).
        /// </summary>
        Last = 0x02,

        /// <summary>
        /// Delete a wireless profile even if a network interface is using it.
        /// </summary>
        Force = 0x04,

        /// <summary>
        /// Replace at an index equal to the count adds the block (single instance blocks, like certificates).
        /// </summary>
        Upsert = 0x08,

        /// <summary>
        /// Replace of a wireless profile keeps the password that is stored on the device (passwords can't be read back).
        /// </summary>
        KeepPassword = 0x10,
    }

    /// <summary>
    /// Outcome of a configuration operation.
    /// </summary>
    public enum ConfigurationOperationError : uint
    {
        /// <summary>
        /// The operation was executed.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Chunk accepted, the device is waiting for the next one.
        /// </summary>
        Pending = 1,

        /// <summary>
        /// The block is the same as the one stored, nothing was written.
        /// </summary>
        NoChanges = 2,

        /// <summary>
        /// The configuration type, or the operation, isn't supported by the device.
        /// </summary>
        NotSupported = 10,

        /// <summary>
        /// There is no block at this index.
        /// </summary>
        InvalidIndex = 11,

        /// <summary>
        /// The block has a wrong size or invalid content.
        /// </summary>
        InvalidBlock = 12,

        /// <summary>
        /// The maximum count of blocks of this type has been reached.
        /// </summary>
        MaxCount = 13,

        /// <summary>
        /// There is no room for the block in the configuration storage.
        /// </summary>
        NoSpace = 14,

        /// <summary>
        /// The device doesn't have enough memory to execute the operation.
        /// </summary>
        OutOfMemory = 15,

        /// <summary>
        /// A chunk was received out of sequence.
        /// </summary>
        Sequence = 16,

        /// <summary>
        /// There is already a wireless profile with this Id.
        /// </summary>
        DuplicateId = 17,

        /// <summary>
        /// The wireless profile is used by a network interface (see <see cref="ConfigurationOperationFlags.Force"/>).
        /// </summary>
        InUse = 18,

        /// <summary>
        /// There is nothing stored to delete.
        /// </summary>
        NotFound = 19,

        /// <summary>
        /// Error reading or writing the configuration storage.
        /// </summary>
        StorageError = 20,

        /// <summary>
        /// The device doesn't have the configuration storage API (firmware has to be updated).
        /// </summary>
        /// <remarks>This one is set by the library, it's not a device error code.</remarks>
        FirmwareUpdateRequired = 1000,

        /// <summary>
        /// The device didn't reply.
        /// </summary>
        /// <remarks>This one is set by the library, it's not a device error code.</remarks>
        NoReply = 1001,
    }

    /// <summary>
    /// Flags of a configuration block type.
    /// </summary>
    [Flags]
    public enum ConfigurationBlockFlags : byte
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// Blocks of this type can be added.
        /// </summary>
        CanAdd = 0x01,

        /// <summary>
        /// Blocks of this type can be replaced.
        /// </summary>
        CanReplace = 0x02,

        /// <summary>
        /// Blocks of this type can be deleted.
        /// </summary>
        CanDelete = 0x04,

        /// <summary>
        /// Blocks of this type can be read. Certificates are never readable.
        /// </summary>
        Readable = 0x08,

        /// <summary>
        /// A delete resets the block to its defaults instead of removing it (network interfaces).
        /// </summary>
        DeleteResets = 0x10,

        /// <summary>
        /// The Id is assigned by the storage and can change on replace.
        /// </summary>
        IdIsSlot = 0x20,
    }

    /// <summary>
    /// Flags of the configuration storage.
    /// </summary>
    [Flags]
    public enum ConfigurationStorageFlags : uint
    {
        /// <summary>
        /// No flags.
        /// </summary>
        None = 0,

        /// <summary>
        /// Storing a block erases and rewrites the whole configuration storage.
        /// </summary>
        ErasesStorage = 0x01,

        /// <summary>
        /// The operations are atomic in case of power loss.
        /// </summary>
        PowerLossSafe = 0x02,
    }

    /// <summary>
    /// Information about a configuration block type stored in the device.
    /// </summary>
    public sealed class ConfigurationBlockInfo
    {
        /// <summary>
        /// Configuration block type.
        /// </summary>
        public DeviceConfiguration.DeviceConfigurationOption Option { get; }

        /// <summary>
        /// Count of blocks stored.
        /// </summary>
        public int Count { get; internal set; }

        /// <summary>
        /// Maximum count of blocks. <see langword="null"/> if it's only limited by the free space.
        /// </summary>
        public int? MaxCount { get; }

        /// <summary>
        /// Flags of this block type.
        /// </summary>
        public ConfigurationBlockFlags Flags { get; }

        /// <summary>
        /// Size of the block, or the maximum size for blocks of variable size (certificates).
        /// </summary>
        public uint MaxBlockSize { get; }

        /// <summary>
        /// <see langword="true"/> if the device supports this block type.
        /// </summary>
        public bool IsSupported => MaxCount != 0;

        /// <summary>
        /// <see langword="true"/> if there is at least one block stored.
        /// </summary>
        public bool IsStored => Count > 0;

        /// <summary>
        /// <see langword="true"/> if a block can be added now.
        /// </summary>
        public bool CanAdd => Flags.HasFlag(ConfigurationBlockFlags.CanAdd) && (MaxCount == null || Count < MaxCount);

        /// <summary>
        /// <see langword="true"/> if blocks of this type can be replaced.
        /// </summary>
        public bool CanReplace => Flags.HasFlag(ConfigurationBlockFlags.CanReplace);

        /// <summary>
        /// <see langword="true"/> if blocks of this type can be deleted.
        /// </summary>
        public bool CanDelete => Flags.HasFlag(ConfigurationBlockFlags.CanDelete);

        /// <summary>
        /// Creates the information from the block information reported by the device.
        /// </summary>
        /// <param name="blockInfo">Block information reported by the device.</param>
        public ConfigurationBlockInfo(Commands.Monitor_ConfigurationInfo.BlockInfo blockInfo)
        {
            Option = (DeviceConfiguration.DeviceConfigurationOption)blockInfo.Configuration;
            Count = blockInfo.Count;
            MaxCount = blockInfo.MaxCount == 0xFF ? (int?)null : blockInfo.MaxCount;
            Flags = (ConfigurationBlockFlags)blockInfo.Flags;
            MaxBlockSize = blockInfo.MaxBlockSize;
        }
    }

    /// <summary>
    /// Information about the configuration storage of a device, as reported by the device.
    /// Never includes the content of the blocks.
    /// </summary>
    public sealed class ConfigurationStorageInfo
    {
        /// <summary>
        /// Version of the configuration storage API.
        /// </summary>
        public uint Version { get; }

        /// <summary>
        /// Flags of the configuration storage.
        /// </summary>
        public ConfigurationStorageFlags Flags { get; }

        /// <summary>
        /// Size of the configuration storage. <see langword="null"/> if unknown.
        /// </summary>
        public uint? TotalSize { get; }

        /// <summary>
        /// Free space in the configuration storage. <see langword="null"/> if unknown.
        /// </summary>
        public uint? FreeSize { get; }

        /// <summary>
        /// Information about each block type.
        /// </summary>
        public IReadOnlyList<ConfigurationBlockInfo> Blocks { get; }

        /// <summary>
        /// Gets the information about a block type, <see langword="null"/> if the device didn't report it.
        /// </summary>
        public ConfigurationBlockInfo this[DeviceConfiguration.DeviceConfigurationOption option] =>
            Blocks.FirstOrDefault(b => b.Option == option);

        /// <summary>
        /// Creates the information from the reply of the device.
        /// </summary>
        /// <param name="reply">Reply of the device to <see cref="Commands.Monitor_ConfigurationInfo"/>.</param>
        public ConfigurationStorageInfo(Commands.Monitor_ConfigurationInfo.Reply reply)
        {
            Version = reply.Version;
            Flags = (ConfigurationStorageFlags)reply.Flags;
            TotalSize = reply.TotalSize == 0 ? (uint?)null : reply.TotalSize;
            FreeSize = reply.FreeSize == 0xFFFFFFFF ? (uint?)null : reply.FreeSize;
            Blocks = reply.Blocks
                .Take((int)reply.BlockInfoCount)
                .Select(b => new ConfigurationBlockInfo(b))
                .ToList();
        }
    }

    /// <summary>
    /// Result of a configuration operation.
    /// </summary>
    public sealed class ConfigurationOperationResult
    {
        /// <summary>
        /// Outcome of the operation.
        /// </summary>
        public ConfigurationOperationError Error { get; }

        /// <summary>
        /// <see langword="true"/> if the operation was executed (or there was nothing to change).
        /// </summary>
        public bool Success => Error == ConfigurationOperationError.Success || Error == ConfigurationOperationError.NoChanges;

        /// <summary>
        /// Index of the block after the operation (add, replace). <see langword="null"/> otherwise.
        /// </summary>
        public int? BlockIndex { get; }

        /// <summary>
        /// Count of blocks of this type after the operation. <see langword="null"/> if the device didn't report it.
        /// </summary>
        public int? Count { get; }

        /// <summary>
        /// Id of the wireless profile after the operation (add, replace). <see langword="null"/> otherwise.
        /// </summary>
        public uint? Id { get; }

        internal ConfigurationOperationResult(ConfigurationOperationError error)
        {
            Error = error;
        }

        /// <summary>
        /// Creates the result from the reply of the device.
        /// </summary>
        /// <param name="reply">Reply of the device to <see cref="Commands.Monitor_ConfigurationOperation"/>.</param>
        public ConfigurationOperationResult(Commands.Monitor_ConfigurationOperation.Reply reply)
        {
            Error = (ConfigurationOperationError)reply.ErrorCode;
            BlockIndex = reply.BlockIndex == 0xFFFFFFFF ? (int?)null : (int)reply.BlockIndex;
            Count = (int)reply.Count;
            Id = reply.Id == 0xFFFFFFFF ? (uint?)null : reply.Id;
        }

        /// <inheritdoc/>
        public override string ToString() => Error.ToString();
    }
}
