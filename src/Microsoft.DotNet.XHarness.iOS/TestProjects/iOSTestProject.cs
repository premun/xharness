namespace Microsoft.DotNet.XHarness.iOS.TestProjects
{
    public class iOSTestProject : TestProject
    {
        public bool SkipiOSVariation { get; set; }
        public bool SkipwatchOSVariation { get; set; } // skip both
        public bool SkipwatchOSARM64_32Variation { get; set; }
        public bool SkipwatchOS32Variation { get; set; }
        public bool SkiptvOSVariation { get; set; }
        public bool BuildOnly { get; set; }

        public iOSTestProject()
        {
        }

        public iOSTestProject(string path, bool isExecutableProject = true)
            : base(path, isExecutableProject)
        {
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }
    }

}
