using System;
using Microsoft.DotNet.XHarness.iOS.Shared;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;

namespace Microsoft.DotNet.XHarness.iOS
{
    public interface ICrashSnapshotReporterFactory
    {
        ICrashSnapshotReporter Create(ILog log, ILogs logs, bool isDevice, string deviceName);
    }

    public class CrashSnapshotReporterFactory : ICrashSnapshotReporterFactory
    {
        private readonly IProcessManager _processManager;

        public CrashSnapshotReporterFactory(IProcessManager processManager)
        {
            _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
        }

        public ICrashSnapshotReporter Create(ILog log, ILogs logs, bool isDevice, string deviceName) =>
            new CrashSnapshotReporter(_processManager, log, logs, isDevice, deviceName);
    }
}
