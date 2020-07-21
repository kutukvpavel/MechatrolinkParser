using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalysisHelper
{
    class Program
    {
        static void Main(string[] args)
        {
            var fcsFirst = ExtractFCS(@"E:\mechatrolink\encoder\others\05dn_startup.ptxt");
            var fcsSecond = ExtractFCS(@"E:\mechatrolink\encoder\others\10dn_startup.ptxt");
            var fcsDistinct = fcsFirst.
        }

        static IEnumerable<string> ExtractFCS(string path)
        {
            return File.ReadLines(path).Where(x => x.Contains("FCS OK")).Select(x => x.Substring(8, 5)).Distinct();
        }
    }
}
