using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class Stat
    {
        public StatItem[] StatItems { get; set; }

        public string Tag { get; set; }

        public Stat(int N, string tag) {

            this.Tag = tag;

            StatItems = new StatItem[N];

            for (int i = 0; i < StatItems.Length; i++) StatItems[i] = new StatItem();
        }

        public void UpdateWithRandomVector(byte[] randomVector) {

            for (int i = 0; i < StatItems.Length; i++) StatItems[i].UpdateWithNumber(randomVector[i]);
        }

        public double GetCombinationsCount(int oneCount) {

            Combinations combinations = Combinations.GetAllCombinations(StatItems.Length, oneCount);

            double sum = 0;

            foreach (int[] combination in combinations.allCombinations) {

                double one = 1;
                for (int i = 0; i < combination.Length; i++) {

                    one *= StatItems[combination[i]].GetBetweenValuesCount(0.5, 1.1);
                }

                if (one == 0) continue;

                double bigger = 1;
                for (int i = 0; i < StatItems.Length; i++) {

                    if (!combination.Contains(i)) {

                        bigger *= StatItems[i].GetEqualOrBiggerToValueCount(1.1);
                    }
                }

                double temp = one * bigger;

                sum += temp;
            }

            return sum;
        }

        public int GetOneCount(Vector vector) {

            int export = 0;

            for (int i = 0; i < StatItems.Length; i++) {

                if (StatItems[i].Probabilities[vector.Data[i]] < 1.1) export++;
            }

            return export;
        }

        public double GetLowestProbability(Vector vector) {

            double min = Double.MaxValue;

            for (int i = 0; i < StatItems.Length; i++)
            {
                min = Math.Min(min, StatItems[i].Probabilities[vector.Data[i]]);
            }

            return min;
        }

        public double GetProbabilityLog(Vector vector) {

            double export = 1;

            for (int i = 0; i < StatItems.Length; i++)
            {
                export *= StatItems[i].GetProbabilityForNumber(vector.Data[i]);
            }

            return Math.Log(export, 2);
        }

        public void PrintStat() {

            for (int i = 0; i < StatItems.Length; i++) {

                Console.WriteLine(i + ";" + StatItems[i].ToString());
            }
        }
    }
}
