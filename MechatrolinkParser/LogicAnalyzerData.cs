using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    /// <summary>
    /// Key is x10nS
    /// </summary>
    public class LogicAnalyzerData : SortedList<int, bool>
    {
        public enum Format
        {
            KingstTXT,
            LogicSnifferSTM,
            Unknown
        }
        /// <summary>
        /// Switches time/line limit
        /// </summary>
        public static bool UseTimeLimit { get; set; } = false;

        private LogicAnalyzerData(SortedList<int, bool> list) : base(list)
        { }

        public static Format DetectFormat(string filePath)
        {
            TextReader reader = new StreamReader(filePath);
            reader.ReadLine();     //Skip header in case there is one
            //LogicSnifferSTM uses colons to separate columns, Kingst uses commas
            string line = reader.ReadLine();
            if (line.Contains(':')) return Format.LogicSnifferSTM;
            if (line.Count(x => x == ',') == 1) return Format.KingstTXT;
            return Format.Unknown;
        }

        public static LogicAnalyzerData CreateFromLogicSnifferSTM(string filePath, int limit = int.MaxValue)
        {
            string[] fileContents;
            SortedList<int, bool> result;
            int lineLimit = LoadDataHelper(filePath, limit, out fileContents, out result);
            for (int i = fileContents[0].Contains(':') ? 0 : 1; i < lineLimit; i++)
            {
                string[] split = fileContents[i].Split(':');
                try
                {
                    int temp = int.Parse(split[0]);
                    if (UseTimeLimit) if (temp > limit) break;
                    result.Add(int.Parse(split[0]), int.Parse(split[1]) > 0);
                }
                catch (FormatException e)
                {
                    ErrorListener.Add(new Exception("Can't parse LogicSnifferSTM data line: " + fileContents[i], e));
                }
            }
            return new LogicAnalyzerData(result);
        }

        public static LogicAnalyzerData CreateFromKingst(string filePath, int limit = int.MaxValue)
        {
            if (limit < int.MaxValue) limit++; //Take header into account
            string[] fileContents;
            SortedList<int, bool> result;
            int lineLimit = LoadDataHelper(filePath, limit, out fileContents, out result);
            for (int i = 1; i < lineLimit; i++)
            {
                string[] split = fileContents[i].Split(',');
                split[0] = split[0].Replace(".", ""); //Switch to fixed-point arithmetic
                split[1] = split[1].TrimStart();
                try
                {
                    int temp = int.Parse(split[0]);
                    if (UseTimeLimit) if (temp > limit) break;
                    result.Add(int.Parse(split[0]), int.Parse(split[1]) > 0);
                }
                catch (FormatException e)
                {
                    ErrorListener.Add(new Exception("Can't parse Kingst data line: " + fileContents[i], e));
                }
            }
            return new LogicAnalyzerData(result);
        }

        public static void WriteToLogicSnifferSTM(LogicAnalyzerData data, string filePath)
        {
            TextWriter writer = new StreamWriter(filePath);
            for (int i = 0; i < data.Count; i++)
            {
                writer.WriteLine("{0}:{1}\r\n", data.Keys[i], data.Values[i] ? '1' : '0');
            }
            writer.Close();
        }

        private static int LoadDataHelper(string filePath, int limit, out string[] lines, out SortedList<int, bool> result)
        {
            if (UseTimeLimit)
            {
                lines = File.ReadAllLines(filePath);
                limit = lines.Length;
            }
            else
            {
                lines = File.ReadLines(filePath).Take(limit).ToArray();
                if (lines.Length < limit) limit = lines.Length;
            }
            result = new SortedList<int, bool>(limit - 1);
            return limit;
        }
    }
}
