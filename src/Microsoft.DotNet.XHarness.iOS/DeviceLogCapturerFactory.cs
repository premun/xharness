using System;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;

namespace Microsoft.DotNet.XHarness.iOS
{
    public interface IDeviceLogCapturerFactory
    {
        IDeviceLogCapturer Create(ILog mainLog, ILog deviceLog, string deviceName);
    }

    public class DeviceLogCapturerFactory : IDeviceLogCapturerFactory
    {
        private readonly IProcessManager _processManager;

        public DeviceLogCapturerFactory(IProcessManager processManager)
        {
            this._processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        }

        public IDeviceLogCapturer Create(ILog mainLog, ILog deviceLog, string deviceName)
        {
            return new DeviceLogCapturer(_processManager, mainLog, deviceLog, deviceName);
        }
    }
}

