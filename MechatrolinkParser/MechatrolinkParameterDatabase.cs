using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    public static class MechatrolinkParameterDatabase
    {
        public static Dictionary<int, string> SGDH = new Dictionary<int, string>
        {
            { 100, "Speed Loop Gain (Hz)" },
            { 101, "Speed Loop Integral Time Constant (0.01ms)" },
            { 102, "Position Loop Gain (1/s)" },
            { 103, "Inertia Ratio (%)" }
        };
    }
}
