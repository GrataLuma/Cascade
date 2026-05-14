using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class Combinations
    {
        // Process-wide (n,r) → enumerated combinations cache. NOT thread-safe by
        // design: the protocol runs single-threaded per process (one ClientA +
        // one ClientB sequentially), so concurrent GetAllCombinations calls are
        // not part of the supported usage. Callers MUST NOT invoke from multiple
        // threads — doing so risks duplicate computation and torn writes to
        // allCombinations. If a future scenario needs parallel access, switch to
        // ConcurrentDictionary<(int n, int r), Combinations> + treat the result
        // as immutable.
        private static List<Combinations> internalList = new List<Combinations>();

        public int n;
        public int r;

        public List<int[]> allCombinations;

        private Combinations() {

            allCombinations = new List<int[]>();
        }

        public static Combinations GetAllCombinations(int n, int r) {

            foreach (Combinations combinations in internalList) {

                if (combinations.n == n && combinations.r == r) return combinations;
            }

            Combinations export = new Combinations();
            export.n = n;
            export.r = r;

            int[] a = new int[n];
            for (int i = 0; i < n; i++) a[i] = i;

            int[] data = new int[r];

            CombRec(a, data, 0, n - 1, 0, r, export);

            internalList.Add(export);

            return export;
        }

        private static void CombRec(int[] arr, int[] data, int start, int end, int index, int r, Combinations combinations)
        {
            if (index == r)
            {
                int[] temp = new int[r];
                Array.Copy(data, temp, r);

                combinations.allCombinations.Add(temp);

                return;
            }
            for (int i = start; i <= end && end - i + 1 >= r - index; i++)
            {
                data[index] = arr[i];
                CombRec(arr, data, i + 1, end, index + 1, r, combinations);
            }
        }

        public static BigInteger ComputeCombinationsCount(BigInteger n, BigInteger k)
        {
            BigInteger numerator = BigInteger.One;

            for (BigInteger i = n; i > (n - k); i--)
            {

                numerator *= i;
            }

            BigInteger denominator = BigInteger.One;

            for (BigInteger i = k; i > 1; i--)
            {

                denominator *= i;
            }

            return numerator / denominator;
        }


        public static double ComputeExponentOfCombinationsCount(BigInteger n, BigInteger k)
        {
            double numerator = 0;

            for (BigInteger i = n; i > (n - k); i--)
            {

                numerator += BigInteger.Log10(i);
            }

            double denominator = 0;

            for (BigInteger i = k; i > 1; i--)
            {

                denominator += BigInteger.Log10(i);
            }

            return numerator - denominator;
        }

        public static BigInteger Factorial(BigInteger value) {

            BigInteger export = 1;

            while (value > 1) {

                export = BigInteger.Multiply(export, value);

                value = BigInteger.Subtract(value, 1);
            }

            return export;
        }

        public static BigInteger MultiplyFact(BigInteger value, int iterations) {

            BigInteger export = 1;

            for (int i = 0; i < iterations; i++) {

                export = BigInteger.Multiply(export, value);

                value = BigInteger.Subtract(value, 1);
            }

            return export;
        }
    }
}
