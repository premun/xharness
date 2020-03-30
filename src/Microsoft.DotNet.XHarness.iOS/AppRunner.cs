using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.iOS.Shared;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution.Mlaunch;
using Microsoft.DotNet.XHarness.iOS.Shared.Hardware;
using Microsoft.DotNet.XHarness.iOS.Shared.Listeners;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;
using Microsoft.DotNet.XHarness.iOS.Shared.Utilities;

namespace Microsoft.DotNet.XHarness.iOS
{
    public class AppRunner
    {
        private readonly IProcessManager _processManager;
        private readonly ISimulatorsLoaderFactory _simulatorsLoaderFactory;
        private readonly ISimpleListenerFactory _listenerFactory;
        private readonly IDeviceLoaderFactory _devicesLoaderFactory;
        private readonly ICrashSnapshotReporterFactory _snapshotReporterFactory;
        private readonly ICaptureLogFactory _captureLogFactory;
        private readonly IDeviceLogCapturerFactory _deviceLogCapturerFactory;
        private readonly ITestReporterFactory _testReporterFactory;
        private readonly RunMode _runMode;
        private readonly bool _isSimulator;
        private readonly TestTarget _target;
        private readonly double _timeoutInMinutes;
        private readonly double _launchTimeoutInMinutes = 15; // TODO
        private readonly double _timeoutMultiplier;
        private readonly XmlResultJargon _xmlResultJargon;
        private readonly int _verbosity = 3;
        private string _deviceName;
        private string _companionDeviceName;
        private ISimulatorDevice[] _simulators;

        private ISimulatorDevice simulator => _simulators[0];

        private bool _ensureCleanSimulatorState = true;

        private bool EnsureCleanSimulatorState
        {
            get => _ensureCleanSimulatorState && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SKIP_SIMULATOR_SETUP"));
            set => _ensureCleanSimulatorState = value;
        }

        private bool IsExtension => AppInformation.Extension.HasValue;

        public AppBundleInformation AppInformation { get; }

        public TestExecutingResult Result { get; private set; }

        public string FailureMessage { get; private set; }

        public ILog MainLog { get; set; }

        public ILogs Logs { get; }

        public AppRunner(IProcessManager processManager,
                          ISimulatorsLoaderFactory simulatorsFactory,
                          ISimpleListenerFactory simpleListenerFactory,
                          IDeviceLoaderFactory devicesFactory,
                          ICrashSnapshotReporterFactory snapshotReporterFactory,
                          ICaptureLogFactory captureLogFactory,
                          IDeviceLogCapturerFactory deviceLogCapturerFactory,
                          ITestReporterFactory reporterFactory,
                          TestTarget target,
                          ILog mainLog,
                          ILogs logs,
                          string appBundlePath,
                          ISimulatorDevice[] simulators = null,
                          string deviceName = null,
                          string companionDeviceName = null,
                          bool ensureCleanSimulatorState = false,
                          double timeoutInMinutes = 15,
                          double timeoutMultiplier = 1,
                          string variation = null,
                          XmlResultJargon xmlResultJargon = XmlResultJargon.xUnit)
        {
            MainLog = mainLog ?? throw new ArgumentNullException(nameof(mainLog));
            Logs = logs ?? throw new ArgumentNullException(nameof(logs));

            _processManager = processManager ?? throw new ArgumentNullException(nameof(processManager));
            _simulatorsLoaderFactory = simulatorsFactory ?? throw new ArgumentNullException(nameof(simulatorsFactory));
            _listenerFactory = simpleListenerFactory ?? throw new ArgumentNullException(nameof(simpleListenerFactory));
            _devicesLoaderFactory = devicesFactory ?? throw new ArgumentNullException(nameof(devicesFactory));
            _snapshotReporterFactory = snapshotReporterFactory ?? throw new ArgumentNullException(nameof(snapshotReporterFactory));
            _captureLogFactory = captureLogFactory ?? throw new ArgumentNullException(nameof(captureLogFactory));
            _deviceLogCapturerFactory = deviceLogCapturerFactory ?? throw new ArgumentNullException(nameof(deviceLogCapturerFactory));
            _testReporterFactory = reporterFactory ?? throw new ArgumentNullException(nameof(_testReporterFactory));
            _timeoutMultiplier = timeoutMultiplier;
            _xmlResultJargon = xmlResultJargon;
            _deviceName = deviceName;
            _companionDeviceName = companionDeviceName;
            _ensureCleanSimulatorState = ensureCleanSimulatorState;
            _timeoutInMinutes = timeoutInMinutes;
            _simulators = simulators;
            _target = target;

            _runMode = target.ToRunMode();
            _isSimulator = target.IsSimulator();

            // TODO
            AppInformation = new AppBundleInformation(
                appName: Path.GetFileName(appBundlePath).Replace(".app", string.Empty),
                bundleIdentifier: Path.GetFileName(appBundlePath),
                appPath: appBundlePath,
                launchAppPath: Path.GetDirectoryName(appBundlePath),
                extension: null)
            {
                Variation = variation
            };
        }

        private async Task<bool> FindSimulatorAsync()
        {
            if (_simulators != null)
            {
                return true;
            }

            var sims = _simulatorsLoaderFactory.CreateLoader();
            await sims.LoadAsync(Logs.Create($"simulator-list-{Helpers.Timestamp}.log", "Simulator list"), false, false);
            _simulators = await sims.FindAsync(_target, MainLog);

            return _simulators != null;
        }

        private void FindDevice()
        {
            if (_deviceName != null)
            {
                return;
            }

            _deviceName = Environment.GetEnvironmentVariable("DEVICE_NAME");
            if (!string.IsNullOrEmpty(_deviceName))
            {
                return;
            }

            var devs = _devicesLoaderFactory.CreateLoader();
            Task.Run(async () =>
            {
                await devs.LoadAsync(MainLog, false, false);
            }).Wait();

            DeviceClass[] deviceClasses;
            switch (_runMode)
            {
                case RunMode.iOS:
                    deviceClasses = new[] { DeviceClass.iPhone, DeviceClass.iPad, DeviceClass.iPod };
                    break;
                case RunMode.WatchOS:
                    deviceClasses = new[] { DeviceClass.Watch };
                    break;
                case RunMode.TvOS:
                    deviceClasses = new[] { DeviceClass.AppleTV }; // Untested
                    break;
                default:
                    throw new ArgumentException(nameof(_runMode));
            }

            var selected = devs.ConnectedDevices.Where((v) => deviceClasses.Contains(v.DeviceClass) && v.IsUsableForDebugging != false);
            IHardwareDevice selected_data;
            if (selected.Count() == 0)
            {
                throw new NoDeviceFoundException($"Could not find any applicable devices with device class(es): {string.Join(", ", deviceClasses)}");
            }
            else if (selected.Count() > 1)
            {
                selected_data = selected
                    .OrderBy((dev) =>
                    {
                        Version v;
                        if (Version.TryParse(dev.ProductVersion, out v))
                        {
                            return v;
                        }

                        return new Version();
                    })
                    .First();
                MainLog.WriteLine("Found {0} devices for device class(es) '{1}': '{2}'. Selected: '{3}' (because it has the lowest version).", selected.Count(), string.Join("', '", deviceClasses), string.Join("', '", selected.Select((v) => v.Name).ToArray()), selected_data.Name);
            }
            else
            {
                selected_data = selected.First();
            }

            _deviceName = selected_data.Name;

            if (_runMode == RunMode.WatchOS)
            {
                _companionDeviceName = devs.FindCompanionDevice(MainLog, selected_data).Name;
            }
        }

        public async Task<ProcessExecutionResult> InstallAsync(CancellationToken cancellation_token)
        {
            if (_isSimulator)
            {
                // We reset the simulator when running, so a separate install step does not make much sense.
                throw new InvalidOperationException("Installing to a simulator is not supported.");
            }

            FindDevice();

            var args = new MlaunchArguments();

            for (int i = -1; i < _verbosity; i++)
            {
                args.Add(new VerbosityArgument());
            }

            args.Add(new InstallAppOnDeviceArgument(AppInformation.AppPath));
            args.Add(new DeviceNameArgument(_companionDeviceName ?? _deviceName));

            if (_runMode == RunMode.WatchOS)
            {
                args.Add(new DeviceArgument("ios,watchos"));
            }

            var totalSize = Directory.GetFiles(AppInformation.AppPath, "*", SearchOption.AllDirectories).Select((v) => new FileInfo(v).Length).Sum();
            MainLog.WriteLine($"Installing '{AppInformation.AppPath}' to '{_companionDeviceName ?? _deviceName}'. Size: {totalSize} bytes = {totalSize / 1024.0 / 1024.0:N2} MB");

            return await _processManager.ExecuteCommandAsync(args, MainLog, TimeSpan.FromHours(1), cancellation_token: cancellation_token);
        }

        public async Task<ProcessExecutionResult> UninstallAsync()
        {
            if (_isSimulator)
            {
                throw new InvalidOperationException("Uninstalling from a simulator is not supported.");
            }

            FindDevice();

            var args = new MlaunchArguments();

            for (int i = -1; i < _verbosity; i++)
            {
                args.Add(new VerbosityArgument());
            }

            args.Add(new UninstallAppFromDeviceArgument(AppInformation.BundleIdentifier));
            args.Add(new DeviceNameArgument(_companionDeviceName ?? _deviceName));

            return await _processManager.ExecuteCommandAsync(args, MainLog, TimeSpan.FromMinutes(1));
        }

        public async Task<int> RunAsync()
        {
            if (!_isSimulator)
            {
                FindDevice();
            }

            var args = new MlaunchArguments();

            for (int i = -1; i < _verbosity; i++)
            {
                args.Add(new VerbosityArgument());
            }

            args.Add(new SetAppArgumentArgument("-connection-mode"));
            args.Add(new SetAppArgumentArgument("none")); // This will prevent the app from trying to connect to any IDEs
            args.Add(new SetAppArgumentArgument("-autostart", true));
            args.Add(new SetEnvVariableArgument("NUNIT_AUTOSTART", true));
            args.Add(new SetAppArgumentArgument("-autoexit", true));
            args.Add(new SetEnvVariableArgument("NUNIT_AUTOEXIT", true));
            args.Add(new SetAppArgumentArgument("-enablenetwork", true));
            args.Add(new SetEnvVariableArgument("NUNIT_ENABLE_NETWORK", true));

            if (!DisableSystemPermissionTests(TestPlatform.iOS, !_isSimulator))
            {
                args.Add(new SetEnvVariableArgument("DISABLE_SYSTEM_PERMISSION_TESTS", 1));
            }

            if (_isSimulator)
            {
                args.Add(new SetAppArgumentArgument("-hostname:127.0.0.1", true));
                args.Add(new SetEnvVariableArgument("NUNIT_HOSTNAME", "127.0.0.1"));
            }
            else
            {
                var ips = new StringBuilder();
                var ipAddresses = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList;
                for (int i = 0; i < ipAddresses.Length; i++)
                {
                    if (i > 0)
                    {
                        ips.Append(',');
                    }

                    ips.Append(ipAddresses[i].ToString());
                }

                var ipArg = ips.ToString();
                args.Add(new SetAppArgumentArgument($"-hostname:{ipArg}", true));
                args.Add(new SetEnvVariableArgument("NUNIT_HOSTNAME", ipArg));
            }

            var listener_log = Logs.Create($"test-{_runMode.ToString().ToLowerInvariant()}-{Helpers.Timestamp}.log", LogType.TestLog.ToString(), timestamp: true);
            var (transport, listener, listenerTmpFile) = _listenerFactory.Create(_runMode, MainLog, listener_log, _isSimulator, true, false);

            listener.Initialize();

            args.Add(new SetAppArgumentArgument($"-transport:{transport}", true));
            args.Add(new SetEnvVariableArgument("NUNIT_TRANSPORT", transport.ToString().ToUpper()));

            if (transport == ListenerTransport.File)
            {
                args.Add(new SetEnvVariableArgument("NUNIT_LOG_FILE", listenerTmpFile));
            }

            args.Add(new SetAppArgumentArgument($"-hostport:{listener.Port}", true));
            args.Add(new SetEnvVariableArgument("NUNIT_HOSTPORT", listener.Port));

            listener.StartAsync();

            // object that will take care of capturing and parsing the results
            ILog runLog = MainLog;
            var crashLogs = new Logs(Logs.Directory);

            ICrashSnapshotReporter crashReporter = _snapshotReporterFactory.Create(MainLog, crashLogs, isDevice: !_isSimulator, _deviceName);

            var testReporterTimeout = TimeSpan.FromMinutes(_timeoutInMinutes * _timeoutMultiplier);
            var testReporter = _testReporterFactory.Create(MainLog,
                runLog,
                Logs,
                crashReporter,
                listener,
                new XmlResultParser(),
                AppInformation,
                _runMode,
                _xmlResultJargon,
                _deviceName,
                testReporterTimeout,
                _launchTimeoutInMinutes,
                null,
                (level, message) => MainLog.WriteLine(message));

            listener.ConnectedTask
                .TimeoutAfter(TimeSpan.FromMinutes(_launchTimeoutInMinutes))
                .ContinueWith(testReporter.LaunchCallback)
                .DoNotAwait();

            // TODO:
            // args.AddRange(harness.EnvironmentVariables.Select(kvp => new SetEnvVariableArgument(kvp.Key, kvp.Value)));

            if (IsExtension)
            {
                switch (AppInformation.Extension)
                {
                    case Extension.TodayExtension:
                        args.Add(_isSimulator
                            ? (MlaunchArgument)new LaunchSimulatorExtensionArgument(AppInformation.LaunchAppPath, AppInformation.BundleIdentifier)
                            : new LaunchDeviceExtensionArgument(AppInformation.LaunchAppPath, AppInformation.BundleIdentifier));
                        break;
                    case Extension.WatchKit2:
                    default:
                        throw new NotImplementedException();
                }
            }
            else
            {
                args.Add(_isSimulator
                    ? (MlaunchArgument)new LaunchSimulatorArgument(AppInformation.LaunchAppPath)
                    : new LaunchDeviceArgument(AppInformation.LaunchAppPath));
            }
            if (!_isSimulator)
            {
                args.Add(new DisableMemoryLimitsArgument());
            }

            if (_isSimulator)
            {
                if (!await FindSimulatorAsync())
                {
                    return 1;
                }

                if (_runMode != RunMode.WatchOS)
                {
                    var stderr_tty = Helpers.GetTerminalName(2);
                    if (!string.IsNullOrEmpty(stderr_tty))
                    {
                        args.Add(new SetStdoutArgument(stderr_tty));
                        args.Add(new SetStderrArgument(stderr_tty));
                    }
                    else
                    {
                        var stdout_log = Logs.CreateFile($"stdout-{Helpers.Timestamp}.log", "Standard output");
                        var stderr_log = Logs.CreateFile($"stderr-{Helpers.Timestamp}.log", "Standard error");
                        args.Add(new SetStdoutArgument(stdout_log));
                        args.Add(new SetStderrArgument(stderr_log));
                    }
                }

                var systemLogs = new List<ICaptureLog>();
                foreach (var sim in _simulators)
                {
                    // Upload the system log
                    MainLog.WriteLine("System log for the '{1}' simulator is: {0}", sim.SystemLog, sim.Name);
                    bool isCompanion = sim != simulator;

                    var logDescription = isCompanion ? LogType.CompanionSystemLog.ToString() : LogType.SystemLog.ToString();
                    var log = _captureLogFactory.Create(
                        Path.Combine(Logs.Directory, sim.Name + ".log"),
                        sim.SystemLog,
                        true,
                        logDescription);

                    log.StartCapture();
                    Logs.Add(log);
                    systemLogs.Add(log);
                    WrenchLog.WriteLine("AddFile: {0}", log.FullPath);
                }

                MainLog.WriteLine("*** Executing {0}/{1} in the simulator ***", AppInformation.AppName, _runMode);

                if (EnsureCleanSimulatorState)
                {
                    foreach (var sim in _simulators)
                    {
                        await sim.PrepareSimulatorAsync(MainLog, AppInformation.BundleIdentifier);
                    }
                }

                args.Add(new SimulatorUDIDArgument(simulator.UDID));

                await crashReporter.StartCaptureAsync();

                MainLog.WriteLine("Starting test run");

                await testReporter.CollectSimulatorResult(
                    _processManager.ExecuteCommandAsync(args, runLog, testReporterTimeout, cancellation_token: testReporter.CancellationToken));

                // cleanup after us
                if (EnsureCleanSimulatorState)
                {
                    await simulator.KillEverythingAsync(MainLog);
                }

                foreach (var log in systemLogs)
                {
                    log.StopCapture();
                }
            }
            else
            {
                MainLog.WriteLine("*** Executing {0}/{1} on device '{2}' ***", AppInformation.AppName, _runMode, _deviceName);

                if (_runMode == RunMode.WatchOS)
                {
                    args.Add(new AttachNativeDebuggerArgument()); // this prevents the watch from backgrounding the app.
                }
                else
                {
                    args.Add(new WaitForExitArgument());
                }

                args.Add(new DeviceNameArgument(_deviceName));

                var deviceSystemLog = Logs.Create($"device-{_deviceName}-{Helpers.Timestamp}.log", "Device log");
                var deviceLogCapturer = _deviceLogCapturerFactory.Create(MainLog, deviceSystemLog, _deviceName);
                deviceLogCapturer.StartCapture();

                try
                {
                    await crashReporter.StartCaptureAsync();

                    MainLog.WriteLine("Starting test run");

                    // We need to check for MT1111 (which means that mlaunch won't wait for the app to exit).
                    var aggregatedLog = Log.CreateAggregatedLog(testReporter.CallbackLog, MainLog);
                    Task<ProcessExecutionResult> runTestTask = _processManager.ExecuteCommandAsync(
                        args,
                        aggregatedLog,
                        testReporterTimeout,
                        cancellation_token: testReporter.CancellationToken);

                    await testReporter.CollectDeviceResult(runTestTask);
                }
                finally
                {
                    deviceLogCapturer.StopCapture();
                    deviceSystemLog.Dispose();
                }

                // Upload the system log
                if (File.Exists(deviceSystemLog.FullPath))
                {
                    MainLog.WriteLine("A capture of the device log is: {0}", deviceSystemLog.FullPath);
                    WrenchLog.WriteLine("AddFile: {0}", deviceSystemLog.FullPath);
                }
            }

            listener.Cancel();
            listener.Dispose();

            // check the final status, copy all the required data
            (Result, FailureMessage) = await testReporter.ParseResult();

            return testReporter.Success.Value ? 0 : 1;
        }


        private bool DisableSystemPermissionTests(TestPlatform platform, bool device)
        {
            // If we've been told something in particular, that takes precedence.
            //if (IncludeSystemPermissionTests.HasValue)
            //	return IncludeSystemPermissionTests.Value;

            // If we haven't been told, try to be smart.
            switch (platform)
            {
                case TestPlatform.iOS:
                case TestPlatform.Mac:
                case TestPlatform.Mac_Full:
                case TestPlatform.Mac_Modern:
                case TestPlatform.Mac_System:
                    // On macOS we can't edit the TCC database easily
                    // (it requires adding the mac has to be using MDM: https://carlashley.com/2018/09/28/tcc-round-up/)
                    // So by default ignore any tests that would pop up permission dialogs in CI.
                    return true;
                default:
                    // On device we have the same issue as on the mac: we can't edit the TCC database.
                    if (device)
                    {
                        return true;
                    }

                    // But in the simulator we can just write to the simulator's TCC database (and we do)
                    return true;
            }
        }
    }
}
