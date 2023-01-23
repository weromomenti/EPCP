using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AudioMonitor
{
    public class IntMaxCompare : IComparer<double>
    {
        public int Compare(double x, double y) => y.CompareTo(x);
    }
}
