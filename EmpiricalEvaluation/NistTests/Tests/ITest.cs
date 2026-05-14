namespace NistTests.Tests
{
    public sealed class TestResult
    {
        public string Name { get; set; }
        public double PValue { get; set; }
        public bool Passed { get; set; }
        /// <summary>
        /// True if the test declined to emit a p-value (insufficient input length,
        /// unsupported parameters, etc.). A skipped test does not count as FAIL —
        /// the aggregate reporter separates Skipped from PASS/FAIL.
        /// </summary>
        public bool Skipped { get; set; }
        public string Detail { get; set; }

        public static TestResult Make(string name, double pValue, string detail, double alpha = 0.01)
            => new TestResult { Name = name, PValue = pValue, Passed = pValue >= alpha, Skipped = false, Detail = detail };

        /// <summary>
        /// Factory for tests that cannot run on the provided input (e.g. Binary Matrix Rank
        /// with &lt; 38 matrices). Not a FAIL: the test was not exercised.
        /// </summary>
        public static TestResult MakeSkipped(string name, string reason)
            => new TestResult { Name = name, PValue = double.NaN, Passed = false, Skipped = true, Detail = reason };
    }

    public interface ITest
    {
        string Name { get; }
        TestResult[] Run(bool[] bits);
    }
}
