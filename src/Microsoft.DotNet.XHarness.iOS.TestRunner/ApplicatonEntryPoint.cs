using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.DotNet.XHarness.iOS.TestRunner.Core;
using Microsoft.DotNet.XHarness.iOS.TestRunner.XUnit;

namespace Microsoft.DotNet.XHarness.iOS.TestRunner
{
    public enum TestRunner
    {
        NUnit,
        xUnit,
    }

    /// <summary>
    /// Abstract class that represents the entry point of the test application.
    /// 
    /// Subclasses most provide the minimun implementation to ensure that:
    ///
    /// Device: We do have the required device information for the logger.
    /// Asseblies: Provide a list of the assembly information to be ran.
    ///     assemblies can be loaded from disk or from memory, is up to the 
    ///     implementator.
    /// </summary>
    public abstract class ApplicatonEntryPoint
    {
        /// <summary>
        /// Must be implemented and return a class that returns the information
        /// of a device. It can return null.
        /// </summary>
        protected abstract IDevice Device { get; }

        /// <summary>
        /// Returns the IENumerable with the asseblies that contain the tests
        /// to be ran.
        /// </summary>
        /// <returns></returns>
        protected abstract IEnumerable<TestAssemblyInfo> GetTestAssemblies();

        /// <summary>
        /// Returns the type of runner to use.
        /// </summary>
        protected abstract TestRunner TestRunner { get; }

        /// <summary>
        /// Returns the directory that contains the ignore files.
        /// </summary>
        protected abstract string IgnoreFilesDirectory { get; }

        /// <summary>
        /// Terminates the application. This should ensure that it is executed
        /// in the main thread.
        /// </summary>
        protected abstract void TerminateWithSuccess();

        public async Task RunAsync()
        {
            var options = ApplicationOptions.Current;
            TcpTextWriter writer = null;
            if (!string.IsNullOrEmpty(options.HostName))
            {
                try
                {
                    writer = new TcpTextWriter(options.HostName, options.HostPort);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Network error: Cannot connect to {0}:{1}: {2}. Continuing on console.", options.HostName, options.HostPort, ex);
                    writer = null; // will default to the console
                }
            }

            // we generate the logs in two different ways depending if the generate xml flag was
            // provided. If it was, we will write the xml file to the tcp writer if present, else
            // we will write the normal console output using the LogWriter
            var logger = (writer == null || options.EnableXml) ? new LogWriter(Device) : new LogWriter(Device, writer);
            logger.MinimumLogLevel = MinimumLogLevel.Info;
            var testAssemblies = GetTestAssemblies();
            Core.TestRunner runner;
            switch (TestRunner) {
                case TestRunner.NUnit:
                    throw new NotImplementedException("The NUnit test runner has not yet been implemened.");
                default:
                    runner = new XUnitTestRunner(logger);
                    break;
			}

            if (!string.IsNullOrEmpty(IgnoreFilesDirectory))
            {
                var categories = await IgnoreFileParser.ParseTraitsContentFileAsync(IgnoreFilesDirectory, TestRunner == TestRunner.xUnit);
                // add category filters if they have been added
                runner.SkipCategories(categories);

				var skippedTests = await IgnoreFileParser.ParseContentFilesAsync(IgnoreFilesDirectory);
				if (skippedTests.Any())
				{
					// ensure that we skip those tests that have been passed via the ignore files
					runner.SkipTests(skippedTests);
				}
            }

            // if we have ignore files, ignore those tests
            await runner.Run(testAssemblies).ConfigureAwait(false);

            Core.TestRunner.Jargon jargon = Core.TestRunner.Jargon.NUnitV3;
            switch (options.XmlVersion)
            {
                case XmlVersion.NUnitV2:
                    jargon = Core.TestRunner.Jargon.NUnitV2;
                    break;
                case XmlVersion.NUnitV3:
                default: // nunitv3 gives os the most amount of possible details
                    jargon = Core.TestRunner.Jargon.NUnitV3;
                    break;
            }
            if (options.EnableXml)
            {
                runner.WriteResultsToFile(writer ?? Console.Out, jargon);
                logger.Info("Xml file was written to the tcp listener.");
            }
            else
            {
                string resultsFilePath = runner.WriteResultsToFile(jargon);
                logger.Info($"Xml result can be found {resultsFilePath}");
            }

            logger.Info($"Tests run: {runner.TotalTests} Passed: {runner.PassedTests} Inconclusive: {runner.InconclusiveTests} Failed: {runner.FailedTests} Ignored: {runner.FilteredTests}");
            if (options.TerminateAfterExecution)
                TerminateWithSuccess();
        }

    }
}
