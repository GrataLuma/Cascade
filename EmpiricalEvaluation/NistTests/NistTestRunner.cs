using System.Collections.Generic;
using NistTests.Tests;

namespace NistTests
{
    /// <summary>
    /// Orchestrator: runs the full NIST SP 800-22 battery on a bitstream and collects results.
    /// </summary>
    public sealed class NistTestRunner
    {
        public int BlockSize { get; set; } = 128;

        public List<TestResult> RunAll(bool[] bits)
        {
            var tests = new ITest[]
            {
                new FrequencyTest(),
                new BlockFrequencyTest { BlockSize = BlockSize },
                new RunsTest(),
                new LongestRunTest(),
                new BinaryMatrixRankTest(),
                new DftTest(),
                new NonOverlappingTemplateTest(),
                new ApproximateEntropyTest(),
                new CumulativeSumsTest(),
                new SerialTest()
            };

            var results = new List<TestResult>();
            foreach (var t in tests)
            {
                foreach (var r in t.Run(bits)) results.Add(r);
            }
            return results;
        }

        /// <summary>
        /// Convert concatenated K* byte arrays to bool[] (MSB first per byte, skipping null keys).
        /// </summary>
        public static bool[] BitsFromKeys(byte[][] keys)
        {
            int totalBytes = 0;
            foreach (var k in keys) if (k != null) totalBytes += k.Length;
            var bits = new bool[totalBytes * 8];
            int bi = 0;
            foreach (var k in keys)
            {
                if (k == null) continue;
                for (int i = 0; i < k.Length; i++)
                {
                    byte b = k[i];
                    for (int j = 7; j >= 0; j--) bits[bi++] = ((b >> j) & 1) == 1;
                }
            }
            return bits;
        }

        public static bool[] BitsFromString(string binary)
        {
            var bits = new bool[binary.Length];
            for (int i = 0; i < binary.Length; i++)
            {
                if (binary[i] == '0') bits[i] = false;
                else if (binary[i] == '1') bits[i] = true;
                else throw new System.ArgumentException($"Invalid bit char '{binary[i]}' at index {i}");
            }
            return bits;
        }
    }
}
