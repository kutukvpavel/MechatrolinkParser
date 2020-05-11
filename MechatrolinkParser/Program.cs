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
[-x] = full exception info (defaults to distinct exception output)
[-stm] = transcode the file into LogicSnifferSTM format
[-f] = filter output (exclude nonsensical commands, e.g. with Control field equal to neither 01h nor 03h)
[-b] = bulk (folder) processing, <file path> argument field becomes <""directory path|search string"">, other argument fields are not changed
[-p] = parallel computation in bulk mode
[-k] = keep console windows open (Console.ReadKey())
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
                DataReporter.FilterOutput = args.Contains("-f");
                DataReporter.EnableProgressOutput = !args.Contains("-s");
                BulkProcessing.UseParallelComputation = args.Contains("-p");
                ErrorListener.PrintOnlyDistinctExceptions = !args.Contains("-x");
                if (args.Contains("-b"))
                    return BulkMain(args, limit, freq);
                swap = args.Contains("-e");
                transcode = args.Contains("-stm");
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse command line:");
                Console.WriteLine(e.ToString());
                return ReturnHelper(1, args);
            }
            DataReporter.ReportProgress(string.Format("Time limit: {0}, frequency: {1}, endianess swap: {2}.", 
                limit, freq, swap));

            DataReporter.ReportProgress("Loading data...");
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
                GC.Collect();
                DataReporter.ReportProgress("Parsing data...");
                var parsed = Communication.Parse(data, freq, swap);
                GC.Collect();

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
                return ReturnHelper(2, args);
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
                    return ReturnHelper(3, args);
                }
            }
            return ReturnHelper(0, args);
        }

        public static int ReturnHelper(int code, string[] args)
        {
            if (args.Contains("-k")) Console.ReadKey();
            return code;
        }

        public static void MakeBackupCopy(string filePath)
        {
            string target = Path.GetFileName(filePath);
            target = filePath.Replace(target, 
                string.Format("{0}_backup{1}", Path.GetFileNameWithoutExtension(filePath), Path.GetExtension(filePath)));
            File.Copy(filePath, target);
        }

        public static int BulkMain(string[] args, int lim, int freq)
        {
            if (args.Contains("-f"))
                BulkProcessing.NameModificationFormat = "{0}_parsed_filtered{1}";
            try
            {
                string[] split = args[0].Split('|');
                return ReturnHelper(BulkProcessing.ProcessDirectory(split[0], lim, freq, split[1],
                    string.Join(" ", args.Skip(3).Where(x => x != "-b")))
                    ? 0 : 2, args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Bulk processing error: " + e.ToString());
            }
            return ReturnHelper(1, args);
        }
    }

    public static class ErrorListener
    {
        public static bool PrintOnlyDistinctExceptions { get; set; } = true;

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
            var data = list;
            if (PrintOnlyDistinctExceptions)
            {
                ExceptionComparer comparer = new ExceptionComparer();
                data = data.Distinct(comparer).ToList();
            }
            foreach (var item in data)
            {
                res.AppendFormat("{0}: {1}" + Environment.NewLine,
                    item.Message, item.InnerException != null ? item.InnerException.Message : "");
                res.AppendLine();
            }
            return res.ToString();
        }

        private class ExceptionComparer : IEqualityComparer<Exception>
        {
            public bool Equals(Exception x, Exception y)
            {
                bool current = (x.GetType() == y.GetType()) && (x.Message == y.Message);
                bool inner = (x.InnerException.GetType() == y.InnerException.GetType()) &&
                    (x.InnerException.Message == y.InnerException.Message);
                return current && inner;
            }

            public int GetHashCode(Exception obj)
            {
                int res = obj.GetType().GetHashCode();
                unchecked
                {
                    res = res * 31 + obj.Message.GetHashCode();
                    res = res * 31 + obj.InnerException.Message.GetHashCode();
                }
                return  res;
            }
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
            byte addr = packet.ParsedData[(int)Packet.Fields.Address][0];
            if (addr == 0x00) return true;
            if (addr == CommandDatabase.SyncFrameAddress) return false;
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
