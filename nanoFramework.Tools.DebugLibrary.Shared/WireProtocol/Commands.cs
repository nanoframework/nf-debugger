//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public enum AccessMemoryErrorCodes : uint
    {
        ////////////////////////////////////////////////////////////////////////////////////////
        // NEED TO KEEP THESE IN SYNC WITH native 'AccessMemoryErrorCodes' enum in Debugger.h //
        ////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// No error
        /// </summary>
        NoError = 0x0001,

        /// <summary>
        /// Permission denied
        /// </summary>
        PermissionDenied = 0x0010,

        /// <summary>
        /// Failed to allocate buffer to execute operation
        /// </summary>
        FailedToAllocateReadBuffer = 0x0020,

        /// <summary>
        /// Breakpoints are disabled in the device
        /// </summary>
        RequestedOperationFailed = 0x0030,

        Unknown = 0xFFFF,
    }

    /// <summary>
    /// Storage operation error codes.
    /// </summary>
    public enum StorageOperationErrorCode : uint
    {
        ///////////////////////////////////////////////////////////////////////////////////////////
        // NEED TO KEEP THESE IN SYNC WITH native 'StorageOperationErrorCode' enum in Debugger.h //
        ///////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// No error.
        /// </summary>
        NoError = 0x0001,

        /// <summary>
        /// Write error.
        /// </summary>
        WriteError = 0x0010,

        /// <summary>
        /// Delete error.
        /// </summary>
        DeleteError = 0x0020,

        /// <summary>
        /// Platform dependent error.
        /// </summary>
        PlatformError = 0x0030,
    }

    public class Commands
    {
        public const uint c_Monitor_Ping = 0x00000000; // The payload is empty, this command is used to let the other side know we are here...
        public const uint c_Monitor_Message = 0x00000001; // The payload is composed of the string characters, no zero at the end.
        public const uint c_Monitor_ReadMemory = 0x00000002;
        public const uint c_Monitor_WriteMemory = 0x00000003;
        public const uint c_Monitor_CheckMemory = 0x00000004;
        public const uint c_Monitor_EraseMemory = 0x00000005;
        public const uint c_Monitor_Execute = 0x00000006;
        public const uint c_Monitor_Reboot = 0x00000007;
        public const uint c_Monitor_MemoryMap = 0x00000008;
        public const uint c_Monitor_ProgramExit = 0x00000009; // The payload is empty, this command is used to tell the PC of a program termination        
        public const uint c_Monitor_DeploymentMap = 0x0000000B;
        public const uint c_Monitor_FlashSectorMap = 0x0000000C;
        public const uint c_Monitor_OemInfo = 0x0000000E;
        public const uint c_Monitor_QueryConfiguration = 0x0000000F;
        public const uint c_Monitor_UpdateConfiguration = 0x00000010;
        public const uint c_Monitor_StorageOperation = 0x00000011;
        public const uint c_Monitor_TargetInfo = 0x00000020;

        public class Monitor_Message : IConverter
        {
            public byte[] m_data = null;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                m_data = new byte[size];
            }

            public override string ToString()
            {
                Boolean completed;
                Int32 bytesUsed, charsUsed;
                var chars = new Char[m_data.Length];

                Encoding.UTF8.GetDecoder().Convert(m_data, 0, m_data.Length, chars, 0, m_data.Length, false, out bytesUsed, out charsUsed, out completed);
                return new String(chars, 0, charsUsed);
            }
        }

        public class Monitor_FlashSectorMap
        {
            public const uint c_MEMORY_USAGE_BOOTSTRAP = 0x00000010;
            public const uint c_MEMORY_USAGE_CODE = 0x00000020;
            public const uint c_MEMORY_USAGE_CONFIG = 0x00000030;
            public const uint c_MEMORY_USAGE_FS = 0x00000040;
            public const uint c_MEMORY_USAGE_DEPLOYMENT = 0x00000050;
            public const uint c_MEMORY_USAGE_UPDATE = 0x0060;
            public const uint c_MEMORY_USAGE_SIMPLE_A = 0x00000090;
            public const uint c_MEMORY_USAGE_SIMPLE_B = 0x000000A0;
            public const uint c_MEMORY_USAGE_STORAGE_A = 0x000000E0;
            public const uint c_MEMORY_USAGE_STORAGE_B = 0x000000F0;
            public const uint c_MEMORY_USAGE_MASK = 0x000000F0;

            // media attributes
            public const uint BlockRegionAttributes_MASK = 0x0000FF00;
            public const uint BlockRegionFlashProgrammingWidth_MASK = 0x00007E00;

            public const uint BlockRegionAttribute_MemoryMapped = 0x0100;

            // Programming width definitions
            public const uint BlockRegionAttribute_ProgramWidthIs8bits = 0x0000;
            public const uint BlockRegionAttribute_ProgramWidthIs64bits = 0x0200;
            public const uint BlockRegionAttribute_ProgramWidthIs128bits = 0x0400;
            public const uint BlockRegionAttribute_ProgramWidthIs256bits = 0x0800;
            public const uint BlockRegionAttribute_ProgramWidthIs512bits = 0x1000;
            public const uint BlockRegionAttribute_ProgramWidthIs1024bits = 0x2000;
            public const uint BlockRegionAttribute_ProgramWidthIs2048bits = 0x4000;

            public struct FlashSectorData
            {
                public uint StartAddress;
                public uint NumBlocks;
                public uint BytesPerBlock;
                public uint Flags;
            }

            public class Reply : IConverter
            {
                public List<FlashSectorData> m_map;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    int num = size / (4 * 4);  // size divided by size of FlashSectorData struct (3*sizeof(uint))

                    m_map = Enumerable.Range(0, num).Select(x => new FlashSectorData()).ToList();
                }
            }
        }

        public class Monitor_Ping
        {
            ///////////////////////////////////////////////////////////////////////

            public const uint c_Ping_Source_NanoCLR = 0x00010000;
            public const uint c_Ping_Source_NanoBooter = 0x00010001;
            public const uint c_Ping_Source_Host = 0x00010002;

            public const uint c_Ping_DbgFlag_Stop = 0x00000001;
            public const uint c_Ping_DbgFlag_BigEndian = 0x02000002;
            public const uint c_Ping_DbgFlag_AppExit = 0x00000004;

            ///////////////////////////////////////////////////////////////////////
            // flags specific to Wire Protocol capabilities
            public const uint c_Ping_WPFlag_SupportsCRC32 = 0x00000010;

            // Wire Protocol packet size (3rd position)
            public const uint Monitor_Ping_c_PacketSize_Position = 0x00000700;
            // default packet size is 1024
            public const uint Monitor_Ping_c_PacketSize_1024 = 0x00000100;
            public const uint Monitor_Ping_c_PacketSize_0512 = 0x00000200;
            public const uint Monitor_Ping_c_PacketSize_0256 = 0x00000300;
            public const uint Monitor_Ping_c_PacketSize_0128 = 0x00000400;

            //////////////////////////////////////////////////////////////////////
            // flags related with device capabilities

            /// <summary>
            /// This flag indicates that the device has a proprietary bootloader.
            /// </summary>
            public const uint Monitor_Ping_c_HasProprietaryBooter = 0x00010000;

            /// <summary>
            /// This flag indicates that the target device is IFU capable.
            /// </summary>
            public const uint Monitor_Ping_c_IFUCapable = 0x00020000;

            /// <summary>
            /// This flag indicates that the device requires that the configuration block to be erased before updating it.
            /// </summary>
            public const uint Monitor_Ping_c_ConfigBlockRequiresErase = 0x00040000;

            /// <summary>
            /// This flag indicates that the device has nanoBooter.
            /// </summary>
            public const uint Monitor_Ping_c_HasNanoBooter = 0x00080000;

            ///////////////////////////////////////////////////////////////////////

            public uint Source;
            public uint Flags;


            public class Reply
            {
                public uint Source;
                public uint Flags;
            }
        }

        public class Monitor_OemInfo
        {
            public class Reply : IConverter
            {
                public ReleaseInfo m_releaseInfo;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_releaseInfo = new ReleaseInfo(data.Length);
                }
            }
        }

        public class Monitor_TargetInfo
        {
            public class Reply : IConverter
            {
                public TargetInfo TargetInfo;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    TargetInfo = new TargetInfo(data.Length);
                }
            }
        }

        public class Monitor_ReadMemory
        {
            public uint m_address = 0;
            public uint m_length = 0;

            public class Reply : IConverter
            {
                public uint ErrorCode;
                public byte[] m_data = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    ErrorCode = 0;

                    // buffer length is: size - sizeof(ErrorCode)
                    m_data = new byte[size - 4];
                }
            }
        }

        public class Monitor_WriteMemory : OverheadBase
        {
            public uint m_address = 0;
            public uint m_length = 0;
            public byte[] m_data = null;

            public class Reply
            {
                public uint ErrorCode;
            };

            public void PrepareForSend(uint address, byte[] data, int offset, int length)
            {
                m_address = address;

                PrepareForSend(data, offset, length);
            }

            public override bool PrepareForSend(byte[] data, int offset, int length)
            {
                m_length = (uint)length;
                m_data = new byte[length];

                Array.Copy(data, offset, m_data, 0, length);

                return true;
            }
        }

        public class Monitor_CheckMemory
        {
            public uint m_address = 0;
            public uint m_length = 0;

            public class Reply
            {
                public uint m_crc = 0;
            }
        }

        public class Monitor_EraseMemory
        {
            public uint m_address = 0;
            public uint m_length = 0;

            public class Reply
            {
                public uint ErrorCode;
            };
        }

        public class Monitor_Execute
        {
            public uint m_address = 0;
        }

        public class Monitor_MemoryMap
        {
            public const uint c_RAM = 0x00000001;
            public const uint c_FLASH = 0x00000002;

            public struct Range
            {
                public uint m_address;
                public uint m_length;
                public uint m_flags;
            }

            public class Reply : IConverter
            {
                public List<Range> m_map;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    int num = size / (3 * 4);

                    m_map = Enumerable.Range(0, num).Select(x => new Range()).ToList();
                }
            }
        }

        public class Monitor_Signature : OverheadBase
        {
            public uint m_keyIndex;
            public uint m_length = 0;
            public byte[] m_signature = null;

            public override bool PrepareForSend(byte[] signature, int keyIndex, int offset = 0)
            {
                int length = signature.Length;

                m_keyIndex = (uint)keyIndex;
                m_length = (uint)length;
                m_signature = new byte[length];

                Array.Copy(signature, 0, m_signature, 0, length);

                return true;
            }
        }

        public class MonitorReboot
        {
            public uint flags = 0;
        }

        public class Monitor_DeploymentMap
        {
            public struct DeploymentData
            {
                public uint m_address;
                public uint m_size;
                public uint m_CRC;
            }

            public class Reply : IConverter
            {
                public List<DeploymentData> m_map;
                public int m_count = 0;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    int num = (size - 4) / (3 * 4);  // size - sizof(m_count) divided by size of deployment data struct (3*sizeof(uint))

                    m_map = Enumerable.Range(0, num).Select(x => new DeploymentData()).ToList();

                }
            }
        }

        public class Monitor_QueryConfiguration
        {
            public uint Configuration;

            public uint BlockIndex;

            public class Reply : IConverter
            {
                public byte[] Data = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Data = new byte[size];
                }
            }

            public class NetworkConfiguration : NetworkConfigurationBase, IConverter
            {
                public NetworkConfiguration()
                {
                    Marker = new byte[4];
                    MacAddress = new byte[6];
                    IPv6Address = new uint[4];
                    IPv6NetMask = new uint[4];
                    IPv6GatewayAddress = new uint[4];
                    IPv6DNSAddress1 = new uint[4];
                    IPv6DNSAddress2 = new uint[4];
                    StartupAddressMode = (byte)AddressMode.Invalid;
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Marker = new byte[4];
                    MacAddress = new byte[6];
                    IPv6Address = new uint[4];
                    IPv6NetMask = new uint[4];
                    IPv6GatewayAddress = new uint[4];
                    IPv6DNSAddress1 = new uint[4];
                    IPv6DNSAddress2 = new uint[4];
                    StartupAddressMode = (byte)AddressMode.Invalid;
                }
            }

            public class NetworkWirelessConfiguration : Wireless80211ConfigurationBase, IConverter
            {
                public NetworkWirelessConfiguration()
                {
                    Marker = new byte[4];
                    Id = 0xFFFFFF;
                    Authentication = 0;
                    Encryption = 0;
                    Radio = 0;
                    Ssid = new byte[32];
                    Password = new byte[64];
                    Options = 0;
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Marker = new byte[4];
                    Id = 0xFFFFFF;
                    Authentication = 0;
                    Encryption = 0;
                    Radio = 0;
                    Ssid = new byte[32];
                    Password = new byte[64];
                    Options = 0;
                }
            }

            public class NetworkWirelessAPConfiguration : WirelessAPConfigurationBase, IConverter
            {
                public NetworkWirelessAPConfiguration()
                {
                    Marker = new byte[4];
                    Id = 0xFFFFFF;
                    Authentication = 0;
                    Encryption = 0;
                    Radio = 0;
                    Ssid = new byte[32];
                    Password = new byte[64];
                    Options = 0;
                    Channel = 0;
                    MaxConnections = 0;
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Marker = new byte[4];
                    Id = 0xFFFFFF;
                    Authentication = 0;
                    Encryption = 0;
                    Radio = 0;
                    Ssid = new byte[32];
                    Password = new byte[64];
                    Options = 0;
                    Channel = 0;
                    MaxConnections = 0;
                }
            }

            public class X509CaRootBundleConfig : X509CaRootBundleBase, IConverter
            {
                public X509CaRootBundleConfig()
                {
                    Marker = new byte[4];
                    CertificateSize = 0xFFFF;
                    Certificate = new byte[64];
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Marker = new byte[4];
                    CertificateSize = 0xFFFF;
                    Certificate = new byte[size - 4 - 4];
                }
            }

            public class X509DeviceCertificatesConfig : X509DeviceCertificatesBase, IConverter
            {
                public X509DeviceCertificatesConfig()
                {
                    Marker = new byte[4];
                    CertificateSize = 0xFFFF;
                    Certificate = new byte[64];
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    Marker = new byte[4];
                    CertificateSize = 0xFFFF;
                    Certificate = new byte[size - 4 - 4];
                }
            }
        }

        public class Monitor_UpdateConfiguration : OverheadBase
        {
            public uint Configuration;
            public uint BlockIndex;
            public uint Length;
            public uint Offset;
            public uint Done;
            public byte[] Data;

            public class Reply
            {
                public uint ErrorCode;
            };

            public override bool PrepareForSend(byte[] data, int length, int offset = 0)
            {
                Length = (uint)length;
                Data = new byte[length];

                Offset = (uint)offset;

                Array.Copy(data, offset, Data, 0, length);

                return true;
            }
        }

        /// <summary>
        /// Perform storage operation on the target device.
        /// </summary>
        public class Monitor_StorageOperation : OverheadBase
        {
            /// <summary>
            /// Storage operation to be performed.
            /// </summary>
            public uint Operation = (byte)StorageOperation.None;

            /// <summary>
            /// File name for the the storage operation.
            /// </summary>
            [IgnoreDataMember]
            public string FileName = string.Empty;

            /// <summary>
            /// Length of the name of the file to be used in the operation.
            /// </summary>
            public uint NameLength = 0;

            /// <summary>
            /// Length of the data to be used in the operation.
            /// </summary>
            public uint DataLength = 0;

            /// <summary>
            /// Offset in the file data of the chunck in this operation.
            /// </summary>
            /// <remarks>
            /// This is to be used by the target device to know where to start writing the chunk data.
            /// </remarks>
            public uint Offset = 0;

            /// <summary>
            /// Data buffer to be sent to the device.
            /// </summary>
            public byte[] Data;

            public class Reply
            {
                public uint ErrorCode;
            };

            /// <summary>
            /// Prepare for sending a storage operation to the target device.
            /// </summary>
            /// <param name="operation"><see cref="StorageOperation"/> to be performed.</param>
            /// <param name="name">Name of the file to be used in the operation.</param>
            public void SetupOperation(
              StorageOperation operation,
              string name)
            {
                Operation = (uint)operation;
                FileName = name;
            }

            /// <summary>
            /// Prepare for sending a storage operation to the target device.
            /// </summary>
            /// <param name="buffer">Data buffer to be sent to the device.</param>
            /// <param name="offset">Offset in the <paramref name="buffer"/> to start copying data from.</param>
            /// <param name="length">Length of the data to be copied from the <paramref name="buffer"/>.</param>
            public override bool PrepareForSend(
                byte[] buffer,
                int length,
                int offset = 0)
            {
                // setup the data payload
                DataLength = (uint)length;
                Data = new byte[length + FileName.Length];

                // add the file name to the data property buffer
                var tempName = Encoding.UTF8.GetBytes(FileName);
                NameLength = (uint)tempName.Length;
                Array.Copy(tempName, 0, Data, 0, NameLength);

                // copy the buffer data to the data property buffer
                Array.Copy(buffer, offset, Data, NameLength, length);

                return true;
            }

            //////////////////////////////////////////////////////////////////////////////////////
            // !!! KEEP IN SYNC WITH typedef enum Monitor_StorageOperation (in native code) !!! //
            //////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Storage operation to be performed.
            /// </summary>
            public enum StorageOperation : byte
            {
                /// <summary>
                /// Not specified.
                /// </summary>
                None = 0,

                /// <summary>
                /// Write to storage.
                /// </summary>
                /// <remarks>
                /// If the file already exists, it will be overwritten.
                /// </remarks>
                Write = 1,

                /// <summary>
                /// Delete from storage.
                /// </summary>
                /// <remarks>
                /// If the file doesn't exist, no action is taken.
                /// </remarks>
                Delete = 2,

                /// <summary>
                /// Append to a file.
                /// </summary>
                /// <remarks>
                /// If the file doesn't exist, no action is taken.
                /// </remarks>
                Append = 3
            }
        }

        public const uint c_Debugging_Execution_BasePtr = 0x00020000; // Returns the pointer for the ExecutionEngine object.
        public const uint c_Debugging_Execution_ChangeConditions = 0x00020001; // Sets/resets the state of the debugger.
        public const uint c_Debugging_Execution_SecurityKey = 0x00020002; // Sets security key.
        public const uint c_Debugging_Execution_Unlock = 0x00020003; // Unlocks the low-level command, for mfg. test programs.
        public const uint c_Debugging_Execution_Allocate = 0x00020004; // Permanently allocates some memory.
        public const uint c_Debugging_Execution_Breakpoints = 0x00020005; // Sets breakpoints.
        public const uint c_Debugging_Execution_BreakpointHit = 0x00020006; // Notification that a breakpoint was hit.
        public const uint c_Debugging_Execution_BreakpointStatus = 0x00020007; // Queries last breakpoint hit.
        public const uint c_Debugging_Execution_QueryCLRCapabilities = 0x00020008; // Queries capabilities of the CLR.
        public const uint c_Debugging_Execution_SetCurrentAppDomain = 0x00020009; // Sets current AppDomain for subsequent debugging operations

        public const uint c_Debugging_Thread_Create = 0x00020010; // Creates a new thread, based on a static method.
        public const uint c_Debugging_Thread_List = 0x00020011; // Lists the active/waiting threads.
        public const uint c_Debugging_Thread_Stack = 0x00020012; // Lists the call stack for a thread.
        public const uint c_Debugging_Thread_Kill = 0x00020013; // Kills a thread.
        public const uint c_Debugging_Thread_Suspend = 0x00020014; // Suspends the execution of a thread.
        public const uint c_Debugging_Thread_Resume = 0x00020015; // Resumes the execution of a thread.
        public const uint c_Debugging_Thread_GetException = 0x00020016; // Gets the current exception.
        public const uint c_Debugging_Thread_Unwind = 0x00020017; // Unwinds to given stack frame.
        public const uint c_Debugging_Thread_CreateEx = 0x00020018; // Creates a new thread as Debugging_Thread_Create that borrows the identity of another thread.
        public const uint c_Debugging_Thread_Get = 0x00021000; // Gets the current thread.

        public const uint c_Debugging_Stack_Info = 0x00020020; // Gets more info on a stack frame.
        public const uint c_Debugging_Stack_SetIP = 0x00020021; // Sets the IP on a given stack frame.

        public const uint c_Debugging_Value_ResizeScratchPad = 0x00020030; // Resizes the scratchpad area.
        public const uint c_Debugging_Value_GetStack = 0x00020031; // Reads a value from the stack frame.
        public const uint c_Debugging_Value_GetField = 0x00020032; // Reads a value from an object's field.
        public const uint c_Debugging_Value_GetArray = 0x00020033; // Reads a value from an array's element.
        public const uint c_Debugging_Value_GetBlock = 0x00020034; // Reads a value from a heap block.
        public const uint c_Debugging_Value_GetScratchPad = 0x00020035; // Reads a value from the scratchpad area.
        public const uint c_Debugging_Value_SetBlock = 0x00020036; // Writes a value to a heap block.
        public const uint c_Debugging_Value_SetArray = 0x00020037; // Writes a value to an array's element.
        public const uint c_Debugging_Value_AllocateObject = 0x00020038; // Creates a new instance of an object.
        public const uint c_Debugging_Value_AllocateString = 0x00020039; // Creates a new instance of a string.
        public const uint c_Debugging_Value_AllocateArray = 0x0002003A; // Creates a new instance of an array.
        public const uint c_Debugging_Value_Assign = 0x0002003B; // Assigns a value to another value.

        public const uint c_Debugging_TypeSys_Assemblies = 0x00020040; // Lists all the assemblies in the system.
        public const uint c_Debugging_TypeSys_AppDomains = 0x00020044; // Lists all the AppDomans loaded.
        public const uint c_Debugging_TypeSys_InteropNativeAssemblies = 0x00020045; // Lists all the Interop Native Assemblies available in the device.

        public const uint c_Debugging_Resolve_Assembly = 0x00020050; // Resolves an assembly.
        public const uint c_Debugging_Resolve_Type = 0x00020051; // Resolves a type to a string.
        public const uint c_Debugging_Resolve_Field = 0x00020052; // Resolves a field to a string.
        public const uint c_Debugging_Resolve_Method = 0x00020053; // Resolves a method to a string.
        public const uint c_Debugging_Resolve_VirtualMethod = 0x00020054; // Resolves a virtual method to the actual implementation.
        public const uint c_Debugging_Resolve_AppDomain = 0x00020055; // Resolves an AppDomain to it's name, and list its loaded assemblies.

        public const uint c_Debugging_MFUpdate_Start = 0x00020056; // 
        public const uint c_Debugging_MFUpdate_AddPacket = 0x00020057; // 
        public const uint c_Debugging_MFUpdate_Install = 0x00020058; // 
        public const uint c_Debugging_MFUpdate_AuthCmd = 0x00020059; // 
        public const uint c_Debugging_MFUpdate_Authenticate = 0x00020060; // 
        public const uint c_Debugging_MFUpdate_GetMissingPkts = 0x00020061; // 

        public const uint c_Debugging_UpgradeToSsl = 0x00020069; // 

        public const uint c_Debugging_Button_Report = 0x00020080; // Reports a button press/release.
        public const uint c_Debugging_Button_Inject = 0x00020081; // Injects a button press/release.

        public const uint c_Debugging_Messaging_Query = 0x00020090; // Checks the presence of an EndPoint.
        public const uint c_Debugging_Messaging_Send = 0x00020091; // Sends a message to an EndPoint.
        public const uint c_Debugging_Messaging_Reply = 0x00020092; // Response from an EndPoint.

        public const uint c_Debugging_Logging_GetNumberOfRecords = 0x000200A0; // Returns the number of records in the log.
        public const uint c_Debugging_Logging_GetRecord = 0x000200A1; // Returns the n-th record in the log.
        public const uint c_Debugging_Logging_Erase = 0x000200A2; // Erases the logs.
        public const uint c_Debugging_Logging_GetRecords = 0x000200A3; // Returns multiple records, starting from the n-th record.

        public const uint c_Debugging_Deployment_Status = 0x000200B0; // Returns entryPoint and boundary of deployment area.

        public const uint c_Debugging_Info_SetJMC = 0x000200C0; // Sets code to be flagged as JMC (Just my code).

        public const uint c_Profiling_Command = 0x00030000; // Controls various aspects of profiling.
        public const uint c_Profiling_Stream = 0x00030001; // Stream for MFProfiler information.

        public class Debugging_Execution_BasePtr
        {
            public class Reply
            {
                public uint m_EE = 0;
            }
        }

        /// <summary>
        /// These flags are used to change the state of the debugger execution on the target.
        /// </summary>
        public class DebuggingExecutionChangeConditions
        {
            /// <summary>
            /// State for debugger execution on target.
            /// </summary>
            [Flags]
            public enum State : uint
            {
                /////////////////////////////////////////////////////////////////////////////////////////////////
                // NEED TO KEEP THESE IN SYNC WITH native 'CLR_RT_ExecutionEngine' struct in nanoCLR_Runtime.h //
                // constants there start with c_fDebugger_NNNNNNNNNNN
                /////////////////////////////////////////////////////////////////////////////////////////////////

                /// <summary>
                /// Device is in initialization state
                /// </summary>
                Initialize = 0x00000000,

                /// <summary>
                /// Type resolution has failed
                /// </summary>
                ResolutionFailed = 0x00000001,

                /// <summary>
                /// Device has a program running
                /// </summary>
                ProgramRunning = 0x00000400,

                /// <summary>
                /// Device has exited a previously running program
                /// </summary>
                ProgramExited = 0x00000800,

                /// <summary>
                /// Breakpoints are disabled in the device
                /// </summary>
                BreakpointsDisabled = 0x00001000,

                /// <summary>
                /// No debugger text is to be sent by target device
                /// </summary>
                DebuggerQuiet = 0x00010000,

                /// <summary>
                /// Execution engine won't process stack trace when an exception occurs.
                /// </summary>
                /// <remarks>
                /// This is used to save processing time with crawling the stack and gatthering the details. It will also
                /// prevent detailed stack trace information to be sent to the debugger. The StackTrace property in the Exception will be empty.
                /// Note that this won't have any effect if the firmware has been compiled without debugger support or with the configuration that disables tracing exceptions.
                /// </remarks>
                NoStackTraceInExceptions = 0x02000000,

                /// <summary>
                /// Threads associated with timers are created in "suspended" mode.
                /// </summary>
                PauseTimers = 0x04000000,

                /// <summary>
                /// No compaction is to be performed during execution.
                /// </summary>
                NoCompaction = 0x08000000,

                /// <summary>
                /// Enable source level debugging
                /// </summary>
                SourceLevelDebugging = 0x10000000,

                /// <summary>
                /// The debugger is enabled
                /// </summary>
                DebuggerEnabled = 0x40000000,

                /// <summary>
                /// Debugger is stopped
                /// </summary>
                Stopped = 0x80000000,

                Unknown = 0xFFFFFFFF,
            }

            internal const State StateMask = (State.ProgramRunning | State.ProgramExited);

            // these need to be uint type (basic) so that they are properly converted to payload in the outgoing message
            public uint FlagsToSet = 0;
            public uint FlagsToReset = 0;

            public class Reply
            {
                public uint CurrentState = (uint)State.Unknown;
            }
        }

        public class Debugging_Execution_SecurityKey
        {
            public byte[] m_key = new byte[32];
        };

        public class Debugging_Execution_Unlock
        {
            public byte[] m_command = new byte[128];
            public byte[] m_hash = new byte[128];
        };

        public class Debugging_Execution_Allocate
        {
            public uint m_size;

            public class Reply
            {
                public uint m_address = 0;
            }
        };

        public class Debugging_UpgradeToSsl
        {
            public uint m_flags;

            public class Reply
            {
                public int m_success;
            }
        }

        public class Debugging_MFUpdate_Start
        {
            public const int c_UpdateProviderSize = 64;

            public byte[] m_updateProvider = new byte[c_UpdateProviderSize];
            public uint m_updateId;
            public uint m_updateType;
            public uint m_updateSubType;
            public uint m_updateSize;
            public uint m_updatePacketSize;
            public ushort m_updateVerMajor;
            public ushort m_updateVerMinor;

            public class Reply
            {
                public int m_updateHandle;
            };
        };


        public class Debugging_MFUpdate_AuthCommand : OverheadBase
        {
            public int m_updateHandle;
            public uint m_authCommand;
            public uint m_authArgsSize;
            public byte[] m_authArgs;

            public override bool PrepareForSend(byte[] authArgs, int length = 0, int offset = 0)
            {
                m_authArgsSize = (uint)authArgs.Length;
                m_authArgs = new byte[m_authArgsSize];

                Array.Copy(authArgs, 0, m_authArgs, 0, (int)m_authArgsSize);

                return true;
            }

            public class Reply : IConverter
            {
                public int m_success;
                public uint m_responseSize;
                public byte[] m_response;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_response = new byte[(size - 8)]; // subtract sizeof(m_success) and sizeof(m_responseSize)
                }
            };
        };

        public class Debugging_MFUpdate_Authenticate : OverheadBase
        {
            public int m_updateHandle;
            public uint m_authenticationSize;
            public byte[] m_authenticationData;

            public override bool PrepareForSend(byte[] authenticationData, int length = 0, int offset = 0)
            {
                m_authenticationSize = authenticationData == null ? 0 : (uint)authenticationData.Length;
                m_authenticationData = new byte[m_authenticationSize];

                if (m_authenticationSize > 0)
                {
                    Array.Copy(authenticationData, 0, m_authenticationData, 0, (int)m_authenticationSize);
                }

                return true;
            }

            public class Reply
            {
                public int m_success;
            };
        };

        public class Debugging_MFUpdate_GetMissingPkts
        {
            public int m_updateHandle;

            public class Reply : IConverter
            {
                public int m_success;
                public int m_missingPktCount;
                public uint[] m_missingPkts;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_missingPkts = new uint[(size - 8) / 4]; // subtract sizeof(m_success) and sizeof(m_missingPktCount)
                }
            };
        };

        public class Debugging_MFUpdate_AddPacket : OverheadBase
        {
            public int m_updateHandle;
            public uint m_packetIndex;
            public uint m_packetValidation;
            public uint m_packetLength = 0;
            public byte[] m_packetData;

            public override bool PrepareForSend(byte[] packetData, int length = 0, int offset = 0)
            {
                m_packetLength = (uint)packetData.Length;
                m_packetData = new byte[m_packetLength];

                Array.Copy(packetData, 0, m_packetData, 0, (int)m_packetLength);

                return true;
            }

            public class Reply
            {
                public uint m_success;
            };
        };

        public class Debugging_MFUpdate_Install : OverheadBase
        {
            public int m_updateHandle;
            public uint m_updateValidationSize;
            public byte[] m_updateValidation;

            public override bool PrepareForSend(byte[] packetValidation, int length = 0, int offset = 0)
            {
                m_updateValidationSize = (uint)packetValidation.Length;
                m_updateValidation = new byte[m_updateValidationSize];

                Array.Copy(packetValidation, 0, m_updateValidation, 0, (int)m_updateValidationSize);

                return true;
            }

            public class Reply
            {
                public uint m_success;
            };
        };



        public class Debugging_Execution_BreakpointDef
        {
            public const ushort c_STEP_IN = 0x0001;
            public const ushort c_STEP_OVER = 0x0002;
            public const ushort c_STEP_OUT = 0x0004;
            public const ushort c_HARD = 0x0008;
            public const ushort c_EXCEPTION_THROWN = 0x0010;
            public const ushort c_EXCEPTION_CAUGHT = 0x0020;
            public const ushort c_EXCEPTION_UNCAUGHT = 0x0040;
            public const ushort c_THREAD_TERMINATED = 0x0080;
            public const ushort c_THREAD_CREATED = 0x0100;
            public const ushort c_ASSEMBLIES_LOADED = 0x0200;
            public const ushort c_LAST_BREAKPOINT = 0x0400;
            public const ushort c_STEP_JMC = 0x0800;
            public const ushort c_BREAK = 0x1000;
            public const ushort c_EVAL_COMPLETE = 0x2000;
            public const ushort c_EXCEPTION_UNWIND = 0x4000;
            public const ushort c_EXCEPTION_FINALLY = 0x8000;

            public const ushort c_STEP = c_STEP_IN | c_STEP_OUT | c_STEP_OVER;

            public const int c_PID_ANY = 0x7FFFFFFF;

            public const uint c_DEPTH_EXCEPTION_FIRST_CHANCE = 0x00000000;
            public const uint c_DEPTH_EXCEPTION_USERS_CHANCE = 0x00000001;
            public const uint c_DEPTH_EXCEPTION_HANDLER_FOUND = 0x00000002;

            public const uint c_DEPTH_STEP_NORMAL = 0x00000010;
            public const uint c_DEPTH_STEP_RETURN = 0x00000020;
            public const uint c_DEPTH_STEP_CALL = 0x00000030;
            public const uint c_DEPTH_STEP_EXCEPTION_FILTER = 0x00000040;
            public const uint c_DEPTH_STEP_EXCEPTION_HANDLER = 0x00000050;
            public const uint c_DEPTH_STEP_INTERCEPT = 0x00000060;
            public const uint c_DEPTH_STEP_EXIT = 0x00000070;

            public const uint c_DEPTH_UNCAUGHT = 0xFFFFFFFF;

            public short m_id;
            public ushort m_flags;

            public uint m_pid;
            public uint m_depth;

            //m_IPStart, m_IPEnd are used for optimizing stepping operations.  a STEP_IN | STEP_OVER breakpoint will be
            //hit in the given stack frame only if the IP is outside of the given range [m_IPStart m_IPEnd)  
            public uint m_IPStart;
            public uint m_IPEnd;

            public uint m_md;
            public uint m_IP;

            public uint m_td;

            public uint m_depthExceptionHandler;
        }

        public class Debugging_Execution_Breakpoints
        {
            public uint m_flags;

            public Debugging_Execution_BreakpointDef[] m_data;
        }

        public class Debugging_Execution_BreakpointHit
        {
            public Debugging_Execution_BreakpointDef m_hit;
        }

        public class Debugging_Execution_BreakpointStatus
        {
            public class Reply
            {
                public Debugging_Execution_BreakpointDef m_lastHit;
            }
        }


        //////////////////////////////////////////////////////////////////////////////////////
        // Keep in sync with Debugging_Execution_QueryCLRCapabilities struct in native code //
        //////////////////////////////////////////////////////////////////////////////////////

        public class Debugging_Execution_QueryCLRCapabilities
        {
            public const uint c_CapabilityFlags = 1;
            public const uint c_CapabilitySoftwareVersion = 3;
            public const uint c_CapabilityHalSystemInfo = 5;
            public const uint c_CapabilityClrInfo = 6;
            public const uint c_CapabilitySolutionReleaseInfo = 7;
            public const uint c_CapabilityInteropNativeAssemblies = 8;

            ///////////////////////////////////////////////////////////
            // because the number of deployed assemblies can make the size of the Wire Protocol package grow beyond the 
            // target max packet size, need to be able to query the assemblies in batches
            // at the same time need to keep backwards compatibility with the existing targets.
            public const uint c_CapabilityInteropNativeAssembliesCount = 9;

            //////////////////////////////////////////////////////////////////////////////////////
            // Keep in sync with Debugging_Execution_QueryCLRCapabilities struct in native code //
            //////////////////////////////////////////////////////////////////////////////////////
            public const uint c_CapabilityFlags_FloatingPoint = 0x00000001;
            public const uint c_CapabilityFlags_SourceLevelDebugging = 0x00000002;
            public const uint c_CapabilityFlags_AppDomains = 0x00000004;
            public const uint c_CapabilityFlags_ExceptionFilters = 0x00000008;
            public const uint c_CapabilityFlags_IncrementalDeployment = 0x00000010;
            public const uint c_CapabilityFlags_SoftReboot = 0x00000020;
            public const uint c_CapabilityFlags_Profiling = 0x00000040;
            public const uint c_CapabilityFlags_Profiling_Allocations = 0x00000080;
            public const uint c_CapabilityFlags_Profiling_Calls = 0x00000100;
            public const uint c_CapabilityFlags_ThreadCreateEx = 0x00000400;
            public const uint c_CapabilityFlags_ConfigBlockRequiresErase = 0x00000800;
            public const uint c_CapabilityFlags_HasNanoBooter = 0x00001000;
            public const uint c_CapabilityFlags_CanChangeMacAddress = 0x00002000;

            public const uint c_CapabilityFlags_PlatformCapabiliy_0 = 0x01000000;
            public const uint c_CapabilityFlags_PlatformCapabiliy_1 = 0x02000000;
            public const uint c_CapabilityFlags_PlatformCapabiliy_2 = 0x04000000;
            public const uint c_CapabilityFlags_PlatformCapabiliy_3 = 0x08000000;
            public const uint c_CapabilityFlags_PlatformCapabiliy_Mask = 0x0F000000;

            public const uint c_CapabilityFlags_TargetCapabiliy_0 = 0x10000000;
            public const uint c_CapabilityFlags_TargetCapabiliy_1 = 0x20000000;
            public const uint c_CapabilityFlags_TargetCapabiliy_2 = 0x40000000;
            public const uint c_CapabilityFlags_TargetCapabiliy_3 = 0x80000000;
            public const uint c_CapabilityFlags_TargetCapabiliy_Mask = 0xF0000000;

            //////////////////////////////////////////////////////////////////////////////////////
            //////////////////////////////////////////////////////////////////////////////////////

            public uint m_caps;

            public class Reply : IConverter
            {
                public byte[] m_data = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_data = new byte[size];
                }
            }

            public class SoftwareVersion
            {
                public byte[] BuildDate = new byte[22];
                public byte[] CompilerInfo = new byte[16];
                public uint CompilerVersion;
            }

            public class OEM_MODEL_SKU
            {
                public byte OEM;
                public byte Model;
                public ushort SKU;
            }

            public class OEM_SERIAL_NUMBERS : IConverter
            {
                public byte[] module_serial_number;
                public byte[] system_serial_number;

                public OEM_SERIAL_NUMBERS()
                {
                    module_serial_number = new byte[32];
                    system_serial_number = new byte[16];
                }

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    module_serial_number = new byte[32];
                    system_serial_number = new byte[16];
                }
            }

            public class HalSystemInfo : IConverter
            {
                public ReleaseInfo m_releaseInfo;
                public OEM_MODEL_SKU m_OemModelInfo;
                public OEM_SERIAL_NUMBERS m_OemSerialNumbers;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_releaseInfo = new ReleaseInfo();
                    m_OemModelInfo = new OEM_MODEL_SKU();
                    m_OemSerialNumbers = new OEM_SERIAL_NUMBERS();
                }
            }

            public class ClrInfo : IConverter
            {
                public ReleaseInfo m_clrReleaseInfo;
                public VersionStruct m_TargetFrameworkVersion;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_clrReleaseInfo = new ReleaseInfo();
                    m_TargetFrameworkVersion = new VersionStruct();
                }
            }

            public class NativeAssemblyDetails
            {
                /// <summary>
                /// size of NativeAssemblyDetails struct (4 + 4 * 2 + 128 * 1)
                /// </summary>
                public const int Size = (4 + 4 * 2 + 128 * 1);

                // the fields bellow have to follow the exact type and order so that the reply of the device can be properly parsed

                /////////////////////////////////////////////////////////////////////////////////////////
                // NEED TO KEEP THESE IN SYNC WITH native 'NativeAssemblyDetails' struct in Debugger.h //
                /////////////////////////////////////////////////////////////////////////////////////////

                /// <summary>
                /// Checksum of the assembly.
                /// </summary>
                public uint CheckSum;
                public VersionStruct AssemblyVersion;
                private readonly byte[] _name = new byte[128];

                ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                /// <summary>
                /// Name of the assembly.
                /// </summary>
                public string Name => GetZeroTerminatedString(_name, true);
            }

            public class NativeAssemblies : IConverter
            {
                public List<NativeAssemblyDetails> NativeInteropAssemblies;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    // find out how many items are in the reply array 
                    // size of the reply buffer divided by the size of NativeAssemblyDetails struct
                    int numOfAssemblies = size / NativeAssemblyDetails.Size;

                    NativeInteropAssemblies = Enumerable.Range(0, numOfAssemblies).Select(x => new NativeAssemblyDetails()).ToList();
                }
            }

        }

        public class Debugging_Execution_SetCurrentAppDomain
        {
            public uint m_id;
        }

        public class Debugging_Thread_Create
        {
            public uint m_md = 0;
            public int m_scratchPad = 0;

            public class Reply
            {
                public uint m_pid = 0;
            }
        }

        public class Debugging_Thread_CreateEx
        {
            public uint m_md = 0;
            public int m_scratchPad = 0;
            public uint m_pid = 0;

            public class Reply
            {
                public uint m_pid = 0;
            }
        }

        public class Debugging_Thread_List
        {
            public class Reply : IConverter
            {
                public uint m_num = 0;
                public uint[] m_pids = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_pids = new uint[(size - 4) / 4];
                }
            }
        }

        public class Debugging_Thread_Stack
        {
            public uint m_pid = 0;

            public class Reply : IConverter
            {
                #region Thread flags

                public const uint TH_S_Ready = 0x00000000;
                public const uint TH_S_Waiting = 0x00000001;
                public const uint TH_S_Terminated = 0x00000002;

                public const uint TH_F_Suspended = 0x00000001;
                public const uint TH_F_Aborted = 0x00000002;
                public const uint TH_F_Finalizer = 0x00000004;
                public const uint TH_F_ContainsDoomedAppDomain = 0x00000008;

                #endregion

                #region Stack Flags

                public const uint c_MethodKind_Native = 0x00000000;
                public const uint c_MethodKind_Interpreted = 0x00000001;
                public const uint c_MethodKind_Jitted = 0x00000002;
                public const uint c_MethodKind_Mask = 0x00000003;

                public const uint c_UNUSED_00000004 = 0x00000004;
                public const uint c_UNUSED_00000008 = 0x00000008;

                public const uint c_ExecutingConstructor = 0x00000010;
                public const uint c_CompactAndRestartOnOutOfMemory = 0x00000020;
                public const uint c_CallOnPop = 0x00000040;
                public const uint c_CalledOnPop = 0x00000080;

                public const uint c_NeedToSynchronize = 0x00000100;
                public const uint c_PendingSynchronize = 0x00000200;
                public const uint c_Synchronized = 0x00000400;
                public const uint c_UNUSED_00000800 = 0x00000800;

                public const uint c_NeedToSynchronizeGlobally = 0x00001000;
                public const uint c_PendingSynchronizeGlobally = 0x00002000;
                public const uint c_SynchronizedGlobally = 0x00004000;
                public const uint c_PseudoStackFrameForFilter = 0x00080000;

                public const uint c_ExecutingIL = 0x00010000;
                public const uint c_CallerIsCompatibleForCall = 0x00020000;
                public const uint c_CallerIsCompatibleForRet = 0x00040000;
                public const uint c_UNUSED_00080000 = 0x00080000;

                public const uint c_UNUSED_00100000 = 0x00100000;
                public const uint c_UNUSED_00200000 = 0x00200000;
                public const uint c_UNUSED_00400000 = 0x00400000;
                public const uint c_UNUSED_00800000 = 0x00800000;

                public const uint c_UNUSED_01000000 = 0x01000000;
                public const uint c_UNUSED_02000000 = 0x02000000;

                public const uint c_AppDomainMethodInvoke = 0x04000000;
                public const uint c_AppDomainInjectException = 0x08000000;
                public const uint c_AppDomainTransition = 0x10000000;
                public const uint c_InvalidIP = 0x20000000;
                public const uint c_ShouldInterceptException = 0x40000000;
                public const uint c_HasBreakpoint = 0x80000000;

                #endregion

                public class Call
                {
                    public uint m_md;
                    public uint m_IP;
                }

                public class CallEx : Call
                {
                    public uint m_appDomainID;
                    public uint m_flags;
                }

                public uint m_num = 0;
                public uint m_status = 0;
                public uint m_flags = 0;
                public Call[] m_data = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    if (converter.Capabilities.AppDomains)
                    {
                        m_data = new CallEx[(size - 12) / 16];
                    }
                    else
                    {
                        m_data = new Call[(size - 12) / 8];
                    }
                }
            }
        }

        public class Debugging_Thread_Kill
        {
            public uint m_pid = 0;

            public class Reply
            {
                public int m_result = 0;
            }
        }

        public class Debugging_Thread_Suspend
        {
            public uint m_pid = 0;
        }

        public class Debugging_Thread_Resume
        {
            public uint m_pid = 0;
        }

        public class Debugging_Thread_Get
        {
            public uint m_pid = 0;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Thread_GetException
        {
            public uint m_pid = 0;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Thread_Unwind
        {
            public uint m_pid = 0;
            public uint m_depth = 0;
        }

        public class Debugging_Stack_Info
        {
            public uint m_pid;
            public uint m_depth;

            public class Reply
            {
                public uint m_md;
                public uint m_IP;
                public uint m_numOfArguments;
                public uint m_numOfLocals;
                public uint m_depthOfEvalStack;
            }
        }

        public class Debugging_Stack_SetIP
        {
            public uint m_pid;
            public uint m_depth;

            public uint m_IP;
            public uint m_depthOfEvalStack;
        }

        public class Debugging_Value_Reply : IConverter
        {
            public Debugging_Value[] m_values;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                m_values = Debugging_Value.Allocate(size, data);
            }
        }

        public class Debugging_Value
        {
            public const uint HB_Alive = 0x01;
            public const uint HB_KeepAlive = 0x02;
            public const uint HB_Event = 0x04;
            public const uint HB_Pinned = 0x08;
            public const uint HB_Boxed = 0x10;
            public const uint HB_NeedFinalizer = 0x20;
            public const uint HB_Signaled = 0x40;
            public const uint HB_SignalAutoReset = 0x80;


            public uint m_referenceID;
            public uint m_dt;
            public uint m_flags;
            public uint m_size;

            public byte[] m_builtinValue = new byte[128];

            // For DATATYPE_STRING

            public uint m_bytesInString;
            public uint m_charsInString;

            // For DATATYPE_VALUETYPE or DATATYPE_CLASSTYPE

            public uint m_td;

            // For DATATYPE_SZARRAY

            public uint m_array_numOfElements;
            public uint m_array_depth;
            public uint m_array_typeIndex;

            // For values from an array.

            public uint m_arrayref_referenceID;
            public uint m_arrayref_index;


            static internal Debugging_Value[] Allocate(int size, byte[] data)
            {
                int num = size / (12 * 4 + 128);

                Debugging_Value[] res = new Debugging_Value[num];

                for (int i = 0; i < num; i++)
                {
                    res[i] = new Debugging_Value();
                }

                return res;
            }
        }

        public class Debugging_Value_ResizeScratchPad
        {
            public int m_size;
        }

        public class Debugging_Value_GetStack
        {
            public const uint c_Local = 0;
            public const uint c_Argument = 1;
            public const uint c_EvalStack = 2;

            public uint m_pid;
            public uint m_depth;
            public uint m_kind;
            public uint m_index;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_GetField
        {
            public uint m_heapblock;
            public uint m_offset;
            public uint m_fd;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_GetArray
        {
            public uint m_heapblock;
            public uint m_index;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_GetBlock
        {
            public uint m_heapblock;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_GetScratchPad
        {
            public int m_index;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_SetBlock
        {
            public uint m_heapblock;
            public uint m_dt;
            public byte[] m_value = new byte[8];
        }

        public class Debugging_Value_SetArray
        {
            public uint m_heapblock;
            public uint m_index;
            public byte[] m_value = new byte[8];   // Only primitive support for now
        }

        public class Debugging_Value_AllocateObject
        {
            public int m_index;
            public uint m_td;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_AllocateString
        {
            public int m_index;
            public uint m_size;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_AllocateArray
        {
            public int m_index;
            public uint m_td;
            public uint m_depth;
            public uint m_numOfElements;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Value_Assign
        {
            public uint m_heapblockSrc;
            public uint m_heapblockDst;

            // Reply is Debugging_Value_Reply
        }

        public class Debugging_Reply_Uint_Array : IConverter
        {
            public uint[] Data = null;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                Data = new uint[(size) / 4];
            }
        }

        public class Debugging_TypeSys_Assemblies
        {
            public class Reply : Debugging_Reply_Uint_Array
            {
            }
        }

        public class Debugging_TypeSys_AppDomains
        {
            public class Reply : Debugging_Reply_Uint_Array
            {
            }
        }

        public class Debugging_Resolve_Type
        {
            public uint m_td = 0;

            public class Reply
            {
                public byte[] m_type = new byte[512];
            }

            public class Result
            {
                public string m_name;
            }
        }

        public class Debugging_Resolve_Field
        {
            public uint m_fd = 0;

            public class Reply
            {
                public uint m_td = 0;
                public uint m_offset = 0;
                public byte[] m_name = new byte[512];
            }

            public class Result
            {
                public uint m_td;
                public uint m_offset;
                public string m_name;
            }
        }

        public class Debugging_Resolve_Method
        {
            public uint m_md = 0;

            public class Reply
            {
                public uint m_td;
                public byte[] m_method = new byte[512];
            }

            public class Result
            {
                public uint m_td;
                public string m_name;
            }
        }

        public class DebuggingResolveAssembly
        {
            public uint Idx = 0;

            [IgnoreDataMember]
            public Reply Result;

            public struct Version
            {
                public ushort MajorVersion;
                public ushort MinorVersion;
                public ushort BuildNumber;
                public ushort RevisionNumber;

                public override string ToString()
                {
                    return string.Format("{0}.{1}.{2}.{3}", MajorVersion, MinorVersion, BuildNumber, RevisionNumber);
                }
            }

            /// <summary>
            /// Resolved status for a deployed assembly.
            /// </summary>
            [Flags]
            public enum ResolvedStatus
            {
                //////////////////////////////////////////////////////////////////////////////////////////
                // NEED TO KEEP THESE IN SYNC WITH native 'CLR_RT_Assembly' struct in nanoCLR_Runtime.h //
                //////////////////////////////////////////////////////////////////////////////////////////
                Resolved = 0x00000001,
                Patched = 0x00000002,
                PreparedForExecution = 0x00000004,
                Deployed = 0x00000008,
                PreparingForExecution = 0x00000010,
            }

            public class Reply
            {
                ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                // the fields bellow have to be here AND follow the exact type and order so that the reply of the device can be properly parsed
                public uint Flags;
                public Version Version;
                public byte[] NameBuffer = new byte[512]; // char
                ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                [IgnoreDataMember]
                private string _name;
                [IgnoreDataMember]
                private string _path;

                private void EnsureName()
                {
                    if (_name == null)
                    {
                        string name = GetZeroTerminatedString(NameBuffer, false);
                        string path = null;

                        int iComma = name.IndexOf(',');

                        if (iComma >= 0)
                        {
                            path = name.Substring(iComma + 1);
                            name = name.Substring(0, iComma);
                        }

                        _name = name;
                        _path = path;
                    }
                }

                public string Name
                {
                    get
                    {
                        EnsureName();

                        return _name;
                    }
                }

                public string Path
                {
                    get
                    {
                        EnsureName();

                        return _path;
                    }
                }

                public ResolvedStatus Status
                {
                    get
                    {
                        return (ResolvedStatus)Enum.Parse(typeof(ResolvedStatus), Flags.ToString());
                    }
                }
            }
        }

        public class Debugging_Resolve_VirtualMethod
        {
            public uint m_md;
            public uint m_obj;

            public class Reply
            {
                public uint m_md;
            }
        }

        public class Debugging_Resolve_AppDomain
        {
            public static uint AppDomainState_Loaded = 0;
            public static uint AppDomainState_Unloading = 1;
            public static uint AppDomainState_Unloaded = 2;

            public uint m_id;

            public class Reply : IConverter
            {
                public uint m_state;
                public byte[] m_szName = new byte[512];
                public uint[] m_data = null;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    m_data = new uint[(size - 512 - 4) / 4];
                }

                public string Name
                {
                    get
                    {
                        return GetZeroTerminatedString(m_szName, false);
                    }
                }
            }
        }

        public class Debugging_Button_Report
        {
            public uint m_pressed;
            public uint m_released;
        }

        public class Debugging_Button_Inject
        {
            public uint m_pressed;
            public uint m_released;
        }

        public class Debugging_Messaging_Address
        {
            public const int c_size = 5 * 4;

            public uint m_seq;

            public uint m_from_Type;
            public uint m_from_Id;

            public uint m_to_Type;
            public uint m_to_Id;
        }

        public class Debugging_Messaging_Query
        {
            public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();

            public class Reply
            {
                public uint m_found = 0;
                public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();
            }
        }

        public class Debugging_Messaging_Send : IConverter
        {
            public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();
            public byte[] m_data = null;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                m_data = new byte[size - Debugging_Messaging_Address.c_size];
            }

            public class Reply
            {
                public uint m_found = 0;
                public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();
            }
        }

        public class Debugging_Messaging_Reply : IConverter
        {
            public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();
            public byte[] m_data = null;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                m_data = new byte[size - Debugging_Messaging_Address.c_size];
            }

            public class Reply
            {
                public uint m_found = 0;
                public Debugging_Messaging_Address m_addr = new Debugging_Messaging_Address();
            }
        }

        public class DebuggingDeploymentStatus
        {
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // the fields bellow have to be here AND follow the exact type and order so that the reply of the device can be properly parsed
            public struct FlashSector
            {
                public uint Start;
                public uint Length;
            };

            public class Reply
            {
                public uint EntryPoint;
                public uint StorageStart;
                public uint StorageLength;
            }

            public class ReplyEx : Reply, IConverter
            {
                public FlashSector[] SectorData;

                public void PrepareForDeserialize(int size, byte[] data, Converter converter)
                {
                    SectorData = new FlashSector[(size - 3 * 4)];
                }
            }
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        }

        public class Debugging_Info_SetJMC
        {
            public uint m_fIsJMC;
            public uint m_kind;

            public uint m_raw;
        }

        public class Profiling_Command
        {
            public const byte c_Command_ChangeConditions = 0x01;
            public const byte c_Command_FlushStream = 0x02;

            public class ChangeConditionsFlags
            {
                public const uint c_Enabled = 0x00000001;
                public const uint c_Allocations = 0x00000002;
                public const uint c_Calls = 0x00000004;
                public const uint c_nanoCLRTypes = 0x00000008;
            }

            public byte m_command;
            public uint m_parm1;
            public uint m_parm2;


            public class Reply
            {
                public uint m_raw = 0;
            }
        }

        public class Profiling_Stream : IConverter
        {
            public ushort seqId;
            public ushort bitLen;
            public byte[] payload;

            public void PrepareForDeserialize(int size, byte[] data, Converter converter)
            {
                payload = new byte[size - 4];
            }
        }

        internal static string GetZeroTerminatedString(byte[] buf, bool fUTF8)
        {
            if (buf == null) return null;

            int len = 0;
            int num = buf.Length;

            while (len < num && buf[len] != 0) len++;

            if (fUTF8) return Encoding.UTF8.GetString(buf, 0, len);
            else return Encoding.ASCII.GetString(buf, 0, len);
        }

        public static object ResolveCommandToPayload(uint cmd, bool fReply, CLRCapabilities capabilities)
        {
            if (fReply)
            {
                switch (cmd)
                {
                    case c_Monitor_Ping: return new Monitor_Ping.Reply();
                    case c_Monitor_OemInfo: return new Monitor_OemInfo.Reply();
                    case c_Monitor_TargetInfo: return new Monitor_TargetInfo.Reply();
                    case c_Monitor_ReadMemory: return new Monitor_ReadMemory.Reply();
                    case c_Monitor_WriteMemory: return new Monitor_WriteMemory.Reply();
                    case c_Monitor_CheckMemory: return new Monitor_CheckMemory.Reply();
                    case c_Monitor_EraseMemory: return new Monitor_EraseMemory.Reply();
                    case c_Monitor_MemoryMap: return new Monitor_MemoryMap.Reply();
                    case c_Monitor_DeploymentMap: return new Monitor_DeploymentMap.Reply();
                    case c_Monitor_FlashSectorMap: return new Monitor_FlashSectorMap.Reply();
                    case c_Monitor_QueryConfiguration: return new Monitor_QueryConfiguration.Reply();
                    case c_Monitor_UpdateConfiguration: return new Monitor_UpdateConfiguration.Reply();
                    case c_Monitor_StorageOperation: return new Monitor_StorageOperation.Reply();

                    case c_Debugging_Execution_BasePtr: return new Debugging_Execution_BasePtr.Reply();
                    case c_Debugging_Execution_ChangeConditions: return new DebuggingExecutionChangeConditions.Reply();
                    case c_Debugging_Execution_Allocate: return new Debugging_Execution_Allocate.Reply();
                    case c_Debugging_Execution_BreakpointStatus: return new Debugging_Execution_BreakpointStatus.Reply();
                    case c_Debugging_Execution_QueryCLRCapabilities: return new Debugging_Execution_QueryCLRCapabilities.Reply();

                    case c_Debugging_MFUpdate_Start: return new Debugging_MFUpdate_Start.Reply();
                    case c_Debugging_MFUpdate_AuthCmd: return new Debugging_MFUpdate_AuthCommand.Reply();
                    case c_Debugging_MFUpdate_Authenticate: return new Debugging_MFUpdate_Authenticate.Reply();
                    case c_Debugging_MFUpdate_GetMissingPkts: return new Debugging_MFUpdate_GetMissingPkts.Reply();
                    case c_Debugging_MFUpdate_AddPacket: return new Debugging_MFUpdate_AddPacket.Reply();
                    case c_Debugging_MFUpdate_Install: return new Debugging_MFUpdate_Install.Reply();

                    case c_Debugging_UpgradeToSsl: return new Debugging_UpgradeToSsl.Reply();

                    case c_Debugging_Thread_Create: return new Debugging_Thread_Create.Reply();
                    case c_Debugging_Thread_CreateEx: return new Debugging_Thread_CreateEx.Reply();
                    case c_Debugging_Thread_List: return new Debugging_Thread_List.Reply();
                    case c_Debugging_Thread_Stack: return new Debugging_Thread_Stack.Reply();
                    case c_Debugging_Thread_Kill: return new Debugging_Thread_Kill.Reply();
                    case c_Debugging_Thread_GetException: return new Debugging_Value_Reply();
                    case c_Debugging_Thread_Get: return new Debugging_Value_Reply();

                    case c_Debugging_Stack_Info: return new Debugging_Stack_Info.Reply();

                    case c_Debugging_Value_GetStack: return new Debugging_Value_Reply();
                    case c_Debugging_Value_GetField: return new Debugging_Value_Reply();
                    case c_Debugging_Value_GetArray: return new Debugging_Value_Reply();
                    case c_Debugging_Value_GetBlock: return new Debugging_Value_Reply();
                    case c_Debugging_Value_GetScratchPad: return new Debugging_Value_Reply();
                    case c_Debugging_Value_AllocateObject: return new Debugging_Value_Reply();
                    case c_Debugging_Value_AllocateString: return new Debugging_Value_Reply();
                    case c_Debugging_Value_AllocateArray: return new Debugging_Value_Reply();
                    case c_Debugging_Value_Assign: return new Debugging_Value_Reply();

                    case c_Debugging_TypeSys_Assemblies: return new Debugging_TypeSys_Assemblies.Reply();
                    case c_Debugging_TypeSys_AppDomains: return new Debugging_TypeSys_AppDomains.Reply();

                    case c_Debugging_Resolve_Type: return new Debugging_Resolve_Type.Reply();
                    case c_Debugging_Resolve_Field: return new Debugging_Resolve_Field.Reply();
                    case c_Debugging_Resolve_Method: return new Debugging_Resolve_Method.Reply();
                    case c_Debugging_Resolve_Assembly: return new DebuggingResolveAssembly.Reply();
                    case c_Debugging_Resolve_VirtualMethod: return new Debugging_Resolve_VirtualMethod.Reply();
                    case c_Debugging_Resolve_AppDomain: return new Debugging_Resolve_AppDomain.Reply();

                    case c_Debugging_Messaging_Query: return new Debugging_Messaging_Query.Reply();
                    case c_Debugging_Messaging_Send: return new Debugging_Messaging_Send.Reply();
                    case c_Debugging_Messaging_Reply: return new Debugging_Messaging_Reply.Reply();

                    case c_Debugging_Deployment_Status:
                        if (capabilities.IncrementalDeployment) return new DebuggingDeploymentStatus.ReplyEx();
                        else return new DebuggingDeploymentStatus.Reply();

                    case c_Profiling_Command: return new Profiling_Command.Reply();
                }
            }
            else
            {
                switch (cmd)
                {
                    case c_Monitor_Ping: return new Monitor_Ping();
                    case c_Monitor_Message: return new Monitor_Message();
                    case c_Monitor_ReadMemory: return new Monitor_ReadMemory();
                    case c_Monitor_WriteMemory: return new Monitor_WriteMemory();
                    case c_Monitor_CheckMemory: return new Monitor_CheckMemory();
                    case c_Monitor_EraseMemory: return new Monitor_EraseMemory();
                    case c_Monitor_Execute: return new Monitor_Execute();
                    case c_Monitor_MemoryMap: return new Monitor_MemoryMap();
                    case c_Monitor_Reboot: return new MonitorReboot();
                    case c_Monitor_DeploymentMap: return new Monitor_DeploymentMap();
                    case c_Monitor_FlashSectorMap: return new Monitor_FlashSectorMap();
                    case c_Monitor_QueryConfiguration: return new Monitor_QueryConfiguration();
                    case c_Monitor_StorageOperation: return new Monitor_StorageOperation();

                    case c_Debugging_Execution_BasePtr: return new Debugging_Execution_BasePtr();
                    case c_Debugging_Execution_ChangeConditions: return new DebuggingExecutionChangeConditions();
                    case c_Debugging_Execution_SecurityKey: return new Debugging_Execution_SecurityKey();
                    case c_Debugging_Execution_Unlock: return new Debugging_Execution_Unlock();
                    case c_Debugging_Execution_Allocate: return new Debugging_Execution_Allocate();
                    case c_Debugging_Execution_BreakpointHit: return new Debugging_Execution_BreakpointHit();
                    case c_Debugging_Execution_BreakpointStatus: return new Debugging_Execution_BreakpointStatus();
                    case c_Debugging_Execution_QueryCLRCapabilities: return new Debugging_Execution_QueryCLRCapabilities();
                    case c_Debugging_Execution_SetCurrentAppDomain: return new Debugging_Execution_SetCurrentAppDomain();

                    case c_Debugging_MFUpdate_Start: return new Debugging_MFUpdate_Start();
                    case c_Debugging_MFUpdate_AuthCmd: return new Debugging_MFUpdate_AuthCommand();
                    case c_Debugging_MFUpdate_Authenticate: return new Debugging_MFUpdate_Authenticate();
                    case c_Debugging_MFUpdate_GetMissingPkts: return new Debugging_MFUpdate_GetMissingPkts();
                    case c_Debugging_MFUpdate_AddPacket: return new Debugging_MFUpdate_AddPacket();
                    case c_Debugging_MFUpdate_Install: return new Debugging_MFUpdate_Install();

                    case c_Debugging_UpgradeToSsl: return new Debugging_UpgradeToSsl();

                    case c_Debugging_Thread_Create: return new Debugging_Thread_Create();
                    case c_Debugging_Thread_CreateEx: return new Debugging_Thread_CreateEx();
                    case c_Debugging_Thread_List: return new Debugging_Thread_List();
                    case c_Debugging_Thread_Stack: return new Debugging_Thread_Stack();
                    case c_Debugging_Thread_Kill: return new Debugging_Thread_Kill();
                    case c_Debugging_Thread_Suspend: return new Debugging_Thread_Suspend();
                    case c_Debugging_Thread_Resume: return new Debugging_Thread_Resume();
                    case c_Debugging_Thread_GetException: return new Debugging_Thread_GetException();
                    case c_Debugging_Thread_Unwind: return new Debugging_Thread_Unwind();
                    case c_Debugging_Thread_Get: return new Debugging_Thread_Get();

                    case c_Debugging_Stack_Info: return new Debugging_Stack_Info();
                    case c_Debugging_Stack_SetIP: return new Debugging_Stack_SetIP();

                    case c_Debugging_Value_ResizeScratchPad: return new Debugging_Value_ResizeScratchPad();
                    case c_Debugging_Value_GetStack: return new Debugging_Value_GetStack();
                    case c_Debugging_Value_GetField: return new Debugging_Value_GetField();
                    case c_Debugging_Value_GetArray: return new Debugging_Value_GetArray();
                    case c_Debugging_Value_GetBlock: return new Debugging_Value_GetBlock();
                    case c_Debugging_Value_GetScratchPad: return new Debugging_Value_GetScratchPad();
                    case c_Debugging_Value_SetBlock: return new Debugging_Value_SetBlock();
                    case c_Debugging_Value_SetArray: return new Debugging_Value_SetArray();
                    case c_Debugging_Value_AllocateObject: return new Debugging_Value_AllocateObject();
                    case c_Debugging_Value_AllocateString: return new Debugging_Value_AllocateString();
                    case c_Debugging_Value_AllocateArray: return new Debugging_Value_AllocateArray();
                    case c_Debugging_Value_Assign: return new Debugging_Value_Assign();

                    case c_Debugging_TypeSys_Assemblies: return new Debugging_TypeSys_Assemblies();
                    case c_Debugging_TypeSys_AppDomains: return new Debugging_TypeSys_AppDomains();

                    case c_Debugging_Resolve_Type: return new Debugging_Resolve_Type();
                    case c_Debugging_Resolve_Field: return new Debugging_Resolve_Field();
                    case c_Debugging_Resolve_Method: return new Debugging_Resolve_Method();
                    case c_Debugging_Resolve_Assembly: return new DebuggingResolveAssembly();
                    case c_Debugging_Resolve_VirtualMethod: return new Debugging_Resolve_VirtualMethod();
                    case c_Debugging_Resolve_AppDomain: return new Debugging_Resolve_AppDomain();

                    case c_Debugging_Button_Report: return new Debugging_Button_Report();
                    case c_Debugging_Button_Inject: return new Debugging_Button_Inject();

                    case c_Debugging_Messaging_Query: return new Debugging_Messaging_Query();
                    case c_Debugging_Messaging_Send: return new Debugging_Messaging_Send();
                    case c_Debugging_Messaging_Reply: return new Debugging_Messaging_Reply();

                    case c_Debugging_Deployment_Status: return new DebuggingDeploymentStatus();

                    case c_Debugging_Info_SetJMC: return new Debugging_Info_SetJMC();

                    case c_Profiling_Stream: return new Profiling_Stream();
                }
            }

            return null;
        }

        public abstract class OverheadBase
        {
            [IgnoreDataMember]
            public int Overhead
            {
                get
                {
                    return GetOverhead();
                }

                private set { }
            }

            protected OverheadBase()
            {
                Overhead = GetOverhead();
            }

            public abstract bool PrepareForSend(byte[] data, int length, int offset = 0);

            private int GetOverhead()
            {
                Converter c = new Converter();
                PrepareForSend(new byte[0], 0);

                return c.Serialize(this).Length;
            }
        }
    }
}
