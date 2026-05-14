using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrataCascade.Core
{
    public class KeyValue
    {
        public int Key { get; set; }
        public int Value { get; set; }

        public KeyValue(int key, int value)
        {
            Key = key;
            Value = value;
        }
    }
}
