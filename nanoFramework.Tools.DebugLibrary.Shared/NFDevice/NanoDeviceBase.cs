// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.PortTcpIp;
using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;

namespace nanoFramework.Tools.Debugger
{
    [AddINotifyPropertyChangedInterface]
    public abstract partial class NanoDeviceBase
    {
        /// <summary>
        /// nanoFramework debug engine.
        /// </summary>
        public Engine DebugEngine { get; set; }

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
        public string ConnectionId { get; set; }

        /// <summary>
        /// Device description.
        /// </summary>
        [DependsOn(nameof(TargetName), nameof(ConnectionId))]
        public string Description => $"{TargetName} @ {ConnectionId}";

        /// <summary>
        /// Target name.
        /// </summary>
        public string TargetName { get; set; }

        /// <summary>
        /// Target platform.
        /// </summary>
        public string Platform { get; set; }

        /// <summary>
        /// Device serial number (if defined on the target).
        /// </summary>
        public string SerialNumber { get; set; }

        /// <summary>
        /// Unique ID of the NanoDevice.
        /// </summary>
        public Guid DeviceUniqueId { get; set; }

        /// <summary>
        /// Detailed info about the NanoFramework device hardware, solution and CLR.
        /// </summary>
        public INanoFrameworkDeviceInfo DeviceInfo { get; protected internal set; }

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
        public virtual Version CLRVersion
        {
            get
            {
                try
                {
                    return DebugEngine.TargetInfo.CLRVersion;
                }
                catch
                {
                    return new Version();
                }
            }
        }

        /// <summary>
        /// This indicates if the device has a proprietary bootloader.
        /// </summary>
        public bool HasProprietaryBooter => DebugEngine != null && DebugEngine.HasProprietaryBooter;

        /// <summary>
        /// This indicates if the target device has nanoBooter.
        /// </summary>
        public bool HasNanoBooter => DebugEngine != null && DebugEngine.HasNanoBooter;

        /// <summary>
        /// This indicates if the target device is IFU capable.
        /// </summary>
        public bool IsIFUCapable => DebugEngine != null && DebugEngine.IsIFUCapable;

        private readonly object m_serverCert = null;
        private readonly Dictionary<uint, string> m_execSrecHash = new Dictionary<uint, string>();
        private readonly Dictionary<uint, int> m_srecHash = new Dictionary<uint, int>();

        private readonly AutoResetEvent m_evtMicroBooter = new AutoResetEvent(false);
        private readonly AutoResetEvent m_evtMicroBooterError = new AutoResetEvent(false);
        private readonly ManualResetEvent m_evtMicroBooterStart = new ManualResetEvent(false);

        private bool IsCLRDebuggerEnabled
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

        protected NanoDeviceBase()
        {
            DeviceInfo = new NanoFrameworkDeviceInfo(this);

            DeviceUniqueId = Guid.NewGuid();
        }

        public abstract void Disconnect(bool force = false);

        /// <summary>
        /// Creates a new debug engine for this nanoDevice.
        /// Transport to the device. 
        /// </summary>
        public void CreateDebugEngine()
        {
            DebugEngine = new Engine(this);

            DebugEngine.DefaultTimeout = Transport switch
            {
                TransportType.Serial => NanoSerialDevice.SafeDefaultTimeout,
                TransportType.TcpIp => NanoNetworkDevice.SafeDefaultTimeout,
                _ => throw new NotImplementedException()
            };
        }

        /// <summary>
        /// Creates a new debug engine for this nanoDevice.
        /// Transport to the device. 
        /// </summary>
        /// <param name="timeoutMilliseconds"></param>
        public void CreateDebugEngine(int timeoutMilliseconds)
        {
            DebugEngine = new Engine(this);
            DebugEngine.DefaultTimeout = NanoSerialDevice.SafeDefaultTimeout;
        }

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
                NanoFrameworkDeviceInfo nfDeviceInfo = new NanoFrameworkDeviceInfo(this);
                nfDeviceInfo.GetDeviceInfo();

                DeviceInfo = nfDeviceInfo;
            }

            return DeviceInfo;
        }

        /// <summary>
        /// Attempts to communicate with the connected nanoFramework device
        /// </summary>
        /// <returns></returns>
        public ConnectionSource Ping()
        {
            if (DebugEngine == null)
            {
                return ConnectionSource.Unknown;
            }

            return DebugEngine.GetConnectionSource();
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
                    return (s.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                }).StartAddress;
            }

            return -1;
        }

        /// <summary>
        /// Start address of the CLR block.
        /// Returns (-1) as invalid value if the address can't be retrieved from the device properties.
        /// </summary>
        public int GetCLRStartAddress()
        {
            if (DebugEngine != null)
            {
                return (int)DebugEngine.FlashSectorMap.FirstOrDefault(s =>
                {
                    return (s.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                }).StartAddress;
            }

            return -1;
        }

        /// <summary>
        /// Attempt to establish a connection with nanoBooter (with reboot if necessary)
        /// </summary>
        /// <returns>true connection was made, false otherwise</returns>
        public bool ConnectToNanoBooter()
        {
            bool ret = false;

            if (!DebugEngine.Connect(1000, true))
            {
                return false;
            }

            if (DebugEngine != null)
            {
                if (DebugEngine.IsConnectedTonanoBooter) return true;

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
                        if (DebugEngine == null)
                        {
                            CreateDebugEngine();
                        }

                        if (fConnected = DebugEngine.Connect(
                            true))
                        {
                            ret = (DebugEngine.GetConnectionSource() == ConnectionSource.nanoBooter);

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
        /// Erases the deployment sectors of the connected <see cref="NanoDevice"/>.
        /// </summary>
        /// <param name="options">Identifies which areas are to be erased.</param>
        /// <param name="progress">Progress report of execution.</param>
        /// <param name="log">Progress report of execution</param>
        /// <returns>Returns false if the erase fails, true otherwise
        /// Possible exceptions: MFUserExitException, MFDeviceNoResponseException
        /// </returns>
        public bool Erase(
            EraseOptions options,
            IProgress<MessageWithProgress> progress = null,
            IProgress<string> log = null)
        {
            bool requestBooter = false;

            if (DebugEngine == null)
            {
                return false;
            }

            if (!IsCLRDebuggerEnabled || 0 != (options & EraseOptions.Firmware))
            {
                log?.Report("Connecting to nanoBooter...");

                if (DebugEngine.IsConnectedTonanoCLR)
                {
                    // executing CLR so need to reset to nanoBooter
                    if (!ConnectToNanoBooter())
                    {
                        log?.Report("*** ERROR: request to connect to nanoBooter failed ***");

                        return false;
                    }

                    // flag request to launch nanoBooter 
                    requestBooter = true;
                }
            }

            if (DebugEngine.FlashSectorMap.Count == 0)
            {
                log?.Report("*** ERROR: device flash map not available, aborting ***");

                return false;
            }

            if (DebugEngine.IsConnectedTonanoCLR)
            {
                var deviceState = DebugEngine.GetExecutionMode();

                if (deviceState == Commands.DebuggingExecutionChangeConditions.State.Unknown)
                {
                    log?.Report("*** ERROR: failed to retrieve device execution state ***");

                    return false;
                }

                if (!deviceState.IsDeviceInStoppedState())
                {
                    log?.Report("Connected to CLR. Pausing execution...");

                    if (!DebugEngine.PauseExecution())
                    {
                        log?.Report("*** ERROR: failed to stop execution ***");

                        return false;
                    }
                }
            }

            List<Commands.Monitor_FlashSectorMap.FlashSectorData> eraseSectors = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();

            // need to use a foreach loop here because EraseOptions can contain multiple options 
            foreach (Commands.Monitor_FlashSectorMap.FlashSectorData flashSectorData in DebugEngine.FlashSectorMap)
            {
                switch (flashSectorData.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK)
                {
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT:
                        if (EraseOptions.Deployment == (options & EraseOptions.Deployment))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_UPDATE:
                        if (EraseOptions.UpdateStorage == (options & EraseOptions.UpdateStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_A:
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_SIMPLE_B:
                        if (EraseOptions.SimpleStorage == (options & EraseOptions.SimpleStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_A:
                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_STORAGE_B:
                        if (EraseOptions.UserStorage == (options & EraseOptions.UserStorage))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_FS:
                        if (EraseOptions.FileSystem == (options & EraseOptions.FileSystem))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG:
                        if (EraseOptions.Configuration == (options & EraseOptions.Configuration))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;

                    case Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE:
                        if (EraseOptions.Firmware == (options & EraseOptions.Firmware))
                        {
                            eraseSectors.Add(flashSectorData);
                        }
                        break;
                }

            }

            uint totalBytes = (uint)eraseSectors.Sum(s => s.NumBlocks * s.BytesPerBlock);
            uint current = 0;

            foreach (Commands.Monitor_FlashSectorMap.FlashSectorData flashSectorData in eraseSectors)
            {
                if (DebugEngine.IsConnectedTonanoCLR)
                {
                    // connected to CLR, OK to erase per sector

                    var sectorSize = flashSectorData.NumBlocks * flashSectorData.BytesPerBlock;

                    log?.Report($"Erasing sector @ 0x{flashSectorData.StartAddress:X8}...");
                    progress?.Report(new MessageWithProgress($"Erasing sector @ 0x{flashSectorData.StartAddress:X8}...", current, totalBytes));

                    (AccessMemoryErrorCodes ErrorCode, bool Success) = DebugEngine.EraseMemory(
                        flashSectorData.StartAddress,
                        sectorSize);

                    if (!Success)
                    {
                        log?.Report($"Error erasing sector @ 0x{flashSectorData.StartAddress:X8}.");
                        progress?.Report(new MessageWithProgress(""));

                        // don't bother continuing
                        return false;
                    }

                    // check the error code returned
                    if (ErrorCode != AccessMemoryErrorCodes.NoError)
                    {
                        // operation failed
                        log?.Report($"Error erasing sector @ 0x{flashSectorData.StartAddress:X8}. Error code: {ErrorCode}.");
                        progress?.Report(new MessageWithProgress(""));

                        // don't bother continuing
                        return false;
                    }

                    current += sectorSize;
                }
                else
                {
                    // connected to nanoBooter so need to erase per sector
                    for (int blockIndex = 0; blockIndex < flashSectorData.NumBlocks; blockIndex++)
                    {
                        var blockAddress = flashSectorData.StartAddress + (blockIndex * flashSectorData.BytesPerBlock);

                        log?.Report($"Erasing block @ 0x{blockAddress:X8}...");
                        progress?.Report(new MessageWithProgress($"Erasing block @ 0x{blockAddress:X8}...", current, totalBytes));

                        (AccessMemoryErrorCodes ErrorCode, bool Success) = DebugEngine.EraseMemory((uint)blockAddress,
                                                                                                   flashSectorData.BytesPerBlock);

                        if (!Success)
                        {
                            log?.Report($"Error erasing block @ 0x{blockAddress:X8}.");
                            progress?.Report(new MessageWithProgress(""));

                            // don't bother continuing
                            return false;
                        }

                        // check the error code returned
                        if (ErrorCode != AccessMemoryErrorCodes.NoError)
                        {
                            // operation failed
                            log?.Report($"Error erasing block @ 0x{blockAddress:X8}. Error code: {ErrorCode}.");
                            progress?.Report(new MessageWithProgress(""));

                            // don't bother continuing
                            return false;
                        }

                        current += flashSectorData.BytesPerBlock;
                    }
                }
            }

            progress?.Report(new MessageWithProgress(""));

            // reset device if we specifically entered nanoBooter to erase
            if (requestBooter)
            {
                log?.Report("Rebooting...");
                progress?.Report(new MessageWithProgress("Rebooting..."));

                DebugEngine.RebootDevice(RebootOptions.NormalReboot, log);
            }
            else if (DebugEngine.IsConnectedTonanoCLR)
            {
                // reboot if we are talking to the CLR

                log?.Report("Rebooting nanoCLR...");
                progress?.Report(new MessageWithProgress("Rebooting nanoCLR..."));

                DebugEngine.RebootDevice(RebootOptions.ClrOnly, log);
            }

            progress?.Report(new MessageWithProgress(""));

            return true;
        }

        //public bool DeployUpdateAsync(StorageFile comprFilePath, CancellationToken cancellationToken, IProgress<string> progress = null)
        //{
        //    if (DebugEngine.IsConnectedTonanoCLR)
        //    {
        //        if (await DeployMFUpdateAsync(comprFilePath, cancellationToken, progress))
        //        {
        //            return true;
        //        }
        //    }

        //    return false;
        //}

        /// <summary>
        /// Attempts to deploy a binary (.bin) file to the connected nanoFramework device. 
        /// </summary>
        /// <param name="binFile">Path to the binary file (.bin).</param>
        /// <param name="address">Address to write to.</param>
        /// <returns>Returns false if the deployment fails, true otherwise.
        /// </returns>
        /// <remarks>
        /// To perform the update the device has to be running:
        /// - nanoCLR if this is meant to update the deployment region.
        /// - nanoBooter if this is meant to update nanoCLR
        /// Failing to meet this condition will abort the operation.
        /// </remarks>
        public bool DeployBinaryFile(
            string binFile,
            uint address,
            IProgress<string> progress = null)
        {
            // validate if file exists
            if (!File.Exists(binFile))
            {
                return false;
            }

            if (DebugEngine == null)
            {
                return false;
            }

            var data = File.ReadAllBytes(binFile);

            if (!PrepareForDeploy(
                address,
                progress))
            {
                return false;
            }

            if (!DeployFile(
                data,
                address,
                0,
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
        private bool DeploySrecFile(
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

                if (!PrepareForDeploy(blocks, progress))
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
                        0,
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
            int programAligment = 0,
            IProgress<string> progress = null)
        {
            AccessMemoryErrorCodes errorCode = DebugEngine.WriteMemory(
                address,
                buffer,
                programAligment);

            if (errorCode != AccessMemoryErrorCodes.NoError)
            {
                progress?.Report($"Error writing to device memory @ 0x{address:X8}, error {errorCode}.");

                return false;
            }

            return true;
        }

        private bool VerifyMemory(
            byte[] buffer,
            uint address,
            IProgress<string> progress = null)
        {
            if (!DebugEngine.PerformWriteMemoryCheck(address, buffer))
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
        /// <returns>Returns false if execution fails, true otherwise
        /// </returns>
        public bool Execute(uint entryPoint)
        {
            if (DebugEngine == null)
            {
                return false;
            }

            if (CheckForMicroBooter())
            {
                if (m_execSrecHash.ContainsKey(entryPoint))
                {
                    string execRec = m_execSrecHash[entryPoint];
                    bool fRet = false;

                    for (int retry = 0; retry < 10; retry++)
                    {
                        try
                        {
                            DebugEngine.SendBuffer(Encoding.UTF8.GetBytes(execRec));

                            DebugEngine.SendBuffer(Encoding.UTF8.GetBytes("\n"));
                        }
                        catch
                        {
                            // catch all, doesn't care about the return
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

            var connectionSource = DebugEngine.GetConnectionSource();

            if (connectionSource == ConnectionSource.Unknown)
            {
                return false;
            }

            // only execute if connected to nanoBooter, otherwise reboot
            if (connectionSource == ConnectionSource.nanoBooter)
            {
                return DebugEngine.ExecuteMemory(entryPoint);
            }
            else
            {
                // if connected to CLR then this was just a deployment update, reboot
                DebugEngine.RebootDevice(RebootOptions.ClrOnly);
            }

            return true;
        }

        internal bool CheckForMicroBooter()
        {
            if (DebugEngine == null) return false;

            try
            {
                m_evtMicroBooterStart.Set();
                m_evtMicroBooterError.Reset();

                // try to see if we are connected to MicroBooter
                for (int retry = 0; retry < 5; retry++)
                {
                    DebugEngine.SendBuffer(Encoding.UTF8.GetBytes("xx\n"));

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

        //private bool DeployMFUpdateAsync(StorageFile zipFile, CancellationToken cancellationToken, IProgress<string> progress = null)
        //{
        //    if (zipFile.IsAvailable)
        //    {
        //        byte[] packet = new byte[DebugEngine.WireProtocolPacketSize];
        //        try
        //        {
        //            int handle = -1;
        //            int idx = 0;

        //            Windows.Storage.FileProperties.BasicProperties fileInfo = await zipFile.GetBasicPropertiesAsync();
        //            uint numPkts = (uint)(fileInfo.Size + DebugEngine.WireProtocolPacketSize - 1) / DebugEngine.WireProtocolPacketSize;

        //            byte[] hashData = Encoding.UTF8.GetBytes(zipFile.Name + fileInfo.DateModified.ToString());

        //            uint updateId = CRC.ComputeCRC(hashData, 0, hashData.Length, 0);
        //            uint imageCRC = 0;

        //            byte[] sig = null;

        //            //Debug.WriteLine(updateId);

        //            handle = DebugEngine.StartUpdate("NetMF", 4, 4, updateId, 0, 0, (uint)fileInfo.Size, DebugEngine.WireProtocolPacketSize, 0);
        //            if (handle > -1)
        //            {
        //                uint authType;
        //                IAsyncResult iar = null;

        //                // perform request
        //                (byte[] Response, bool Success) resp = DebugEngine.UpdateAuthCommand(handle, 1, null);

        //                // check result
        //                if (!resp.Success || resp.Response.Length < 4) return false;


        //                using (MemoryStream ms = new MemoryStream(resp.Item1))
        //                using (BinaryReader br = new BinaryReader(ms))
        //                {
        //                    authType = br.ReadUInt32();
        //                }


        //                byte[] pubKey = null;

        //                // FIXME
        //                //if (m_serverCert != null)
        //                //{

        //                //    RSACryptoServiceProvider rsa = m_serverCert.PrivateKey as RSACryptoServiceProvider;

        //                //    if (rsa != null)
        //                //    {
        //                //        pubKey = rsa.ExportCspBlob(false);
        //                //    }
        //                //}

        //                if (!DebugEngine.UpdateAuthenticate(handle, pubKey))
        //                {
        //                    return false;
        //                }

        //                // FIXME
        //                //if (authType == 1 && m_serverCert != null)
        //                //{
        //                //    iar = await DebugEngine.UpgradeConnectionToSsl_Begin(m_serverCert, m_requireClientCert);

        //                //    if (0 == WaitHandle.WaitAny(new WaitHandle[] { iar.AsyncWaitHandle, EventCancel }, 10000))
        //                //    {
        //                //        try
        //                //        {
        //                //            if (!m_eng.UpgradeConnectionToSSL_End(iar))
        //                //            {
        //                //                m_eng.Dispose();
        //                //                m_eng = null;
        //                //                return false;
        //                //            }
        //                //        }
        //                //        catch
        //                //        {
        //                //            m_eng.Dispose();
        //                //            m_eng = null;
        //                //            return false;
        //                //        }
        //                //    }
        //                //    else
        //                //    {
        //                //        return false;
        //                //    }
        //                //}

        //                // FIXME
        //                //RSAPKCS1SignatureFormatter alg = null;
        //                object alg = null;
        //                HashAlgorithmProvider hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
        //                byte[] hashValue = null;

        //                try
        //                {
        //                    if (m_serverCert != null)
        //                    {
        //                        //alg = new RSAPKCS1SignatureFormatter(m_serverCert.PrivateKey);
        //                        //alg.SetHashAlgorithm("SHA1");
        //                        hash = HashAlgorithmProvider.OpenAlgorithm("SHA1");
        //                        hashValue = new byte[hash.HashLength / 8];
        //                    }
        //                }
        //                catch
        //                {
        //                }

        //                IBuffer buffer = await FileIO.ReadBufferAsync(zipFile);
        //                using (DataReader dataReader = DataReader.FromBuffer(buffer))
        //                {
        //                    dataReader.ReadBytes(packet);

        //                    uint crc = CRC.ComputeCRC(packet, 0, packet.Length, 0);

        //                    if (!DebugEngine.AddPacket(handle, (uint)idx++, packet, CRC.ComputeCRC(packet, 0, packet.Length, 0))) return false;

        //                    imageCRC = CRC.ComputeCRC(packet, 0, packet.Length, imageCRC);

        //                    progress?.Report($"Deploying {idx}...");
        //                }

        //                if (hash != null)
        //                {
        //                    buffer = await FileIO.ReadBufferAsync(zipFile);
        //                                     // hash it
        //                    IBuffer hashed = hash.HashData(buffer);
        //                    CryptographicBuffer.CopyToByteArray(hashed, out sig);
        //                }

        //                if (alg != null)
        //                {
        //                    //sig = alg.CreateSignature(hash);
        //                    //CryptographicBuffer.CopyToByteArray(sig)
        //                }
        //                else
        //                {
        //                    sig = new byte[4];
        //                    using (MemoryStream ms = new MemoryStream(sig))
        //                    using (BinaryWriter br = new BinaryWriter(ms))
        //                    {
        //                        br.Write(imageCRC);
        //                    }
        //                }

        //                if (DebugEngine.InstallUpdate(handle, sig))
        //                {
        //                    return true;
        //                }
        //            }
        //        }
        //        catch
        //        {
        //        }
        //    }

        //    return false;
        //}

        //private async Task<Tuple<uint, bool>> DeploySRECAsync(StorageFile srecFile, CancellationToken cancellationToken)
        //{
        //    m_srecHash.Clear();
        //    m_execSrecHash.Clear();

        //    // create .EXT file for SREC file
        //    StorageFolder folder = await srecFile.GetParentAsync();

        //    int m_totalSrecs = 0;
        //    uint m_minSrecAddr = uint.MaxValue;
        //    uint m_maxSrecAddr = 0;

        //    if (srecFile.IsAvailable)
        //    {
        //        // check is EXT file exists, if yes delete it
        //        StorageFile srecExtFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;
        //        if (srecExtFile != null)
        //        {
        //            await srecExtFile.DeleteAsync();
        //        }

        //        if (await PreProcesSrecAsync(srecFile))
        //        {
        //            srecExtFile = await folder.TryGetItemAsync(srecFile.Name.Replace(srecFile.FileType, "") + ".ext") as StorageFile;
        //        }

        //        // check if cancellation was requested 
        //        if (cancellationToken.IsCancellationRequested)
        //        {
        //            new Tuple<uint, bool>(0, false);
        //        }

        //        SrecParseResult parsedFile = await ParseSrecFileAsync(srecExtFile);

        //        try
        //        {
        //            int sleepTime = 5000;
        //            UInt32 imageAddr = 0xFFFFFFFF;

        //            m_totalSrecs = parsedFile.Records.Count;

        //            //m_evtMicroBooterStart.Set();
        //            //m_evtMicroBooter.Reset();
        //            //m_evtMicroBooterError.Reset();

        //            while (parsedFile.Records.Count > 0)
        //            {
        //                // check if cancellation was requested 
        //                if (cancellationToken.IsCancellationRequested)
        //                {
        //                    new Tuple<uint, bool>(0, false);
        //                }

        //                List<uint> remove = new List<uint>();

        //                const int c_MaxPipeline = 4;
        //                int pipe = c_MaxPipeline;

        //                uint[] keys = new uint[parsedFile.Records.Count];

        //                parsedFile.Records.Keys.CopyTo(keys, 0);

        //                Array.Sort(keys);

        //                if (keys[0] < imageAddr) imageAddr = keys[0];

        //                foreach (uint key in keys)
        //                {
        //                    // check if cancellation was requested 
        //                    if (cancellationToken.IsCancellationRequested)
        //                    {
        //                        new Tuple<uint, bool>(0, false);
        //                    }

        //                    if (key < m_minSrecAddr) m_minSrecAddr = key;
        //                    if (key > m_maxSrecAddr) m_maxSrecAddr = key;
        //                    if (m_srecHash.ContainsKey(key))
        //                    {
        //                        remove.Add(key);
        //                        continue;
        //                    }

        //                    await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(true);

        //                    await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes(parsedFile.Records[key]), TimeSpan.FromMilliseconds(20000), cancellationToken).ConfigureAwait(true);

        //                    await DebugEngine.SendBufferAsync(Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(true);

        //                    if (pipe-- <= 0)
        //                    {
        //                        //m_evtMicroBooter.WaitOne(sleepTime);
        //                        pipe = c_MaxPipeline;
        //                    }
        //                }

        //                int cnt = remove.Count;

        //                if (cnt > 0)
        //                {
        //                    for (int i = 0; i < cnt; i++)
        //                    {
        //                        parsedFile.Records.Remove(remove[i]);
        //                    }
        //                }
        //            }

        //            if (imageAddr != 0)
        //            {
        //                string basefile = Path.GetFileNameWithoutExtension(srecFile.Name);

        //                // srecfile might be .bin.ext (for srec updates)
        //                if (!string.IsNullOrEmpty(Path.GetExtension(basefile)))
        //                {
        //                    basefile = Path.GetFileNameWithoutExtension(basefile);
        //                }

        //                string path = folder.Path;
        //                string binFilePath = "";
        //                string symdefFilePath = "";

        //                if (folder.Path.ToLower().EndsWith("\\nanoCLR.hex"))
        //                {
        //                    binFilePath = Path.GetDirectoryName(path) + "\\nanoCLR.bin\\" + basefile;
        //                    symdefFilePath = Path.GetDirectoryName(path) + "\\nanoCLR.symdefs";
        //                }
        //                else
        //                {
        //                    binFilePath = Path.GetDirectoryName(srecFile.Path) + "\\" + basefile + ".bin";
        //                    symdefFilePath = Path.GetDirectoryName(srecFile.Path) + "\\" + basefile + ".symdefs";
        //                }

        //                StorageFile binFile = await folder.TryGetItemAsync(binFilePath) as StorageFile;

        //                StorageFile symdefFile = await folder.TryGetItemAsync(symdefFilePath) as StorageFile;

        //                // check if cancellation was requested 
        //                if (cancellationToken.IsCancellationRequested)
        //                {
        //                    new Tuple<uint, bool>(0, false);
        //                }

        //                // send image crc
        //                if (binFile != null && symdefFile != null)
        //                {
        //                    Windows.Storage.FileProperties.BasicProperties fileInfo = await binFile.GetBasicPropertiesAsync();

        //                    UInt32 imageCRC = 0;

        //                    // read lines from SREC file
        //                    IList<string> textLines = await FileIO.ReadLinesAsync(symdefFile);

        //                    foreach (string line in textLines)
        //                    {
        //                        // check if cancellation was requested 
        //                        if (cancellationToken.IsCancellationRequested)
        //                        {
        //                            new Tuple<uint, bool>(0, false);
        //                        }

        //                        if (line.Contains("LOAD_IMAGE_CRC"))
        //                        {
        //                            int idxEnd = line.IndexOf(' ', 2);
        //                            imageCRC = UInt32.Parse(line.Substring(2, idxEnd - 2), System.Globalization.NumberStyles.HexNumber);
        //                        }
        //                    }

        //                    m_execSrecHash[parsedFile.EntryPoint] = string.Format("<CRC>{0:X08},{1:X08},{2:X08},{3:X08}</CRC>\n", imageAddr, fileInfo.Size, imageCRC, parsedFile.EntryPoint);
        //                }
        //            }

        //            return new Tuple<uint, bool>(parsedFile.EntryPoint, true);
        //        }
        //        finally
        //        {
        //            //m_evtMicroBooterStart.Reset();
        //        }
        //    }

        //    return new Tuple<uint, bool>(0, false);
        //}

        //private async Task<SrecParseResult> ParseSrecFileAsync(StorageFile srecFile)
        //{
        //    SrecParseResult reply = new SrecParseResult();

        //    Dictionary<uint, string> recs = new Dictionary<uint, string>();

        //    try
        //    {
        //        int total = 0;

        //        IList<string> textLines = await FileIO.ReadLinesAsync(srecFile);

        //        foreach (string line in textLines)
        //        {
        //            string addr = line.Substring(4, 8);

        //            // we only support s7, s3 records
        //            if (line.ToLower().StartsWith("s7"))
        //            {
        //                reply.EntryPoint = uint.Parse(addr, System.Globalization.NumberStyles.HexNumber);
        //            }
        //            else if (line.ToLower().StartsWith("s3"))
        //            {
        //                total += line.Length - 14;
        //                reply.Records[uint.Parse(addr, System.Globalization.NumberStyles.HexNumber)] = line;
        //            }
        //        }

        //        reply.ImageSize = (uint)total;
        //    }
        //    catch
        //    {
        //        return null;
        //    }

        //    return reply;
        //}

        //private bool PreProcesSrecAsync(StorageFile srecFile)
        //{
        //    if (!srecFile.IsAvailable) return false;

        //    // create .EXT file for SREC file
        //    StorageFolder folder = await srecFile.GetParentAsync();

        //    try
        //    {
        //        // read lines from SREC file
        //        IList<string> textLines = await FileIO.ReadLinesAsync(srecFile);

        //        StorageFile srecExtFile = await folder.CreateFileAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext", CreationCollisionOption.ReplaceExisting);

        //        const int c_MaxRecords = 8;
        //        int iRecord = 0;
        //        int currentCRC = 0;
        //        int iDataLength = 0;
        //        string s7rec = "";
        //        StringBuilder sb = new StringBuilder();

        //        foreach (string line in textLines)
        //        {
        //            // we only support s7, s3 records
        //            if (line.ToLower().StartsWith("s7"))
        //            {
        //                s7rec = line;
        //                continue;
        //            }

        //            if (!line.ToLower().StartsWith("s3")) continue;

        //            string crcData;

        //            if (iRecord == 0)
        //            {
        //                crcData = line.Substring(4, line.Length - 6);
        //            }
        //            else
        //            {
        //                crcData = line.Substring(12, line.Length - 14);
        //            }

        //            iDataLength += crcData.Length / 2; // 2 chars per byte

        //            if (iRecord == 0)
        //            {
        //                sb.Append(line.Substring(0, 2));
        //            }
        //            sb.Append(crcData);

        //            iRecord++;

        //            for (int i = 0; i < crcData.Length - 1; i += 2)
        //            {
        //                currentCRC += Byte.Parse(crcData.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
        //            }

        //            if (iRecord == c_MaxRecords)
        //            {
        //                iDataLength += 1; // crc

        //                sb = sb.Insert(2, string.Format("{0:X02}", iDataLength));

        //                currentCRC += (iDataLength & 0xFF) + ((iDataLength >> 8) & 0xFF);

        //                // write crc
        //                sb.Append(string.Format("{0:X02}", (0xFF - (0xFF & currentCRC))));

        //                await FileIO.WriteTextAsync(srecExtFile, sb.ToString());

        //                currentCRC = 0;
        //                iRecord = 0;
        //                iDataLength = 0;
        //                sb.Length = 0;
        //            }
        //        }

        //        if (iRecord != 0)
        //        {
        //            iDataLength += 1; // crc

        //            sb = sb.Insert(2, string.Format("{0:X02}", iDataLength));

        //            currentCRC += (iDataLength & 0xFF) + ((iDataLength >> 8) & 0xFF);

        //            // write crc
        //            sb.Append(string.Format("{0:X02}", (0xFF - (0xFF & currentCRC))));

        //            await FileIO.WriteTextAsync(srecExtFile, sb.ToString());
        //        }

        //        if (s7rec != "")
        //        {
        //            await FileIO.WriteTextAsync(srecExtFile, s7rec);
        //        }
        //    }
        //    catch
        //    {
        //        StorageFile thisFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;

        //        if (thisFile != null)
        //        {
        //            await thisFile.DeleteAsync();
        //        }

        //        return false;
        //    }

        //    return true;
        //}

        private bool PrepareForDeploy(
            uint address,
            IProgress<string> progress = null)
        {
            return PrepareForDeploy(
                address,
                null,
                progress);
        }

        private bool PrepareForDeploy(
            List<SRecordFile.Block> blocks,
            IProgress<string> progress = null)
        {
            return PrepareForDeploy(
                0,
                blocks,
                progress);
        }

        private bool PrepareForDeploy(
            uint address,
            List<SRecordFile.Block> blocks,
            IProgress<string> progress = null)
        {
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

            if (blocks != null)
            {
                foreach (SRecordFile.Block bl in blocks)
                {
                    var startSector = flashSectorsMap.Find(s => s.StartAddress == bl.address);
                    if (startSector.NumBlocks > 0)
                    {
                        updatesDeployment ^= (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                        updatesClr ^= (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                        updatesBooter ^= (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP;
                    }
                }
            }
            else
            {
                var startSector = flashSectorsMap.Find(s => s.StartAddress == address);
                if (startSector.NumBlocks > 0)
                {
                    updatesDeployment = (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
                    updatesClr = (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE;
                    updatesBooter = (startSector.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP;
                }
            }

            // sanity check
            if (updatesBooter)
            {
                // can't handle this update because it touches nanoBooter

                progress?.Report("Can't deploy file because it updates nanoBooter.");

                return false;
            }

            if (
                !updatesDeployment &&
                !updatesClr &&
                !updatesBooter)
            {
                // nothing to update???
                return false;
            }

            if (updatesClr)
            {
                // if this is updating the CLR need to launch nanoBooter
                if (!DebugEngine.IsConnectedTonanoBooter)
                {
                    progress?.Report("Need to launch nanoBooter before updating the firmware.");

                    return false;
                }
            }

            // erase whatever blocks are required
            if (updatesClr)
            {
                if (!Erase(
                    EraseOptions.Firmware,
                    null,
                    progress))
                {
                    return false;
                }
            }

            if (updatesDeployment)
            {
                if (!Erase(
                    EraseOptions.Deployment,
                    null,
                    progress))
                {
                    return false;
                }
            }

            if (DebugEngine.IsConnectedTonanoCLR)
            {
                DebugEngine.PauseExecution();
            }

            return true;
        }
    }
}
