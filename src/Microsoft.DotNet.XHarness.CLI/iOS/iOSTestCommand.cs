// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.DotNet.XHarness.CLI.Common;
using Microsoft.DotNet.XHarness.iOS;
using Microsoft.DotNet.XHarness.iOS.Shared;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Hardware;
using Microsoft.DotNet.XHarness.iOS.Shared.Listeners;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;
using Microsoft.DotNet.XHarness.iOS.Shared.Utilities;
using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Microsoft.DotNet.XHarness.CLI.iOS
{
    /// <summary>
    /// Command which executes a given, already-packaged iOS application, waits on it and returns status based on the outcome.
    /// </summary>
    public class iOSTestCommand : TestCommand
    {
        // Path to packaged app
        private string _applicationPath = "";

        // List of targets to test.
        private IEnumerable<TestTarget> _targets = Array.Empty<TestTarget>();

        // Path where the outputs of execution will be stored.
        private string _outputDirectory = ".";

        // Path where run logs will hbe stored and projects
        private string _workingDirectory = ".";

        // Path where Xcode is installed
        private string _xcodeRoot = null;

        // Path to the mlaunch binary
        private string _mlaunchPath = ".";

        // How long XHarness should wait until a test execution completes before clean up (kill running apps, uninstall, etc)
        private int _timeoutInSeconds = 300;
        private bool _showHelp = false;

        public iOSTestCommand() : base()
        {
            Options = new OptionSet() {
                "usage: ios test [OPTIONS]",
                "",
                "Packaging command that will create a iOS/tvOS/watchOS or macOS application that can be used to run NUnit or XUnit-based test dlls",
                { "app|a=", "Path to already-packaged app",  v => _applicationPath = v},
                { "output-directory=|o=", "Directory in which the resulting package will be outputted", v => _outputDirectory = v},
                { "targets=", "Comma-delineated list of targets to test for", v => _targets = v.Split(',').Select(t => t.ParseAsAppRunnerTarget()) },
                { "timeout=|t=", "Time span, in seconds, to wait for instrumentation to complete.", v => _timeoutInSeconds = int.Parse(v)},
                { "working-directory=|w=", "Directory in which the resulting package will be outputted", v => _workingDirectory = v},
                { "xcode-root=", "Path where Xcode is installed", v => _xcodeRoot = v},
                { "mlaunch-path=", "Path to the mlaunch binary", v => _mlaunchPath = v},
                { "help|h", "Show this message", v => _showHelp = v != null }
            };
        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            // Deal with unknown options and print nicely
            var extra = Options.Parse(arguments);
            if (_showHelp)
            {
                Options.WriteOptionDescriptions(Console.Out);
                return 1;
            }

            if (extra.Count > 0)
            {
                Console.WriteLine($"Unknown arguments: {string.Join(" ", extra)}");
                Options.WriteOptionDescriptions(Console.Out);
                return 2;
            }

            // TODO: Validate args

            var processManager = new ProcessManager(_xcodeRoot, _mlaunchPath);
            var logs = new Logs(_outputDirectory);
            var cancellationToken = new CancellationToken(); // TODO: Get cancellation from command line env?

            foreach (var target in _targets)
            {
                ILog mainLog = logs.Create(Path.Combine(_outputDirectory, target + "-run.log"), LogType.ExecutionLog.ToString(), true);

                var appRunner = new AppRunner(
                    processManager,
                    new SimulatorsLoaderFactory(processManager),
                    new SimpleListenerFactory(),
                    new DeviceLoaderFactory(processManager),
                    new CrashSnapshotReporterFactory(processManager),
                    new CaptureLogFactory(),
                    new DeviceLogCapturerFactory(processManager),
                    new TestReporterFactory(processManager),
                    target,
                    Log.CreateAggregatedLog(mainLog, new ConsoleLog()),
                    logs,
                    _applicationPath,
                    timeoutInMinutes: _timeoutInSeconds * 60,
                    xmlResultJargon: XmlResultJargon.NUnitV3);

                // TODO try {}
                if (!target.IsSimulator())
                {
                    var result = appRunner.InstallAsync(cancellationToken).ConfigureAwait(true).GetAwaiter().GetResult();
                    if (!result.Succeeded)
                    {
                        Console.Error.WriteLine("Failed to install the app bundle"); // TODO: Better error
                        return result.ExitCode;
                    }
                }

                // TODO try {}
                var exitCode = appRunner.RunAsync().ConfigureAwait(true).GetAwaiter().GetResult();
                if (exitCode != 0)
                {
                    Console.Error.WriteLine("Failed to run the app bundle"); // TODO: Better error
                    return exitCode;
                }
            }

            return 0;
        }
    }
}
