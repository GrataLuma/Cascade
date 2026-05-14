using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class Entropy
    {

        public static double GetCombinationsEntropy(double[] data)
        {
            double sum = 0;
            int n = 0;

            foreach (double value in data) {

                if (value > 0) {

                    sum += value;
                    n++;
                }
            }

            double exponent = 0;

            foreach (double value in data)
            {
                if (value > 0) {

                    double probability = 1.0 * value / sum;
                    exponent += probability * Math.Log(probability, 2);
                }
            }

            if (exponent == 0) return 1;

            return Math.Pow(n, -exponent / Math.Log(n, 2));
        }
    }
}
