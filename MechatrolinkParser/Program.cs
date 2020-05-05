using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    class Program
    {
        public static class ErrorListener
        {
            private static List<Exception> list = new List<Exception>();
            public static Exception[] Exceptions
            {
                get { return list.ToArray(); }
            }
            public static void Add(Exception e)
            {
                list.Add(e);
            }
            public static void Clear()
            {
                list.Clear();
            }
            public static new string ToString()
            {
                StringBuilder res = new StringBuilder();
                foreach (var item in list)
                {
                    res.AppendFormat("{0}: {1}; {2}" + Environment.NewLine, 
                        item.GetType().ToString(), item.Message, item.InnerException != null ? item.InnerException.ToString() : "");
                }
                return res.ToString();
            }
        }

        /// <summary>
        /// Key is x10nS
        /// </summary>
        public class LogicAnalyzerData : SortedList<int, bool>
        {
            private LogicAnalyzerData(SortedList<int, bool> list) : base(list)
            {

            }

            public static LogicAnalyzerData Create(string filePath, int timeLimit = int.MaxValue)
            {
                string[] fileContents = File.ReadAllLines(filePath);
                SortedList<int, bool> result = new SortedList<int, bool>(fileContents.Length - 1);
                for (int i = 1; i < fileContents.Length; i++)
                {
                    string[] split = fileContents[i].Split(',');
                    split[0] = split[0].Replace(".", ""); //Switch to fixed-point arithmetic
                    split[1] = split[1].TrimStart();
                    try
                    {
                        int temp = int.Parse(split[0]);
                        if (temp > timeLimit) break;
                        result.Add(int.Parse(split[0]), int.Parse(split[1]) > 0);
                    }
                    catch (FormatException e)
                    {
                        ErrorListener.Add(e);
                    }
                }
                return new LogicAnalyzerData(result);
            }
        }

        public static class DataReporter
        {
            public static bool EnableProgressOutput { get; set; } = true;

            private static readonly string PacketHeaderFormat = "Timestamp: {0}" + Environment.NewLine +
                "Address: {1}" + Environment.NewLine +
                "Control: {2}" + Environment.NewLine + "FCS {3}: {4} (computed: {5})" + Environment.NewLine;
            private static readonly string PacketDataFormat = "Command: {0}" + Environment.NewLine +
                "WDR: {1}" + Environment.NewLine + "Data: {2}" + Environment.NewLine;

            public static string CreateReportString(Communication data)
            {
                StringBuilder res = new StringBuilder(
                    string.Format("Mechatrolink communication session. Packets: {0}, period: {1}." 
                    + Environment.NewLine + Environment.NewLine,
                    data.Packets.Length, data.Period));
                foreach (var item in data.Packets)
                {
                    res.AppendLine("///////////////////////////////////// Packet ///////////////////////////////////");
                    res.AppendLine("Raw: " + string.Join(" ", 
                        item.ParsedData.Select(x => ArrayToString(x ?? new byte[0]))));
                    res.AppendLine("================ HEADER ==================");
                    res.AppendFormat(PacketHeaderFormat, item.Timestamp, 
                        ArrayToString(item.ParsedData[(int)Packet.Fields.Address]),
                        ArrayToString(item.ParsedData[(int)Packet.Fields.Control]),
                        item.FCSError ? "ERROR" : "OK",
                        ArrayToString(item.ParsedData[(int)Packet.Fields.FCS]),
                        ArrayToString(item.ComputedFCS));
                    res.AppendLine("================ COMMAND ==================");
                    res.AppendFormat(PacketDataFormat,
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.Code]),
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.WDT]),
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.Data]));
                    if (item.Command.ContainsSubcommand)
                    {
                        res.AppendLine("================= SUBCOMMAND ==================");
                        res.AppendFormat(PacketDataFormat,
                            ArrayToString(item.Command.ParsedFields[(int)Command.Fields.SubcommandCode]),
                            " -- ",
                            ArrayToString(item.Command.ParsedFields[(int)Command.Fields.SubcommandData]));
                    }
                    res.AppendLine(item.DatabaseReport);
                    //res.AppendLine();
                }
                //ReportProgress("Report created...");
                return res.ToString();
            }

            private static string ArrayToString(byte[] arr)
            {
                return string.Join(" ", arr.Select(x => x.ToString("X2")));
            }

            public static void ReportProgress(string data)
            {
                if (EnableProgressOutput) Console.WriteLine(string.Format("{0:T}: {1}", DateTime.Now, data));
            }
        }



        static void Main(string[] args)
        {
#if DEBUG
            args = new string[] { @"E:\50MSa.txt", "200000", "4000000" };
#endif
            if (args.Length < 1)
            {
                Console.WriteLine(@"Not enough arguments. Usage:
MechatrolinkParser.exe <Exported text file path> <Time limit, x10nS> <Frequency, Hz> <Options>
Options: [-e] = packet body endianess swap (default is 'big-endian')
[-s] = silent (switch off progress reports)
Time limit and frequency are optional, defaults: 200000 and 4000000.");
                Console.ReadLine();
                return;
            }

            //DataReporter.ReportProgress("Parsing command line...");
            int limit = 200000;
            int freq = 4000000;
            bool swap = false;
            bool silent = false;
            try
            {
                if (args.Length > 1)
                {
                    limit = int.Parse(args[1]);
                    if (args.Length > 2)
                    {
                        freq = int.Parse(args[2]);
                    }
                }
                swap = args.Contains("-e");
                silent = args.Contains("-s");
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                Console.ReadKey();
                return;
            }
            Console.WriteLine(string.Format("Time limit: {0}, frequency: {1}, endianess swap: {2}.", 
                limit, freq, swap));

            DataReporter.ReportProgress("Parsing data...");

            var data = LogicAnalyzerData.Create(args[0].Trim('"'), limit);
            var parsed = Communication.Parse(data, freq, swap);

            Console.WriteLine(Environment.NewLine + "Warnings:");
            Console.WriteLine(ErrorListener.ToString());
            DataReporter.ReportProgress("Done." + Environment.NewLine);
            Console.WriteLine(DataReporter.CreateReportString(parsed));

#if DEBUG
            Console.ReadKey();
#endif
        }
    }
}
