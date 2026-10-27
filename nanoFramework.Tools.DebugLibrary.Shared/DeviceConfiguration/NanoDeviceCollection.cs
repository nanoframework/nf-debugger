// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Thread-safe collection of the nanoFramework devices found by the port managers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Enumerating the collection, or calling <see cref="Snapshot"/>, returns the devices present at that moment.
    /// It's safe to do so from any thread, while devices are being added or removed.
    /// </para>
    /// <para>
    /// <see cref="DeviceAdded"/> and <see cref="DeviceRemoved"/> are raised on a library thread (e.g. a device watcher thread),
    /// in the order the changes happened. UI consumers have to marshal them to the UI thread.
    /// An exception thrown by a subscriber is logged and doesn't reach the other subscribers.
    /// </para>
    /// </remarks>
    public sealed class NanoDeviceCollection : IReadOnlyCollection<NanoDeviceBase>
    {
        private static readonly Lazy<NanoDeviceCollection> _instance =
            new Lazy<NanoDeviceCollection>(() => new NanoDeviceCollection());

        private readonly object _syncRoot = new object();
        private readonly List<NanoDeviceBase> _devices = new List<NanoDeviceBase>();

        // copy of _devices, replaced on every change, so it can be enumerated without locking
        private NanoDeviceBase[] _snapshot = Array.Empty<NanoDeviceBase>();

        // changes waiting for their event to be raised, once the lock is released
        private readonly Queue<(NanoDeviceBase Device, bool Added)> _pendingNotifications = new Queue<(NanoDeviceBase, bool)>();
        private bool _raisingNotifications;

        /// <summary>
        /// The devices collection shared by all the port managers.
        /// </summary>
        public static NanoDeviceCollection Instance => _instance.Value;

        private NanoDeviceCollection() { }

        /// <summary>
        /// Raised after a device has been added to the collection.
        /// </summary>
        /// <remarks>
        /// Raised on a library thread.
        /// </remarks>
        public event EventHandler<NanoDeviceEventArgs> DeviceAdded;

        /// <summary>
        /// Raised after a device has been removed from the collection.
        /// </summary>
        /// <remarks>
        /// Raised on a library thread.
        /// </remarks>
        public event EventHandler<NanoDeviceEventArgs> DeviceRemoved;

        /// <summary>
        /// Number of devices in the collection.
        /// </summary>
        public int Count => Volatile.Read(ref _snapshot).Length;

        /// <summary>
        /// Gets the devices currently in the collection.
        /// </summary>
        /// <returns>A copy that isn't affected by later changes.</returns>
        public IReadOnlyList<NanoDeviceBase> Snapshot() => Volatile.Read(ref _snapshot);

        /// <inheritdoc/>
        public IEnumerator<NanoDeviceBase> GetEnumerator() => ((IEnumerable<NanoDeviceBase>)Volatile.Read(ref _snapshot)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Takes the collection lock, so that several reads and changes are done as one operation.
        /// </summary>
        /// <remarks>
        /// The events for the changes made while holding the lock are raised after the outermost scope is disposed.
        /// </remarks>
        internal LockScope Lock()
        {
            Monitor.Enter(_syncRoot);

            return new LockScope(this);
        }

        internal void Add(NanoDeviceBase device)
        {
            using (Lock())
            {
                _devices.Add(device);
                Volatile.Write(ref _snapshot, _devices.ToArray());

                _pendingNotifications.Enqueue((device, true));
            }
        }

        internal bool Remove(NanoDeviceBase device)
        {
            using (Lock())
            {
                if (!_devices.Remove(device))
                {
                    return false;
                }

                Volatile.Write(ref _snapshot, _devices.ToArray());

                _pendingNotifications.Enqueue((device, false));

                return true;
            }
        }

        private void ReleaseLock()
        {
            Monitor.Exit(_syncRoot);

            if (!Monitor.IsEntered(_syncRoot))
            {
                RaisePendingNotifications();
            }
        }

        private void RaisePendingNotifications()
        {
            lock (_syncRoot)
            {
                if (_raisingNotifications || _pendingNotifications.Count == 0)
                {
                    // another thread is raising them (or this one, from an event handler): it will raise these too, in order
                    return;
                }

                _raisingNotifications = true;
            }

            bool allRaised = false;

            try
            {
                while (true)
                {
                    (NanoDeviceBase Device, bool Added) notification;

                    lock (_syncRoot)
                    {
                        if (_pendingNotifications.Count == 0)
                        {
                            _raisingNotifications = false;
                            allRaised = true;
                            return;
                        }

                        notification = _pendingNotifications.Dequeue();
                    }

                    var handler = notification.Added ? DeviceAdded : DeviceRemoved;

                    if (handler is null)
                    {
                        continue;
                    }

                    var eventArgs = new NanoDeviceEventArgs(notification.Device);

                    // each subscriber on its own: one that throws must not keep the others (or the library) from getting on
                    foreach (EventHandler<NanoDeviceEventArgs> subscriber in handler.GetInvocationList())
                    {
                        try
                        {
                            subscriber(this, eventArgs);
                        }
                        catch (Exception ex)
                        {
                            NanoDevicesEventSource.Log.CriticalError(
                                $"{(notification.Added ? nameof(DeviceAdded) : nameof(DeviceRemoved))} handler failed: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                if (!allRaised)
                {
                    // unexpected failure: the notifications left are raised with the next change
                    lock (_syncRoot)
                    {
                        _raisingNotifications = false;
                    }
                }
            }
        }

        /// <summary>
        /// Holds the collection lock until disposed.
        /// </summary>
        internal readonly struct LockScope : IDisposable
        {
            private readonly NanoDeviceCollection _owner;

            internal LockScope(NanoDeviceCollection owner)
            {
                _owner = owner;
            }

            public void Dispose() => _owner.ReleaseLock();
        }
    }
}
