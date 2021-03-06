﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.Apple;
using Microsoft.DotNet.XHarness.CLI.CommandArguments.Apple;
using Microsoft.DotNet.XHarness.Common.CLI;
using Microsoft.DotNet.XHarness.Common.Execution;
using Microsoft.DotNet.XHarness.Common.Logging;
using Microsoft.DotNet.XHarness.iOS.Shared.Execution;
using Microsoft.DotNet.XHarness.iOS.Shared.Hardware;
using Microsoft.DotNet.XHarness.iOS.Shared.Logging;
using Microsoft.DotNet.XHarness.iOS.Shared.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.DotNet.XHarness.CLI.Commands.Apple
{
    internal abstract class AppleAppCommand<TArguments> : XHarnessCommand<TArguments> where TArguments : IAppleAppRunArguments
    {
        protected readonly ErrorKnowledgeBase ErrorKnowledgeBase = new();

        protected AppleAppCommand(string name, bool allowsExtraArgs, IServiceCollection services, string? help = null) : base(name, allowsExtraArgs, help)
        {
            Services = services;
        }

        protected sealed override async Task<ExitCode> InvokeInternal(Extensions.Logging.ILogger logger)
        {
            var exitCode = ExitCode.SUCCESS;

            var targetName = Arguments.Target.Value.AsString();

            logger.LogInformation($"Preparing run for {targetName}{ (!string.IsNullOrEmpty(Arguments.DeviceName.Value) ? " targeting " + Arguments.DeviceName.Value : null) }");

            // Create main log file for the run
            using ILogs logs = new Logs(Arguments.OutputDirectory);
            string logFileName = $"{Name}-{targetName}{(!string.IsNullOrEmpty(Arguments.DeviceName.Value) ? "-" + Arguments.DeviceName.Value : null)}.log";
            IFileBackedLog runLog = logs.Create(logFileName, LogType.ExecutionLog.ToString(), timestamp: true);

            // Pipe the execution log to the debug output of XHarness effectively making "-v" turn this on
            CallbackLog debugLog = new(message => logger.LogDebug(message.Trim()));
            using var mainLog = Log.CreateReadableAggregatedLog(runLog, debugLog);

            var processManager = new MlaunchProcessManager(Arguments.XcodeRoot, Arguments.MlaunchPath);

            Services.TryAddSingleton(mainLog);
            Services.TryAddSingleton(logs);
            Services.TryAddSingleton<IMlaunchProcessManager>(processManager);
            Services.TryAddSingleton<IMacOSProcessManager>(processManager);
            Services.TryAddSingleton<IProcessManager>(processManager);
            Services.TryAddTransient<XHarness.Apple.ILogger, ConsoleLogger>();

            var cts = new CancellationTokenSource();
            cts.CancelAfter(Arguments.Timeout);

            var exitCodeForRun = await InvokeInternal(cts.Token);
            if (exitCodeForRun != ExitCode.SUCCESS)
            {
                exitCode = exitCodeForRun;
            }

            return exitCode;
        }

        protected abstract Task<ExitCode> InvokeInternal(CancellationToken cancellationToken);

        protected class ConsoleLogger : XHarness.Apple.ILogger
        {
            private readonly Extensions.Logging.ILogger _logger;

            public ConsoleLogger(Extensions.Logging.ILogger logger)
            {
                _logger = logger;
            }

            public void LogDebug(string message) => _logger.LogDebug(message);
            public void LogInformation(string message) => _logger.LogInformation(message);
            public void LogWarning(string message) => _logger.LogWarning(message);
            public void LogError(string message) => _logger.LogError(message);
            public void LogCritical(string message) => _logger.LogCritical(message);
        }
    }
}
