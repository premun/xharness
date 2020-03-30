using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.DotNet.XHarness.iOS.Shared.Utilities;

namespace Microsoft.DotNet.XHarness.iOS.TestProjects
{
    public class TestProject
    {
        XmlDocument xml;

        public string Path { get; set; }
        public string SolutionPath { get; set; }
        public string Name { get; set; }
        public bool IsExecutableProject { get; set; }
        public bool IsNUnitProject { get; set; }
        public string[] Configurations { get; set; }
        public Func<Task> Dependency { get; set; }
        public string FailureMessage { get; set; }
        public bool RestoreNugetsInProject { get; set; }
        public string MTouchExtraArgs { get; set; }
        public double TimeoutMultiplier { get; set; } = 1;

        public IEnumerable<TestProject> ProjectReferences;

        public TestProject()
        {
        }

        public TestProject(string path, bool isExecutableProject = true)
        {
            Path = path;
            IsExecutableProject = isExecutableProject;
        }

        public TestProject AsTvOSProject()
        {
            var clone = Clone();
            clone.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), System.IO.Path.GetFileNameWithoutExtension(Path) + "-tvos" + System.IO.Path.GetExtension(Path));
            return clone;
        }

        public TestProject AsWatchOSProject()
        {
            var clone = Clone();
            var fileName = System.IO.Path.GetFileNameWithoutExtension(Path);
            clone.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), fileName + (fileName.Contains("-watchos") ? "" : "-watchos") + System.IO.Path.GetExtension(Path));
            return clone;
        }

        public TestProject AsTodayExtensionProject()
        {
            var clone = Clone();
            clone.Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), System.IO.Path.GetFileNameWithoutExtension(Path) + "-today" + System.IO.Path.GetExtension(Path));
            return clone;
        }

        // Get the referenced today extension project (if any)
        public TestProject GetTodayExtension()
        {
            var extensions = Xml.GetExtensionProjectReferences().ToArray();
            if (!extensions.Any())
                return null;

            if (extensions.Count() != 1)
                throw new NotImplementedException();

            return new TestProject
            {
                Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Path), extensions.First().Replace('\\', '/'))),
            };
        }

        public XmlDocument Xml
        {
            get
            {
                if (xml == null)
                {
                    xml = new XmlDocument();
                    xml.LoadWithoutNetworkAccess(Path);
                }
                return xml;
            }
        }

        public virtual TestProject Clone()
        {
            TestProject rv = (TestProject)Activator.CreateInstance(GetType());
            rv.Path = Path;
            rv.IsExecutableProject = IsExecutableProject;
            rv.RestoreNugetsInProject = RestoreNugetsInProject;
            rv.Name = Name;
            rv.MTouchExtraArgs = MTouchExtraArgs;
            rv.TimeoutMultiplier = TimeoutMultiplier;
            return rv;
        }

        internal async Task<TestProject> CreateCloneAsync()
        {
            var rv = Clone();
            await rv.CreateCopyAsync();
            return rv;
        }

        internal async Task CreateCopyAsync()
        {
            var directory = DirectoryUtilities.CreateTemporaryDirectory(System.IO.Path.GetFileNameWithoutExtension(Path));
            Directory.CreateDirectory(directory);
            var original_path = Path;
            Path = System.IO.Path.Combine(directory, System.IO.Path.GetFileName(Path));

            await Task.Yield();

            XmlDocument doc;
            doc = new XmlDocument();
            doc.LoadWithoutNetworkAccess(original_path);
            var original_name = System.IO.Path.GetFileName(original_path);
            if (original_name.Contains("GuiUnit_NET") || original_name.Contains("GuiUnit_xammac_mobile"))
            {
                // The GuiUnit project files writes stuff outside their project directory using relative paths,
                // but override that so that we don't end up with multiple cloned projects writing stuff to
                // the same location.
                doc.SetOutputPath("bin\\$(Configuration)");
                doc.SetNode("DocumentationFile", "bin\\$(Configuration)\\nunitlite.xml");
            }
            doc.ResolveAllPaths(original_path);

            var projectReferences = new List<TestProject>();
            foreach (var pr in doc.GetProjectReferences())
            {
                var tp = new TestProject(pr.Replace('\\', '/'));
                await tp.CreateCopyAsync();
                doc.SetProjectReferenceInclude(pr, tp.Path.Replace('/', '\\'));
                projectReferences.Add(tp);
            }
            ProjectReferences = projectReferences;

            doc.Save(Path);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
