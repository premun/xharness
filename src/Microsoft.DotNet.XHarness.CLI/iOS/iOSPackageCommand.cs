// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.DotNet.XHarness.iOS.Shared.TestImporter;
using Microsoft.DotNet.XHarness.iOS.Shared.TestImporter.Templates;
using Microsoft.DotNet.XHarness.iOS.Shared.TestImporter.Templates.Managed;
using Microsoft.DotNet.XHarness.iOS.TestImporter;
using Mono.Options;

namespace Microsoft.DotNet.XHarness.CLI.iOS
{

    // Represents the template to be used. Currently only supports the Managed one.
    public enum TemplateType
    {
        Unknown,
        Managed,
        Native,
    }

    // Command that will create the required project generation for the iOS plaform. The command will ensure that all
    // required .csproj and src are created. The command is part of the parent CommandSet iOS and exposes similar
    // plus extra options to the one that its Android counterpart exposes.
    public class iOSPackageCommand : Command
    {

        private bool _showHelp = false;

        // working directories
        private string _workingDirectory;
        private string _outputDirectory;

        // ignore paths, for both ignore files and trait files
        private string _ignoreFilesRootDirectory;
        private string _traitsRootDirectory;

        private string _applicationName;
        private string _mtouchExtraArgs;

        private int _timeoutInSeconds = 300;
        private double _timeoutMultiplier = 1;

        // info required to decide what template to create
        private TemplateType _selectedTemplateType;
        private TestingFramework _testingFramework;
        private List<Platform> _platforms = new List<Platform>();
        public List<string> _assemblies = new List<string>();

        public iOSPackageCommand() : base("package")
        {
            Options = new OptionSet() {
                "usage: ios package [OPTIONS]",
                "",
                "Packaging command that will create a iOS/tvOS/watchOS or macOS application that can be used to run NUnit or XUnit-based test dlls",
                { "name=|n=", "Name of the test application",  v => _applicationName = v},
                { "mtouch-extraargs=|m=", "Extra arguments to be passed to mtouch.", v => _mtouchExtraArgs = v },
                { "ignore-directory=|i=", "Root directory containing all the *.ignore files used to skip tests if needed.", v => _ignoreFilesRootDirectory = v },
                { "traits-directory=|td=", "Root directory that contains all the .txt files with traits that will be skipped if needed.", v =>  _traitsRootDirectory = v },
                { "output-directory=|o=", "Directory in which the resulting package will be outputted", v => _outputDirectory = v},
                { "working-directory=|w=", "Directory that will be used to output generated projects", v => _workingDirectory = v },
                { "assembly=|a=", "An assembly to be added as part of the testing application", v => _assemblies.Add (v)},
                { "testing-framework=|tf=", "The testing framework that is used by the given assemblies.",
                    v => {
                        if (Enum.TryParse(v, out TestingFramework framework))
                            _testingFramework = framework;
                        else
                            _testingFramework = TestingFramework.Unknown;
                    }
                },
                { "platform=|p=", "Plaform to be added as the target for the applicaton.",
                    v => { 
                        // split the platforms and try to parse each of them
                        if (Enum.TryParse(v, out Platform platform))
                            _platforms.Add (platform);
                    }
                },
                { "template=|t=", "Indicates which template to use. There are two available ones: Managed, which uses Xamarin.[iOS|Mac] and Native (default:Managed).",
                    v=> {
                        if (Enum.TryParse(v, out TemplateType template))
                        {
                            _selectedTemplateType = template;
                        }
                        else
                        {
                            _selectedTemplateType = TemplateType.Unknown;
                        }
                    }
                },
                { "help|h", "Show this message", v => _showHelp = v != null },
            };
        }

        public override int Invoke(IEnumerable<string> arguments)
        {
            // Deal with unknown options and print nicely
            List<string> extra = Options.Parse(arguments);
            if (_showHelp)
            {
                Options.WriteOptionDescriptions(Console.Out);
                return 1;
            }
            if (extra.Count > 0)
            {
                Console.WriteLine($"Unknown arguments{string.Join(" ", extra)}");
                Options.WriteOptionDescriptions(Console.Out);
                return 2;
            }
            if (string.IsNullOrEmpty(_applicationName)) {
                Console.WriteLine("You must provide a name for the application to be created.");
                return 1;
            }
            // validate that some of the args are correct
            if (string.IsNullOrEmpty (_outputDirectory)) {
                Console.WriteLine("Output directory path missing.");
                return 1;
            } else {
                if (!Path.IsPathRooted(_outputDirectory))
                    _outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), _outputDirectory);
                if (!Directory.Exists(_outputDirectory))
                    Directory.CreateDirectory(_outputDirectory);
            }

            if (string.IsNullOrEmpty(_workingDirectory)) {
                Console.WriteLine("Working directory path missing.");
                return 1;
            } else {
                if (!Path.IsPathRooted(_workingDirectory))
                    _workingDirectory = Path.Combine(Directory.GetCurrentDirectory(), _workingDirectory);
                if (!Directory.Exists(_workingDirectory))
                    Directory.CreateDirectory(_workingDirectory);
            }

            if (_assemblies.Count == 0) {
                Console.WriteLine("0 assemblies provided. At least one test assembly must be provided to create the application.");
                return 1;
            } else {
                var workingDir = Directory.GetCurrentDirectory();
                foreach (var a in _assemblies) {
                    var assemblyPath = Path.Combine(workingDir, a);
                    if (!File.Exists(assemblyPath))
                    {
                        Console.WriteLine($"Could not find assembly {assemblyPath}");
                        return 1;
                    }
                }
            }

            if (_platforms.Count == 0) {
                Console.WriteLine("0 platforms found. At least one platform must be provided to create the application. Available platforms are:");
                // we in theory support Mac and its flavours, but we are not going to do it at this point
                foreach (var p in new[] { Platform.iOS, Platform.TvOS, Platform.WatchOS}) {
                    Console.WriteLine(p);
                }
            }

            // we must knwo the framework used.
            if (_testingFramework == TestingFramework.Unknown) {
                Console.WriteLine("Unknown testing framework. Supported frameworks are:");
                foreach (var f in new[] { TestingFramework.NUnit, TestingFramework.xUnit })
                    Console.WriteLine(f.ToString());
                return 1;
            } 
            // create the factory, which will be used to find the diff assemblies
            var assemblyDefinitionFactory = new AssemblyDefinitionFactory(_testingFramework, new AssemblyLocator(Directory.GetCurrentDirectory()));

            // assert that we have a known template that we can work with
            ITemplatedProject template;
            switch (_selectedTemplateType) {
                case TemplateType.Managed:
                    template = new XamariniOSTemplate
                    {
                        AssemblyDefinitionFactory = assemblyDefinitionFactory,
                        AssemblyLocator = assemblyDefinitionFactory.AssemblyLocator,
                        OutputDirectoryPath = _outputDirectory,
                        IgnoreFilesRootDirectory = _ignoreFilesRootDirectory,
                        ProjectFilter = new ProjectFilter(_ignoreFilesRootDirectory, _traitsRootDirectory),
                    };
                    break;
                case TemplateType.Native:
                    Console.WriteLine("The 'Native' template is not yet supported. Please use the managed one.");
                    return 1;
                default:
                    Console.WriteLine("Unknonw template was passed. Avaliable templates are:");
                    foreach (var t in new[] { TemplateType.Managed, TemplateType.Native })
                        Console.WriteLine(t.ToString());
                    return 1;
			}

            // first step, generate the required info to be passed to the factory
            var projects = new List<(string Name, string[] Assemblies, string ExtraArgs, double TimeoutMultiplier)> { 
                (Name: _applicationName, Assemblies: _assemblies.ToArray(), ExtraArgs: _mtouchExtraArgs, TimeoutMultiplier: _timeoutMultiplier),
            };
            GeneratedProjects allProjects = new GeneratedProjects();
            foreach (var p in _platforms)
            {
                // so wish that mono.options allowed use to use async :/
                allProjects.AddRange (template.GenerateTestProjectsAsync(projects, p).ConfigureAwait(true).GetAwaiter().GetResult());
            }

            // we do have all the required projects :), time to compile them

            // build the project info that will be used by the template to create the wrapping application
            Console.WriteLine($"iOS Package command called:{Environment.NewLine}Application Name = {_applicationName}");
            Console.WriteLine($"Working Directory:{_workingDirectory}{Environment.NewLine}Output Directory:{_outputDirectory}");
            Console.WriteLine($"Ignore Files Root Directory:{_ignoreFilesRootDirectory}{Environment.NewLine}Traits Root Directory:{_traitsRootDirectory}");
            Console.WriteLine($"MTouch Args:{_mtouchExtraArgs}{Environment.NewLine}Template Type:{Enum.GetName(typeof(TemplateType), _selectedTemplateType)}");

            return 0;
        }
    }
}
