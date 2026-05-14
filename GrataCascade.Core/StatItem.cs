using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class StatItem
    {
        public double[] Probabilities { get; set; }

        public StatItem() {

            Probabilities = new double[256];
            for (int i = 0; i < Probabilities.Length; i++) Probabilities[i] = 1;
        }

        public void UpdateWithNumber(byte number)
        {
            double[] temp = new double[256];

            for (int i = 0; i < Probabilities.Length; i++) {

                if (Probabilities[i] > 0) {

                    int newValue = i + number - 128;

                    if (newValue > 255 || newValue < 0)
                    {
                        newValue = number;
                    }

                    temp[newValue] += Probabilities[i];
                }
            }

            for (int i = 0; i < Probabilities.Length; i++) {

                Probabilities[i] = Configuration.NoUpdateLimit * Probabilities[i] + (1 - Configuration.NoUpdateLimit) * temp[i];
            }
        }

        public double GetProbabilityForNumber(int number) {

            return Probabilities[number] / 256.0;
        }

        public int GetEqualOrBiggerToValueCount(double value) {

            int equal = 0;

            for (int i = 0; i < Probabilities.Length; i++)
            {
                if (Probabilities[i] >= value) equal++;
            }

            return equal;

        }

        public int GetBetweenValuesCount(double min, double max)
        {
            int equal = 0;

            for (int i = 0; i < Probabilities.Length; i++)
            {
                if (Probabilities[i] > min && Probabilities[i] < max) equal++;
            }

            return equal;
        }

        public string GetItemAsString()
        {
            StringBuilder builder = new StringBuilder();

            List<KeyValue> temp = new List<KeyValue>();
            for (int i = 0; i < Probabilities.Length; i++) {

                if (Probabilities[i] > 0) {

                    temp.Add(new KeyValue((int)Probabilities[i], i));
                }
            }

            temp = temp.OrderByDescending(x => x.Key).ToList();

            foreach (KeyValue kv in temp) {

                builder.Append(kv.Key.ToString("000") + " (" + kv.Value + ");");
            }

            return builder.ToString();
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();

            foreach (double value in Probabilities) {

                builder.Append(value + ";");
            }

            return builder.ToString();
        }
    }
}
