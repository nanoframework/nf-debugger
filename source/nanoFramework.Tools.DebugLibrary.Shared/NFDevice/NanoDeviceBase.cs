//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.IO;
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
        public IPort Parent { get; set; }

        /// <summary>
        /// Device description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Detailed info about the NanoFramework device hardware, solution and CLR.
        /// </summary>
        public INanoFrameworkDeviceInfo DeviceInfo { get; internal set; }

        private object m_serverCert = null;
        private Dictionary<uint, string> m_execSrecHash = new Dictionary<uint, string>();
        private Dictionary<uint, int> m_srecHash = new Dictionary<uint, int>();

        private AutoResetEvent m_evtMicroBooter = new AutoResetEvent(false);
        private AutoResetEvent m_evtMicroBooterError = new AutoResetEvent(false);
        private ManualResetEvent m_evtMicroBooterStart = new ManualResetEvent(false);

        public NanoDeviceBase()
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
                var mfDeviceInfo = new NanoFrameworkDeviceInfo(this);
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
                throw new DeviceNotConnectedException();
            }

            var reply = DebugEngine.GetConnectionSource();

            if (reply != null)
            {
                switch (reply.m_source)
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

                DebugEngine.RebootDevice(RebootOptions.EnterBootloader);

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

                    if (fConnected = await DebugEngine.ConnectAsync(1000, true, ConnectionSource.Unknown))
                    {
                        Commands.Monitor_Ping.Reply reply = DebugEngine.GetConnectionSource();

                        ret = (reply.m_source == Commands.Monitor_Ping.c_Ping_Source_NanoBooter);

                        break;
                    }
                }

                if (!fConnected)
                {
                    //Debug.WriteLine("Unable to connect to NanoBooter");
                }
            }
            return ret;
        }

        /// <summary>
        /// Erases the deployment sectors of the connected .Net Micro Framework device
        /// </summary>
        /// <param name="options">Identifies which areas are to be erased</param>
        /// <param name="cancellationToken">Cancellation token to allow caller to cancel task</param>
        /// <param name="progress">Progress report of execution</param>
        /// <returns>Returns false if the erase fails, true otherwise
        /// Possible exceptions: MFUserExitException, MFDeviceNoResponseException
        /// </returns>
        public async Task<bool> EraseAsync(EraseOptions options, CancellationToken cancellationToken, IProgress<ProgressReport> progress = null)
        {
            bool fReset = false;

            if (DebugEngine == null) throw new NanoFrameworkDeviceNoResponseException();

            // check if the device is responsive
            var isPresent = Ping();
            if (Ping() == PingConnectionType.NoConnection)
            {
                // it's not, try reconnect
                if (!await DebugEngine.ConnectAsync(5000, true))
                {
                    throw new NanoFrameworkDeviceNoResponseException();
                }
            }

            if (!IsClrDebuggerEnabled || 0 != (options & EraseOptions.Firmware))
            {
                fReset = (Ping() == PingConnectionType.nanoCLR);

                if (!await ConnectToNanoBooterAsync(cancellationToken))
                {
                    throw new NanoBooterConnectionFailureException();
                }
            }

            var reply = DebugEngine.GetFlashSectorMap();

            if (reply == null) throw new NanoFrameworkDeviceNoResponseException();

            Commands.Monitor_Ping.Reply ping = DebugEngine.GetConnectionSource();

            long total = 0;
            long value = 0;

            bool isConnectedToCLR = ((ping != null) && (ping.m_source == Commands.Monitor_Ping.c_Ping_Source_NanoCLR));


            if (isConnectedToCLR)
            {
                DebugEngine.PauseExecution();
            }

            List<Commands.Monitor_FlashSectorMap.FlashSectorData> eraseSectors = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();

            foreach (Commands.Monitor_FlashSectorMap.FlashSectorData flashSectorData in reply)
            {
                if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

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
                progress?.Report(new ProgressReport(value, total, $"Erasing sector 0x{flashSectorData.m_StartAddress.ToString("X8")}."));

                var (ErrorCode, Success) = DebugEngine.EraseMemory(flashSectorData.m_StartAddress, (flashSectorData.m_NumBlocks * flashSectorData.m_BytesPerBlock));

                if(!Success)
                {
                    // operation failed
                    progress?.Report(new ProgressReport(value, total, $"Error erasing sector @ 0x{flashSectorData.m_StartAddress.ToString("X8")}. Error code: {ErrorCode}."));
                    
                    // don't bother continuing
                    return false;
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
                progress?.Report(new ProgressReport(0, 0, "Rebooting..."));

                var rebootOptions = RebootOptions.ClrOnly;

                DebugEngine.RebootDevice(rebootOptions);
            }

            return true;
        }

        public async Task<bool> DeployUpdateAsync(StorageFile comprFilePath, CancellationToken cancellationToken, IProgress<ProgressReport> progress = null)
        {
            if (DebugEngine.ConnectionSource == ConnectionSource.nanoCLR)
            {
                if (await DeployMFUpdateAsync(comprFilePath, cancellationToken, progress)) return true;
            }

            return false;
        }

        /// <summary>
        /// Attempts to deploy an SREC (.hex) file to the connected nanoFramework device. 
        /// </summary>
        /// <param name="srecFile">Storage file with the SREC (.hex) file</param>
        /// <param name="entrypoint">Out parameter that is set to the entry point address for the given SREC file</param>
        /// <returns>Returns false if the deployment fails, true otherwise
        /// Possible exceptions: MFFileNotFoundException, MFDeviceNoResponseException, MFUserExitException
        /// </returns>
        public async Task<Tuple<uint, bool>> DeployAsync(StorageFile srecFile, CancellationToken cancellationToken, IProgress<ProgressReport> progress = null)
        {
            uint entryPoint = 0;
            List<SRecordFile.Block> blocks = new List<SRecordFile.Block>();

            if (!srecFile.IsAvailable) throw new FileNotFoundException(srecFile.Path);

            if (DebugEngine == null) throw new NanoFrameworkDeviceNoResponseException();

            // make sure we know who we are talking to
            if (await CheckForMicroBooterAsync(cancellationToken))
            {
                var reply = await DeploySRECAsync(srecFile, cancellationToken);

                // check if request was successful
                if (reply.Item2)
                {
                    entryPoint = reply.Item1;

                    return new Tuple<uint, bool>(entryPoint, true);
                }
                else
                {
                    return new Tuple<uint, bool>(0, false);
                }
            }

            await DebugEngine.ConnectAsync(1000, false, ConnectionSource.Unknown);

            var parseResult = await SRecordFile.ParseAsync(srecFile);
            entryPoint = parseResult.Item1;
            blocks = parseResult.Item2;

            if (blocks.Count > 0)
            {
                long total = 0;
                long value = 0;

                for (int i = 0; i < blocks.Count; i++)
                {
                    total += (blocks[i] as SRecordFile.Block).data.Length;
                }

                await PrepareForDeployAsync(blocks, cancellationToken, progress);



                foreach (SRecordFile.Block block in blocks)
                {
                    long len = block.data.Length;
                    uint addr = block.address;

                    // check if cancellation was requested 
                    if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                    block.data.Seek(0, SeekOrigin.Begin);

                    progress?.Report(new ProgressReport(0, total, string.Format("Erasing sector 0x{0:x08}", block.address)));

                    // the clr requires erase before writing
                    var eraseResult = DebugEngine.EraseMemory(block.address, (uint)len);

                    if (!eraseResult.Success)
                    {
                        return new Tuple<uint, bool>(0, false);
                    }

                    while (len > 0)
                    {
                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                        uint buflen = len > DebugEngine.WireProtocolPacketSize ? DebugEngine.WireProtocolPacketSize : (uint)len;
                        byte[] data = new byte[buflen];

                        if (block.data.Read(data, 0, (int)buflen) <= 0)
                        {
                            return new Tuple<uint, bool>(0, false);
                        }

                        var writeResult = DebugEngine.WriteMemory(addr, data);
                        if (writeResult.Success == false)
                        {
                            return new Tuple<uint, bool>(0, false);
                        }

                        value += buflen;
                        addr += (uint)buflen;
                        len -= buflen;

                        progress?.Report(new ProgressReport(value, total, string.Format("Deploying {0}...", srecFile.Name)));
                    }
                }
            }

            return new Tuple<uint, bool>(entryPoint, true);
        }

        /// <summary>
        /// Starts execution on the connected .Net Micro Framework device at the supplied address (parameter entrypoint).
        /// This method is generally used after the Deploy method to jump into the code that was deployed.
        /// </summary>
        /// <param name="entrypoint">Entry point address for execution to begin</param>
        /// <param name="cancellationToken">Cancellation token for caller to cancel task execution</param>
        /// <returns>Returns false if execution fails, true otherwise
        /// Possible exceptions: MFDeviceNoResponseException
        /// </returns>
        public async Task<bool> ExecuteAync(uint entryPoint, CancellationToken cancellationToken)
        {
            if (DebugEngine == null) throw new NanoFrameworkDeviceNoResponseException();

            if (await CheckForMicroBooterAsync(cancellationToken))
            {
                // check if cancellation was requested 
                if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                if (m_execSrecHash.ContainsKey(entryPoint))
                {
                    string execRec = (string)m_execSrecHash[entryPoint];
                    bool fRet = false;

                    for (int retry = 0; retry < 10; retry++)
                    {
                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                        await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes(execRec), TimeSpan.FromMilliseconds(1000), cancellationToken);

                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                        await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);

                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

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

            if (reply == null) throw new NanoFrameworkDeviceNoResponseException();

            // only execute if we are talking to the nanoBooter, otherwise reboot
            if (reply.m_source == Commands.Monitor_Ping.c_Ping_Source_NanoBooter)
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

                    await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes("xx\n"), TimeSpan.FromMilliseconds(5000), cancellationToken);

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

        private async Task<bool> DeployMFUpdateAsync(StorageFile zipFile, CancellationToken cancellationToken, IProgress<ProgressReport> progress = null)
        {
            if (zipFile.IsAvailable)
            {
                byte[] packet = new byte[DebugEngine.WireProtocolPacketSize];
                try
                {
                    int handle = -1;
                    int idx = 0;

                    var fileInfo = await zipFile.GetBasicPropertiesAsync();
                    uint numPkts = (uint)(fileInfo.Size + DebugEngine.WireProtocolPacketSize - 1) / DebugEngine.WireProtocolPacketSize;

                    byte[] hashData = UTF8Encoding.UTF8.GetBytes(zipFile.Name + fileInfo.DateModified.ToString());

                    uint updateId = CRC.ComputeCRC(hashData, 0, hashData.Length, 0);
                    uint imageCRC = 0;

                    byte[] sig = null;

                    //Debug.WriteLine(updateId);

                    handle = DebugEngine.StartUpdate("NetMF", 4, 4, updateId, 0, 0, (uint)fileInfo.Size, (uint)DebugEngine.WireProtocolPacketSize, 0);
                    if (handle > -1)
                    {
                        uint authType;
                        IAsyncResult iar = null;

                        // perform request
                        var resp = DebugEngine.UpdateAuthCommand(handle, 1, null);

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

                            progress?.Report(new ProgressReport(idx, numPkts, string.Format("Deploying {0}...", idx)));
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
            var folder = await srecFile.GetParentAsync();

            int m_totalSrecs = 0;
            uint m_minSrecAddr = uint.MaxValue;
            uint m_maxSrecAddr = 0;

            if (srecFile.IsAvailable)
            {
                // check is EXT file exists, if yes delete it
                var srecExtFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;
                if (srecExtFile != null)
                {
                    await srecExtFile.DeleteAsync();
                }

                if (await PreProcesSrecAsync(srecFile))
                {
                    srecExtFile = await folder.TryGetItemAsync(srecFile.Name.Replace(srecFile.FileType, "") + ".ext") as StorageFile;
                }

                // check if cancellation was requested 
                if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                var parsedFile = await ParseSrecFileAsync(srecExtFile);

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
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                        List<uint> remove = new List<uint>();

                        const int c_MaxPipeline = 4;
                        int pipe = c_MaxPipeline;

                        uint[] keys = new uint[parsedFile.Records.Count];

                        parsedFile.Records.Keys.CopyTo(keys, 0);

                        Array.Sort<uint>(keys);

                        if (keys[0] < imageAddr) imageAddr = keys[0];

                        foreach (uint key in keys)
                        {
                            // check if cancellation was requested 
                            if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                            if (key < m_minSrecAddr) m_minSrecAddr = key;
                            if (key > m_maxSrecAddr) m_maxSrecAddr = key;
                            if (m_srecHash.ContainsKey(key))
                            {
                                remove.Add(key);
                                continue;
                            }

                            await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);

                            await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes(parsedFile.Records[key]), TimeSpan.FromMilliseconds(20000), cancellationToken);

                            await DebugEngine.SendBufferAsync(UTF8Encoding.UTF8.GetBytes("\n"), TimeSpan.FromMilliseconds(1000), cancellationToken);

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

                        var binFile = await folder.TryGetItemAsync(binFilePath) as StorageFile;

                        var symdefFile = await folder.TryGetItemAsync(symdefFilePath) as StorageFile;

                        // check if cancellation was requested 
                        if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

                        // send image crc
                        if (binFile != null && symdefFile != null)
                        {
                            var fileInfo = await binFile.GetBasicPropertiesAsync();

                            UInt32 imageCRC = 0;

                            // read lines from SREC file
                            var textLines = await FileIO.ReadLinesAsync(symdefFile);

                            foreach (string line in textLines)
                            {
                                // check if cancellation was requested 
                                if (cancellationToken.IsCancellationRequested) throw new NanoUserExitException();

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

                var textLines = await FileIO.ReadLinesAsync(srecFile);

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
            var folder = await srecFile.GetParentAsync();

            try
            {
                // read lines from SREC file
                var textLines = await FileIO.ReadLinesAsync(srecFile);

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
                var thisFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(srecFile.Name) + ".ext") as StorageFile;

                if (thisFile != null)
                {
                    await thisFile.DeleteAsync();
                }

                return false;
            }

            return true;
        }

        private async Task PrepareForDeployAsync(List<SRecordFile.Block> blocks, CancellationToken cancellationToken, IProgress<ProgressReport> progress = null)
        {
            const uint c_DeploySector = Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT;
            const uint c_SectorUsageMask = Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK;

            bool fEraseDeployment = false;

            // if vsdebug is not enabled then we cannot write/erase
            if (!IsClrDebuggerEnabled)
            {
                progress?.Report(new ProgressReport(0, 1, "Connecting to TinyBooter..."));

                // only check for signature file if we are uploading firmware
                if (!await ConnectToNanoBooterAsync(cancellationToken))
                {
                    throw new NanoFrameworkDeviceNoResponseException();
                }
            }

            var flasSectorsMap = DebugEngine.GetFlashSectorMap();

            if (flasSectorsMap == null || flasSectorsMap.Count == 0) throw new NanoFrameworkDeviceNoResponseException();

            foreach (SRecordFile.Block bl in blocks)
            {
                foreach (Commands.Monitor_FlashSectorMap.FlashSectorData sector in flasSectorsMap)
                {
                    if (sector.m_StartAddress == bl.address)
                    {
                        // only support writing with CLR to the deployment sector and RESERVED sector (for digi)
                        if (c_DeploySector == (c_SectorUsageMask & sector.m_flags))
                        {
                            fEraseDeployment = true;
                        }
                        else
                        {
                            if (DebugEngine.ConnectionSource != ConnectionSource.nanoBooter)
                            {
                                progress?.Report(new ProgressReport(0, 1, "Connecting to nanoBooter..."));

                                // only check for signature file if we are uploading firmware
                                if (!await ConnectToNanoBooterAsync(cancellationToken))
                                {
                                    throw new NanoFrameworkDeviceNoResponseException();
                                }
                            }
                        }
                        break;
                    }
                }
            }
            if (fEraseDeployment)
            {
                await EraseAsync(EraseOptions.Deployment, cancellationToken, progress);
            }
            else if (DebugEngine.ConnectionSource != ConnectionSource.nanoBooter)
            {
                //if we are not writing to the deployment sector then assure that we are talking with nanoBooter
                await ConnectToNanoBooterAsync(cancellationToken);
            }
            if (DebugEngine.ConnectionSource == ConnectionSource.nanoCLR)
            {
                DebugEngine.PauseExecution();
            }
        }
    }
}
