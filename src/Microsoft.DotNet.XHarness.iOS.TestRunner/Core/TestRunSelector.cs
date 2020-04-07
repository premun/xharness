namespace Microsoft.DotNet.XHarness.iOS.TestRunner.Core
{
    public class TestRunSelector
    {
        public string Assembly { get; set; }
        public string Value { get; set; }
        public TestRunSelectorType Type { get; set; }
        public bool Include { get; set; }
    }
}
