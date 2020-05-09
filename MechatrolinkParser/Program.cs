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
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 1)
            {
                Console.WriteLine(@"Not enough arguments. Usage:
MechatrolinkParser.exe <Exported text file path> <Line limit> <Frequency, Hz> <Options>
Limit and frequency are optional, defaults: 20000 and 4000000. Use limit = 0 to disable the limit.
Options: [-e] = packet body endianess swap (default is 'big-endian'), usually not needed
[-s] = silent (switch off progress reports/warnings)
[-stm] = transcode the file into LogicSnifferSTM format
[-f] = filter output (exclude nonsensical commands, e.g. with Control field equal to neither 01h nor 03h)
[-b] = bulk (folder) processing, <file path> argument field becomes <directory path>, other argument fields are not changed
[-p] = parallel computation in bulk mode
");
                Console.ReadLine();
                return 1;
            }

            //DataReporter.ReportProgress("Parsing command line...");
            int limit = 20000;
            int freq = 4000000;
            bool swap = false; 
            bool transcode = false;
            try
            {
                args[0] = args[0].Trim('"');
                if (args.Length > 1)
                {
                    limit = int.Parse(args[1]);
                    if (limit == 0) limit = int.MaxValue;
                    if (args.Length > 2)
                    {
                        freq = int.Parse(args[2]);
                    }
                }
                swap = args.Contains("-e");
                DataReporter.EnableProgressOutput = !args.Contains("-s");
                transcode = args.Contains("-stm");
                DataReporter.FilterOutput = args.Contains("-f");
                BulkProcessing.UseParallelComputation = args.Contains("-p");
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse command line:");
                Console.WriteLine(e.ToString());
                Console.ReadKey();
                return 1;
            }
            DataReporter.ReportProgress(string.Format("Time limit: {0}, frequency: {1}, endianess swap: {2}.", 
                limit, freq, swap));
            if (args.Contains("-b"))
                return BulkMain(args[0], limit, freq, string.Join(" ", args.Skip(3).Where(x => x != "-b")));

            DataReporter.ReportProgress("Parsing data...");
            LogicAnalyzerData data;
            try
            {
                if (LogicAnalyzerData.DetectFormat(args[0]) == LogicAnalyzerData.Format.LogicSnifferSTM)
                {
                    data = LogicAnalyzerData.CreateFromLogicSnifferSTM(args[0], limit);
                }
                else
                {
                    data = LogicAnalyzerData.CreateFromKingst(args[0], limit);
                }
                var parsed = Communication.Parse(data, freq, swap);

                if (DataReporter.EnableProgressOutput && ErrorListener.Exceptions.Any())
                {
                    Console.WriteLine(Environment.NewLine + "Warnings:");
                    Console.WriteLine(ErrorListener.ToString());
                }
                DataReporter.ReportProgress("Done." + Environment.NewLine);
                Console.WriteLine(DataReporter.CreateReportString(parsed));
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse the data:");
                Console.WriteLine(e.ToString());
                //Console.ReadKey();
                return 2;
            }

            if (transcode)
            {
                try
                {
                    MakeBackupCopy(args[0]);
                    LogicAnalyzerData.WriteToLogicSnifferSTM(data, args[0]);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Unable to transcode the file:");
                    Console.WriteLine(e.ToString());
                    //Console.ReadKey();
                    return 3;
                }
            }
#if DEBUG
            if (args.Contains("-b")) Console.ReadKey();
#endif
            return 0;
        }

        public static void MakeBackupCopy(string filePath)
        {
            string target = Path.GetFileName(filePath);
            target = filePath.Replace(target, target.Replace(".", "_backup."));
            File.Copy(filePath, target);
        }

        public static int BulkMain(string dir, int lim, int freq, string flags)
        {
            return BulkProcessing.ProcessDirectory(dir, lim, freq, "*.txt", flags) ? 0 : 2;
        }
    }

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
                res.AppendFormat("{0}: {1}" + Environment.NewLine,
                    item.Message, item.InnerException != null ? item.InnerException.Message : "");
                res.AppendLine();
            }
            return res.ToString();
        }
    }

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
            string[] fileContents = File.ReadAllLines(filePath);
            int lineLimit = UseTimeLimit ? fileContents.Length : limit;
            SortedList<int, bool> result = new SortedList<int, bool>(lineLimit - 1);
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
            string[] fileContents = File.ReadAllLines(filePath);
            int lineLimit = UseTimeLimit ? fileContents.Length : limit;
            SortedList<int, bool> result = new SortedList<int, bool>(lineLimit - 1);
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
    }

    public static class DataReporter
    {
        public static bool EnableProgressOutput { get; set; } = true;
        public static bool FilterOutput { get; set; } = false;

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
                if (FilterOutput) if (DetectNonsense(item)) continue;
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
        private static bool DetectNonsense(Packet packet)
        {
            byte command = packet.Command.ParsedFields[(byte)Command.Fields.Code][0];
            if (command == 0x0D || command == 0x0E || command == 0x0F) return false;
            if (packet.ParsedData[(int)Packet.Fields.Address][0] == 0x00) return true;
            byte control = packet.ParsedData[(int)Packet.Fields.Control][0];
            if (control != CommandDatabase.RequestControlCode && control != CommandDatabase.ResponseControlCode) return true;
            return false;
        }

        public static void ReportProgress(string data)
        {
            if (EnableProgressOutput) Console.WriteLine(string.Format("{0:T}: {1}", DateTime.Now, data));
        }
    }
}
