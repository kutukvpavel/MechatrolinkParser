using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalysisHelper
{
    /// <summary>
    /// Search for distinct packets that satisfy some additional conditions
    /// Useful for protocol reverse-engineering (e.g. looking for model string being transmitted)
    /// </summary>
    static class Program
    {
        static readonly string[] Files = new string[]
        {
            @"H:\Doc3\mechatrolink\encoder\others\05dn_startup_parsed.ptxt",
            @"H:\Doc3\mechatrolink\encoder\others\10dn_startup_parsed.ptxt",
            @"H:\Doc3\mechatrolink\encoder\new\STARTUP 1S_parsed.ptxt"
        };

        static readonly int[] Take = new int[]
        {
            13800,
            13800,
            31130
        };

        static readonly string Keyword = "Raw: ";

        static void Main(string[] args)
        {
            //Find distinct packets
            IEnumerable<string>[] distinct = new IEnumerable<string>[Files.Length];
            IEnumerable<string>[] original = new IEnumerable<string>[Files.Length];
            for (int i = 0; i < Files.Length; i++)
            {
                original[i] = Extract(Files[i], Keyword, Take[i]);
            }
            for (int i = 0; i < Files.Length; i++)
            {
                distinct[i] = original[i].ToList(); //Copy
                for (int j = 0; j < Files.Length; j++)
                {
                    if (j == i) continue;
                    distinct[i] = distinct[i].Except(original[j]);
                }
                distinct[i] = distinct[i].Where(x => Check(x)); //And check some additional conditions
            }
            //Report
            for (int i = 0; i < Files.Length; i++)
            {
                Console.WriteLine(Files[i]);
                foreach (var item in distinct[i])
                {
                    Console.WriteLine(item);
                }
                Console.WriteLine();
            }
            Console.ReadKey();
        }

        static IEnumerable<string> Extract(string path, string keyword, int take)
        {
            return File.ReadLines(path).Take(take).Where(x => x.Contains(keyword))
                .Select(x => x.Remove(0, keyword.Length)).Distinct();
        }

        static bool Check(string data)
        {
            if (data.Contains("FF FF FF")) return false;
            return true;
        }
    }
}
