//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Streams;

namespace nanoFramework.Tools.Debugger
{
    public abstract class NanoDeviceBase
    {
        /// <summary>
        /// nanoFramework debug engine.
        /// </summary>
        /// 
        public Engine DebugEngine { get; set; }

        /// <summary>
        /// Create a new debug engine for this nanoDevice.
        /// </summary>
        public void CreateDebugEngine()
        {
            DebugEngine = new Engine(this);
        }

        /// <summary>
        /// Transport to the device. 
        /// </summary>
        public TransportType Transport { get; set; }

        /// <summary>
        /// Port here this device is connected.
        /// </summary>
        public IPort ConnectionPort { get; set; }

        /// <summary>
        /// Id of the connection to the device.
        /// </summary>
        public string ConnectionId { get; internal set; }

        /// <summary>
        /// Device description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Target name.
        /// </summary>
        public string TargetName { get; internal set; }

        /// <summary>
        /// Target platform.
        /// </summary>
        public string Platform { get; internal set; }

        /// <summary>
        /// Device serial number (if define on the target).
        /// </summary>
        public string SerialNumber { get; internal set; }

        /// <summary>
        /// Version of nanoBooter.
        /// </summary>
        public Version BooterVersion
        {
            get
            {
                try
                {
                    return DebugEngine.TargetInfo.BooterVersion;
                }
                catch
                {
                    return new Version();
                }
            }
        }

        /// <summary>
        /// Version of nanoCLR.
        /// </summary>
        public Version ClrVersion
        {
            get
            {
                try
                {
                    return DebugEngine.TargetInfo.ClrVersion;
                }
                catch
                {
                    return new Version();
                }
            }
        }

        /// <summary>
        /// Detailed info about the NanoFramework device hardware, solution and CLR.
        /// </summary>
        public INanoFrameworkDeviceInfo DeviceInfo { get; internal set; }

        /// <summary>
        /// This indicates if the device has a proprietary bootloader.
        /// </summary>
        public bool HasProprietaryBooter
        {
            get
            {
                return DebugEngine != null && DebugEngine.HasProprietaryBooter;
            }
        }

        /// <summary>
        /// This indicates if the target device has nanoBooter.
        /// </summary>
        public bool HasNanoBooter
        {
            get
            {
                return DebugEngine != null && DebugEngine.HasNanoBooter;
            }
        }

        /// <summary>
        ///  This indicates if the target device is IFU capable.
        /// </summary>
        public bool IsIFUCapable
        {
            get
            {
                return DebugEngine != null && DebugEngine.IsIFUCapable;
            }
        }

        private readonly object m_serverCert = null;
        private readonly Dictionary<uint, string> m_execSrecHash = new Dictionary<uint, string>();
        private readonly Dictionary<uint, int> m_srecHash = new Dictionary<uint, int>();

        private readonly AutoResetEvent m_evtMicroBooter = new AutoResetEvent(false);
        private readonly AutoResetEvent m_evtMicroBooterError = new AutoResetEvent(false);
        private readonly ManualResetEvent m_evtMicroBooterStart = new ManualResetEvent(false);

        protected NanoDeviceBase()
        {
            DeviceInfo = new NanoFrameworkDeviceInfo(this);
        }

        private bool IsClrDebuggerEnabled
        {
            get
            {
                try
                {
                    if (DebugEngine.IsConnectedTonanoCLR)
                    {
                        return (DebugEngine.Capabilities.SourceLevelDebugging);
                    }
                }
                catch
                {
                }
                return false;
            }
        }

        public object OnProgress { get; private set; }

        public object DeviceBase { get; internal set; }

        public abstract void Disconnect();

        /// <summary>
        /// Get <see cref="INanoFrameworkDeviceInfo"/> from device.
        /// If the device information has been retrieved before this method returns the cached data, unless the force argument is true.
        /// </summary>
        /// <param name="force">Force retrieving the information from the device.</param>
        /// <returns>Return the <see cref="INanoFrameworkDeviceInfo"/> for this device.</returns>
        public INanoFrameworkDeviceInfo GetDeviceInfo(bool force = true)
        {
            // start by checking if we already have this available
            if (!DeviceInfo.Valid || force)
            {
                // seems to be invalid so get it from device
                NanoFrameworkDeviceInfo mfDeviceInfo = new NanoFrameworkDeviceInfo(this);
                mfDeviceInfo.GetDeviceInfo();

                DeviceInfo = mfDeviceInfo;
            }

            return DeviceInfo;
        }

        /// <summary>
        /// Attempts to communicate with the connected nanoFramework device
        /// </summary>
        /// <returns></returns>
        public PingConnectionType Ping()
        {
            if(DebugEngine == null)
            {
                return PingConnectionType.NoConnection;
            }

            Commands.Monitor_Ping.Reply reply = DebugEngine.GetConnectionSource();

            if (reply != null)
            {
                // there is a reply, so the device _has_ to be connected
                DebugEngine.IsConnected = true;

                switch (reply.Source)
                {
                    case Commands.Monitor_Ping.c_Ping_Source_NanoCLR:
                        return PingConnectionType.nanoCLR;

                    case Commands.Monitor_Ping.c_Ping_Source_NanoBooter:
                        return PingConnectionType.nanoBooter;
                }
            }

            return PingConnectionType.NoConnection;
        }

        /// <summary>
        /// Start address of the deployment block.
        /// Returns (-1) as invalid value if the address can't be retrieved from the device properties.
        /// </summary>
        public int GetDeploymentStartAddress()
        {
            if (DebugEngine != null)
            {
                return (int)DebugEngine.FlashSectorMap.FirstOrDefault(s =>
                {
                    return (s.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                }).m_StartAddress;
            }

            return -1;
        }

        /// <summary>
        /// Start address of the CLR block.
        /// Returns (-1) as invalid value if the address can't be retrieved from the device properties.
        /// </summary>
        public int GetClrStartAddress()
        {
            if (DebugEngine != null)
            {
                return (int)DebugEngine.FlashSectorMap.FirstOrDefault(s =>
                {
                    return (s.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                }).m_StartAddress;
            }

            return -1;
        }

        /// <summary>
        /// Attempt to establish a connection with nanoBooter (with reboot if necessary)
        /// </summary>
        /// <returns>true connection was made, false otherwise</returns>
        public async Task<bool> ConnectToNanoBooterAsync(CancellationToken cancellationToken)
        {
            bool ret = false;

            if (!await DebugEngine.ConnectAsync(1000, true)) return false;

            if (DebugEngine != null)
            {
                if (DebugEngine.ConnectionSource == ConnectionSource.nanoBooter) return true;

                try
                {
                    DebugEngine.RebootDevice(RebootOptions.EnterNanoBooter);

                    /////////////////////////////////////////
                    // FIXME
                    /////////////////////////////////////////
                    //// nanoBooter is only com port so
                    //if (Transport == TransportType.TcpIp)
                    //{
                    //    _DBG.PortDefinition pdTmp = m_port;

                    //    Disconnect();

                    //    try
                    //    {
                    //        m_port = m_portNanoBooter;

                    //        // digi takes forever to reset
                    //        if (!Connect(60000, true))
                    //        {
                    //            Console.WriteLine(Properties.Resources.ErrorUnableToConnectToNanoBooterSerial);
                    //            return false;
                    //        }
                    //    }
                    //    finally
                    //    {
                    //        m_port = pdTmp;
                    //    }
                    //}

                    bool fConnected = false;

                    for (int i = 0; i < 40; i++)
                    {
                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) return false;

                        if (DebugEngine == null)
                        {
                            CreateDebugEngine();
                        }

                        if (fConnected = await DebugEngine.ConnectAsync(1000, true, ConnectionSource.Unknown))
                        {
                            Commands.Monitor_Ping.Reply reply = DebugEngine.GetConnectionSource();

                            ret = (reply.Source == Commands.Monitor_Ping.c_Ping_Source_NanoBooter);

                            break;
                        }
                    }

                    if (!fConnected)
                    {
                        //Debug.WriteLine("Unable to connect to NanoBooter");
                    }
                }
                catch
                {
                    // need a catch all here because some targets re-enumerate the USB device and that makes it impossible to catch them here
                }
            }

            return ret;
        }

        /// <summary>
        /// Erases the deployment sectors of the connected nanoDevice
        /// </summary>
        /// <param name="options">Identifies which areas are to be erased</param>
        /// <param name="cancellationToken">Cancellation token to allow caller to cancel task</param>
        /// <param name="progress">Progress report of execution</param>
        /// <returns>Returns false if the erase fails, true otherwise
        /// Possible exceptions: MFUserExitException, MFDeviceNoResponseException
        /// </returns>
        public async Task<bool> EraseAsync(EraseOptions options, CancellationToken cancellationToken, IProgress<string> progress = null)
        {
            bool fReset = false;

            if (DebugEngine == null)
            {
                return false;
            }

            // check if the device is responsive
            if (Ping() == PingConnectionType.NoConnection)
            {
                // it's not, try reconnect
                if (!await DebugEngine.ConnectAsync(5000, true))
                {
                    return false;
                }
            }

            if (!IsClrDebuggerEnabled || 0 != (options & EraseOptions.Firmware))
            {
                fReset = (Ping() == PingConnectionType.nanoCLR);

                if (!await ConnectToNanoBooterAsync(cancellationToken))
                {
                    return false;
                }
            }

            if (DebugEngine.FlashSectorMap == null)
            {
                return false;
            }

            Commands.Monitor_Ping.Reply ping = DebugEngine.GetConnectionSource();

            long total = 0;
            long value = 0;

            bool isConnectedToCLR = ((ping != null) && (ping.Source == Commands.Monitor_Ping.c_Ping_Source_NanoCLR));

            if (isConnectedToCLR)
            {
                DebugEngine.PauseExecution();
            }

            List<Commands.Monitor_FlashSectorMap.FlashSectorData> eraseSectors = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();

            foreach (Commands.Monitor_FlashSectorMap.FlashSectorData flashSectorData in DebugEngine.FlashSectorMap)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                switch (flashSectorData.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK)
                {
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT:
                        if (EraseOptions.Deployment == (options & EraseOptions.Deployment))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_UPDATE:
                        if (EraseOptions.UpdateStorage == (options & EraseOptions.UpdateStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_A:
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_B:
                        if (EraseOptions.SimpleStorage == (options & EraseOptions.SimpleStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_A:
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_B:
                        if (EraseOptions.UserStorage == (options & EraseOptions.UserStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_FS:
                        if (EraseOptions.FileSystem == (options & EraseOptions.FileSystem))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG:
                        if (EraseOptions.Configuration == (options & EraseOptions.Configuration))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE:
                        if (EraseOptions.Firmware == (options & EraseOptions.Firmware))
                        {
                            eraseSectors.Add(flashSectorData);
                            total++;
                        }
                        break;
                }

            }

            foreach (Commands.Monitor_FlashSectorMap.FlashSectorData flashSectorData in eraseSectors)
            {
                for (int block = 0; block < flashSectorData.m_NumBlocks; block++)
                {
                    progress?.Report($"Erasing sector @ 0x{flashSectorData.m_StartAddress:X8}...");
                    
                    (AccessMemoryErrorCodes ErrorCode, bool Success) = DebugEngine.EraseMemory(
                        (uint)(flashSectorData.m_StartAddress + block * flashSectorData.m_BytesPerBlock),
                        flashSectorData.m_BytesPerBlock);

                    if (!Success)
                    {
                        progress?.Report($"Error erasing sector @ 0x{flashSectorData.m_StartAddress:X8}.");

                        return false;
                    }

                    // check the error code returned
                    if (ErrorCode != AccessMemoryErrorCodes.NoError)
                    {
                        // operation failed
                        progress?.Report($"Error erasing sector @ 0x{flashSectorData.m_StartAddress:X8}. Error code: {ErrorCode}.");

                        // don't bother continuing
                        return false;
                    }
                }
                value++;
            }

            // reset if we specifically entered nanoBooter to erase
            if (fReset)
            {
                DebugEngine.ExecuteMemory(0);
            }

            // reboot if we are talking to the CLR
            if (isConnectedToCLR)
            {
                progress?.Report("Rebooting...");

                RebootOptions rebootOptions = RebootOptions.ClrOnly;

                DebugEngine.RebootDevice(rebootOptions);
            }

            return true;
        }

        public async Task<bool> DeployUpdateAsync(StorageFile comprFilePath, CancellationToken cancellationToken, IProgress<string> progress = null)
        {
            if (DebugEngine.ConnectionSource == ConnectionSource.nanoCLR)
            {
                if (await DeployMFUpdateAsync(comprFilePath, cancellationToken, progress)) return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to deploy a binary (.bin) file to the connected nanoFramework device. 
        /// </summary>
        /// <param name="binFile">Path to the binary file (.bin).</param>
        /// <param name="address">Address to write to.</param>
        /// <returns>Returns false if the deployment fails, true otherwise.
        /// </returns>
        public async Task<bool> DeployBinaryFileAsync(
            string binFile,
            uint address,
            CancellationToken cancellationToken, 
            IProgress<string> progress = null)
        {
            // validate if file exists
            if(!File.Exists(binFile))
            {
                return false;
            }

            if (DebugEngine == null)
            {
                return false;
            }
            
            var data = File.ReadAllBytes(binFile);

            if (!await PrepareForDeployAsync(
                data,
                address,
                cancellationToken,
                progress))
            {
                return false;
            }

            progress?.Report($"Writing to device @ 0x{address:X8}...");

            if(!DeployFile(
                data,
                address,
                progress))
            {
                return false;
            }

            progress?.Report($"Verifying image...");

            if (!VerifyMemory(
                data,
                address,
                progress))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to deploy an SREC (.hex) file to the connected nanoFramework device. 
        /// </summary>
        /// <param name="srecFile">Path to the SREC file (.hex) file.</param>
        /// <returns>Returns <see langword="false"/> if the deployment fails, <see langword="true"/> otherwise.
        /// Also returns the entry point address for the given SREC file.
        /// </returns>
        // TODO this is not working most likely because of the format or parsing.
        private async Task<bool> DeploySrecFileAsync(
            string srecFile,
            CancellationToken cancellationToken,
            IProgress<string> progress = null)
        {
            // validate if file exists
            if (!File.Exists(srecFile))
            {
                return false;
            }

            if (DebugEngine == null)
            {
                return false;
            }

            List<SRecordFile.Block> blocks = SRecordFile.Parse(srecFile);

            if (blocks.Count > 0)
            {
                long total = 0;
                long value = 0;

                for (int i = 0; i < blocks.Count; i++)
                {
                    total += blocks[i].data.Length;
                }

                if(!await PrepareForDeployAsync(blocks, cancellationToken, progress))
                {
                    return false;
                }

                progress?.Report($"Deploying {Path.GetFileNameWithoutExtension(srecFile)}...");

                foreach (SRecordFile.Block block in blocks)
                {
                    uint addr = block.address;

                    // check if cancellation was requested 
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return false;
                    }

                    block.data.Seek(0, SeekOrigin.Begin);

                    byte[] data = new byte[block.data.Length];

                    if (!DeployFile(
                        data,
                        addr,
                        progress))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool DeployFile(
            byte[] buffer,
            uint address,
            IProgress<string> progress = null)
        {
            (AccessMemoryErrorCodes ErrorCode, bool Success) = DebugEngine.WriteMemory(address, buffer);
            if (!Success)
            {
                progress?.Report($"Error writing to device memory @ 0x{address:X8}, error {ErrorCode}.");

                return false;
            }

            return true;
        }

        private bool VerifyMemory(
            byte[] buffer,
            uint address,
            IProgress<string> progress = null)
        {
            if (!DebugEngine.CheckMemory(address, buffer))
            {
                progress?.Report($"Verification failed.");

                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts execution on the connected nanoDevice at the supplied address (parameter entrypoint).
        /// This method is generally used after the Deploy method to jump into the code that was deployed.
        /// </summary>
        /// <param name="entrypoint">Entry point address for execution to begin</param>
        /// <param name="cancellationToken">Cancellation token for caller to cancel task execution</param>
        /// <returns>Returns false if execution fails, true otherwise
        /// </returns>
        public async Task<bool> ExecuteAync(uint entryPoint, CancellationToken cancellationToken)
        {
            if (DebugEngine == null)
            {
                return false;
            }

            if (await CheckForMicroBooterAsync(cancellationToken))
            {
                // check if cancellation was requested 
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                if (m_execSrecHash.ContainsKey(entryPoint))
                {
                    string execRec = m_execSrecHash[entryPoint];
                    bool fRet = false;

                    for (int retry = 0; retry < 10; retry++)
                    {
                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }

                        try
                        {
                            await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes(execRec), TimeSpan.FromMilliseconds(1000), cancellationToken);

                            // check if cancellation was requested 
                            if (cancellationToken.IsCancellationRequested)
                            {
                                return false;
                            }

                            await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);
                        }
                        catch
                        {
                            // catch all, doesn't matter the return
                            return false;
                        }

                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested)
                        {
                            return false;
                        }

                        if (m_evtMicroBooter.WaitOne(1000))
                        {
                            fRet = true;
                            break;
                        }
                    }

                    return fRet;
                }

                return false;
            }

            Commands.Monitor_Ping.Reply reply = DebugEngine.GetConnectionSource();

            if (reply == null)
            {
                return false;
            }

            // only execute if we are talking to the nanoBooter, otherwise reboot
            if (reply.Source == Commands.Monitor_Ping.c_Ping_Source_NanoBooter)
            {
                return DebugEngine.ExecuteMemory(entryPoint);
            }
            else // if we are talking to the CLR then we simply did a deployment update, so reboot
            {
                DebugEngine.RebootDevice(RebootOptions.ClrOnly);
            }

            return true;
        }

        internal async Task<bool> CheckForMicroBooterAsync(CancellationToken cancellationToken)
        {
            if (DebugEngine == null) return false;

            try
            {
                m_evtMicroBooterStart.Set();
                m_evtMicroBooterError.Reset();

                // try to see if we are connected to MicroBooter
                for (int retry = 0; retry < 5; retry++)
                {
                    if (cancellationToken.IsCancellationRequested) return false;

                    await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("xx\n"), TimeSpan.FromMilliseconds(5000), cancellationToken);

                    if (m_evtMicroBooterError.WaitOne(100))
                    {
                        return true;
                    }
                }
            }
            finally
            {
                m_evtMicroBooterStart.Reset();
            }

            return false;
        }

        private async Task<bool> DeployMFUpdateAsync(StorageFile zipFile, CancellationToken cancellationToken, IProgress<string> progress = null)
        {
            if (zipFile.IsAvailable)
            {
                byte[] packet = new byte[DebugEngine.WireProtocolPacketSize];
                try
                {
                    int handle = -1;
                    int idx = 0;

                    Windows.Storage.FileProperties.BasicProperties fileInfo = await zipFile.GetBasicPropertiesAsync();
                    uint numPkts = (uint)(fileInfo.Size + DebugEngine.WireProtocolPacketSize - 1) / DebugEngine.WireProtocolPacketSize;

                    byte[] hashData = Encoding.UTF8.GetBytes(zipFile.Name + fileInfo.DateModified.ToString());

                    uint updateId = CRC.ComputeCRC(hashData, 0, hashData.Length, 0);
                    uint imageCRC = 0;

                    byte[] sig = null;

                    //Debug.WriteLine(updateId);

                    handle = DebugEngine.StartUpdate("NetMF", 4, 4, updateId, 0, 0, (uint)fileInfo.Size, DebugEngine.WireProtocolPacketSize, 0);
                    if (handle > -1)
                    {
                        uint authType;
                        IAsyncResult iar = null;

                        // perform request
                        (byte[] Response, bool Success) resp = DebugEngine.UpdateAuthCommand(handle, 1, null);

                        // check result
                        if (!resp.Success || resp.Response.Length < 4) return false;


                        using (MemoryStream ms = new MemoryStream(resp.Item1))
                        using (BinaryReader br = new BinaryReader(ms))
                        {
                            authType = br.ReadUInt32();
                        }


                        byte[] pubKey = null;

                        // FIXME
                        //if (m_serverCert != null)
                        //{

                        //    RSACryptoServiceProvider rsa = m_serverCert.PrivateKey as RSACryptoServiceProvider;

                        //    if (rsa != null)
                        //    {
                        //        pubKey = rsa.ExportCspBlob(false);
                        //    }
                        //}

                        if (!DebugEngine.UpdateAuthenticate(handle, pubKey))
                        {
                            return false;
                        }

                        // FIXME
                        //if (authType == 1 && m_serverCert != null)
                        //{
                        //    iar = await DebugEngine.UpgradeConnectionToSsl_Begin(m_serverCert, m_requireClientCert);

                        //    if (0 == WaitHandle.WaitAny(new WaitHandle[] { iar.AsyncWaitHandle, EventCancel }, 10000))
                        //    {
                        //        try
                        //        {
                        //            if (!m_eng.UpgradeConnectionToSSL_End(iar))
                        //            {
                        //                m_eng.Dispose();
                        //                m_eng = null;
                        //                return false;
                        //            }
                        //        }
                        //        catch
                        //        {
                        //            m_eng.Dispose();
                        //            m_eng = null;
                        //            return false;
                        //        }
                        //    }
                        //    else
                        //    {
                        //        return false;
                        //    }
                        //}

                        // FIXME
                        //RSAPKCS1SignatureFormatter alg = null;
                        object alg = null;
                        HashAlgorithmProvider hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
                        byte[] hashValue = null;

                        try
                        {
                            if (m_serverCert != null)
                            {
                                //alg = new RSAPKCS1SignatureFormatter(m_serverCert.PrivateKey);
                                //alg.SetHashAlgorithm("SHA1");
                                hash = HashAlgorithmProvider.OpenAlgorithm("SHA1");
                                hashValue = new byte[hash.HashLength / 8];
                            }
                        }
                        catch
                        {
                        }

                        IBuffer buffer = await FileIO.ReadBufferAsync(zipFile);
                        using (DataReader dataReader = DataReader.FromBuffer(buffer))
                        {
                            dataReader.ReadBytes(packet);

                            uint crc = CRC.ComputeCRC(packet, 0, packet.Length, 0);

                            if (!DebugEngine.AddPacket(handle, (uint)idx++, packet, CRC.ComputeCRC(packet, 0, packet.Length, 0))) return false;

                            imageCRC = CRC.ComputeCRC(packet, 0, packet.Length, imageCRC);

                            progress?.Report($"Deploying {idx}...");
                        }

                        if (hash != null)
                        {
                            buffer = await FileIO.ReadBufferAsync(zipFile);
                                             // hash it
                            IBuffer hashed = hash.HashData(buffer);
                            CryptographicBuffer.CopyToByteArray(hashed, out sig);
                        }

                        if (alg != null)
                        {
                            //sig = alg.CreateSignature(hash);
                            //CryptographicBuffer.CopyToByteArray(sig)
                        }
                        else
                        {
                            sig = new byte[4];
                            using (MemoryStream ms = new MemoryStream(sig))
                            using (BinaryWriter br = new BinaryWriter(ms))
                            {
                                br.Write(imageCRC);
                            }
                        }

                        if (DebugEngine.InstallUpdate(handle, sig))
                        {
                            return true;
                        }
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private async Task<Tuple<uint, bool>> DeploySRECAsync(StorageFile srecFile, CancellationToken cancellationToken)
        {
            m_srecHash.Clear();
            m_execSrecHash.Clear();

            // create .EXT file for SREC file
            StorageFolder folder = await srecFile.GetParentAsync();

            int m_totalSrecs = 0;
            uint m_minSrecAddr = uint.MaxValue;
            uint m_maxSrecAddr = 0;

            if (srecFile.IsAvailable)
            {
                // check is EXT file exists, if yes delete it
                StorageFile srecExtFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;
                if (srecExtFile != null)
                {
                    await srecExtFile.DeleteAsync();
                }

                if (await PreProcesSrecAsync(srecFile))
                {
                    srecExtFile = await folder.TryGetItemAsync(srecFile.Name.Replace(srecFile.FileType, "") + ".ext") as StorageFile;
                }

                // check if cancellation was requested 
                if (cancellationToken.IsCancellationRequested)
                {
                    new Tuple<uint, bool>(0, false);
                }

                SrecParseResult parsedFile = await ParseSrecFileAsync(srecExtFile);

                try
                {
                    int sleepTime = 5000;
                    UInt32 imageAddr = 0xFFFFFFFF;

                    m_totalSrecs = parsedFile.Records.Count;

                    //m_evtMicroBooterStart.Set();
                    //m_evtMicroBooter.Reset();
                    //m_evtMicroBooterError.Reset();

                    while (parsedFile.Records.Count > 0)
                    {
                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested)
                        {
                            new Tuple<uint, bool>(0, false);
                        }

                        List<uint> remove = new List<uint>();

                        const int c_MaxPipeline = 4;
                        int pipe = c_MaxPipeline;

                        uint[] keys = new uint[parsedFile.Records.Count];

                        parsedFile.Records.Keys.CopyTo(keys, 0);

                        Array.Sort(keys);

                        if (keys[0] < imageAddr) imageAddr = keys[0];

                        foreach (uint key in keys)
                        {
                            // check if cancellation was requested 
                            if (cancellationToken.IsCancellationRequested)
                            {
                                new Tuple<uint, bool>(0, false);
                            }

                            if (key < m_minSrecAddr) m_minSrecAddr = key;
                            if (key > m_maxSrecAddr) m_maxSrecAddr = key;
                            if (m_srecHash.ContainsKey(key))
                            {
                                remove.Add(key);
                                continue;
                            }

                            await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);

                            await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes(parsedFile.Records[key]), TimeSpan.FromMilliseconds(20000), cancellationToken);

                            await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);

                            if (pipe-- <= 0)
                            {
                                //m_evtMicroBooter.WaitOne(sleepTime);
                                pipe = c_MaxPipeline;
                            }
                        }

                        int cnt = remove.Count;

                        if (cnt > 0)
                        {
                            for (int i = 0; i < cnt; i++)
                            {
                                parsedFile.Records.Remove(remove[i]);
                            }
                        }
                    }

                    if (imageAddr != 0)
                    {
                        string basefile = Path.GetFileNameWithoutExtension(srecFile.Name);

                        // srecfile might be .bin.ext (for srec updates)
                        if (!string.IsNullOrEmpty(Path.GetExtension(basefile)))
                        {
                            basefile = Path.GetFileNameWithoutExtension(basefile);
                        }

                        string path = folder.Path;
                        string binFilePath = "";
                        string symdefFilePath = "";

                        if (folder.Path.ToLower().EndsWith("\\nanoCLR.hex"))
                        {
                            binFilePath = Path.GetDirectoryName(path) + "\\nanoCLR.bin\\" + basefile;
                            symdefFilePath = Path.GetDirectoryName(path) + "\\nanoCLR.symdefs";
                        }
                        else
                        {
                            binFilePath = Path.GetDirectoryName(srecFile.Path) + "\\" + basefile + ".bin";
                            symdefFilePath = Path.GetDirectoryName(srecFile.Path) + "\\" + basefile + ".symdefs";
                        }

                        StorageFile binFile = await folder.TryGetItemAsync(binFilePath) as StorageFile;

                        StorageFile symdefFile = await folder.TryGetItemAsync(symdefFilePath) as StorageFile;

                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested)
                        {
                            new Tuple<uint, bool>(0, false);
                        }

                        // send image crc
                        if (binFile != null && symdefFile != null)
                        {
                            Windows.Storage.FileProperties.BasicProperties fileInfo = await binFile.GetBasicPropertiesAsync();

                            UInt32 imageCRC = 0;

                            // read lines from SREC file
                            IList<string> textLines = await FileIO.ReadLinesAsync(symdefFile);

                            foreach (string line in textLines)
                            {
                                // check if cancellation was requested 
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    new Tuple<uint, bool>(0, false);
                                }

                                if (line.Contains("LOAD_IMAGE_CRC"))
                                {
                                    int idxEnd = line.IndexOf(' ', 2);
                                    imageCRC = UInt32.Parse(line.Substring(2, idxEnd - 2), System.Globalization.NumberStyles.HexNumber);
                                }
                            }

                            m_execSrecHash[parsedFile.EntryPoint] = string.Format("<CRC>{0:X08},{1:X08},{2:X08},{3:X08}</CRC>\n", imageAddr, fileInfo.Size, imageCRC, parsedFile.EntryPoint);
                        }
                    }

                    return new Tuple<uint, bool>(parsedFile.EntryPoint, true);
                }
                finally
                {
                    //m_evtMicroBooterStart.Reset();
                }
            }

            return new Tuple<uint, bool>(0, false);
        }

        private async Task<SrecParseResult> ParseSrecFileAsync(StorageFile srecFile)
        {
            SrecParseResult reply = new SrecParseResult();

            Dictionary<uint, string> recs = new Dictionary<uint, string>();

            try
            {
                int total = 0;

                IList<string> textLines = await FileIO.ReadLinesAsync(srecFile);

                foreach (string line in textLines)
                {
                    string addr = line.Substring(4, 8);

                    // we only support s7, s3 records
                    if (line.ToLower().StartsWith("s7"))
                    {
                        reply.EntryPoint = uint.Parse(addr, System.Globalization.NumberStyles.HexNumber);
                    }
                    else if (line.ToLower().StartsWith("s3"))
                    {
                        total += line.Length - 14;
                        reply.Records[uint.Parse(addr, System.Globalization.NumberStyles.HexNumber)] = line;
                    }
                }

                reply.ImageSize = (uint)total;
            }
            catch
            {
                return null;
            }

            return reply;
        }

        private async Task<bool> PreProcesSrecAsync(StorageFile srecFile)
        {
            if (!srecFile.IsAvailable) return false;

            // create .EXT file for SREC file
            StorageFolder folder = await srecFile.GetParentAsync();

            try
            {
                // read lines from SREC file
                IList<string> textLines = await FileIO.ReadLinesAsync(srecFile);

                StorageFile srecExtFile = await folder.CreateFileAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext", CreationCollisionOption.ReplaceExisting);

                const int c_MaxRecords = 8;
                int iRecord = 0;
                int currentCRC = 0;
                int iDataLength = 0;
                string s7rec = "";
                StringBuilder sb = new StringBuilder();

                foreach (string line in textLines)
                {
                    // we only support s7, s3 records
                    if (line.ToLower().StartsWith("s7"))
                    {
                        s7rec = line;
                        continue;
                    }

                    if (!line.ToLower().StartsWith("s3")) continue;

                    string crcData;

                    if (iRecord == 0)
                    {
                        crcData = line.Substring(4, line.Length - 6);
                    }
                    else
                    {
                        crcData = line.Substring(12, line.Length - 14);
                    }

                    iDataLength += crcData.Length / 2; // 2 chars per byte

                    if (iRecord == 0)
                    {
                        sb.Append(line.Substring(0, 2));
                    }
                    sb.Append(crcData);

                    iRecord++;

                    for (int i = 0; i < crcData.Length - 1; i += 2)
                    {
                        currentCRC += Byte.Parse(crcData.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
                    }

                    if (iRecord == c_MaxRecords)
                    {
                        iDataLength += 1; // crc

                        sb = sb.Insert(2, string.Format("{0:X02}", iDataLength));

                        currentCRC += (iDataLength & 0xFF) + ((iDataLength >> 8) & 0xFF);

                        // write crc
                        sb.Append(string.Format("{0:X02}", (0xFF - (0xFF & currentCRC))));

                        await FileIO.WriteTextAsync(srecExtFile, sb.ToString());

                        currentCRC = 0;
                        iRecord = 0;
                        iDataLength = 0;
                        sb.Length = 0;
                    }
                }

                if (iRecord != 0)
                {
                    iDataLength += 1; // crc

                    sb = sb.Insert(2, string.Format("{0:X02}", iDataLength));

                    currentCRC += (iDataLength & 0xFF) + ((iDataLength >> 8) & 0xFF);

                    // write crc
                    sb.Append(string.Format("{0:X02}", (0xFF - (0xFF & currentCRC))));

                    await FileIO.WriteTextAsync(srecExtFile, sb.ToString());
                }

                if (s7rec != "")
                {
                    await FileIO.WriteTextAsync(srecExtFile, s7rec);
                }
            }
            catch
            {
                StorageFile thisFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;

                if (thisFile != null)
                {
                    await thisFile.DeleteAsync();
                }

                return false;
            }

            return true;
        }

        private async Task<bool> PrepareForDeployAsync(
            byte[] buffer,
            uint address,
            CancellationToken cancellationToken,
            IProgress<string> progress = null)
        {
            return await PrepareForDeployAsync(
                buffer,
                address,
                null,
                cancellationToken,
                progress);
        }

        private async Task<bool> PrepareForDeployAsync(
            List<SRecordFile.Block> blocks,
            CancellationToken cancellationToken,
            IProgress<string> progress = null)
        {
            return await PrepareForDeployAsync(
                null,
                0,
                blocks,
                cancellationToken,
                progress);
        }

        private async Task<bool> PrepareForDeployAsync(
            byte[] buffer,
            uint address,
            List<SRecordFile.Block> blocks, 
            CancellationToken cancellationToken, 
            IProgress<string> progress = null)
        {
            // make sure we are connected
            if(!await DebugEngine.ConnectAsync(5000, true))
            {
                return false;
            }

            // get flash sector map, only if needed
            List<Commands.Monitor_FlashSectorMap.FlashSectorData> flashSectorsMap = DebugEngine.FlashSectorMap;

            // sanity check
            if (flashSectorsMap == null ||
                flashSectorsMap.Count == 0)
            {
                return false;
            }

            // validate deployment
            bool updatesDeployment = false;
            bool updatesClr = false;
            bool updatesBooter = false;

            long totalLength;

            if (blocks != null)
            {
                foreach (SRecordFile.Block bl in blocks)
                {
                    var startSector = flashSectorsMap.Find(s => s.m_StartAddress == bl.address);
                    if (startSector.m_NumBlocks > 0)
                    {
                        updatesDeployment ^= (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                        updatesClr ^= (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                        updatesBooter ^= (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP;
                    }
                }

                totalLength = blocks.Sum(b => b.data.Length);
            }
            else
            {
                var startSector = flashSectorsMap.Find(s => s.m_StartAddress == address);
                if (startSector.m_NumBlocks > 0)
                {
                    updatesDeployment = (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                    updatesClr = (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                    updatesBooter = (startSector.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP;
                }

                totalLength = buffer.Length;
            }

            // sanity check
            if (updatesBooter)
            {
                // can't handle this update because it touches nanoBooter
                return false;
            }

            if(
                !updatesDeployment &&
                !updatesClr &&
                !updatesBooter)
            {
                // nothing to update???
                return false;
            }

            if (updatesClr &&
                DebugEngine.ConnectionSource != ConnectionSource.nanoBooter)
            {
                // if this is updating the CLR need to launch nanoBooter
                await ConnectToNanoBooterAsync(cancellationToken);
            }

            // erase whatever blocks are required
            if (updatesClr)
            {
                if (!await EraseAsync(
                    EraseOptions.Firmware,
                    cancellationToken,
                    progress))
                {
                    return false;
                }
            }

            if (updatesDeployment)
            {
                if (!await EraseAsync(
                    EraseOptions.Deployment, 
                    cancellationToken, 
                    progress))
                {
                    return false;
                }
            }

            if (DebugEngine.ConnectionSource == ConnectionSource.nanoCLR)
            {
                DebugEngine.PauseExecution();
            }

            return true;
        }
    }
}
