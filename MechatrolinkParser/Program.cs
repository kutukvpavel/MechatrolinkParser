using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

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
[-i] = invert raw data (to correct receiver polarity)
[-p:XX] = set preamble length to XX (defaults to 16)
[-n] = encoder mode (sets preamble to 15 bits and enables inversion - for CC-Link/Mechatrolink encoder communications)
[-c:X] = set Kingst LA channel index (of CSV column index) to X (defaults to 1)
");
                Console.ReadLine();
                return 1;
            }

            //DataReporter.ReportProgress("Parsing command line...");
            int limit = 20000;
            int freq = 4000000;
            int column = 1;
            bool swap = false; 
            bool transcode = false;
            bool invert = false;
            bool encoder = false;
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
                invert = args.Contains("-i");
                DataReporter.FilterOutput = args.Contains("-f");
                DataReporter.EnableProgressOutput = !args.Contains("-s");
                BulkProcessing.UseParallelComputation = args.Contains("-p");
                ErrorListener.PrintOnlyDistinctExceptions = !args.Contains("-x");
                if (args.Contains("-b"))
                    return BulkMain(args, limit, freq);
                swap = args.Contains("-e");
                transcode = args.Contains("-stm");
                if (args.Contains("-n")) //Mode switches override on/off switches
                {
                    invert = true;
                    encoder = true;
                    HDLCManchesterDecoder.PreambleLength = 15;
                }
                //Switches with arguments override mode switches
                ArgumentHelper("-p:", args, (int i) => { HDLCManchesterDecoder.PreambleLength = i; });
                ArgumentHelper("-c:", args, (int i) => { column = i; });
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
                    data = LogicAnalyzerData.CreateFromKingst(args[0], column, limit);
                }
                if (invert) data = data.Invert();
                GC.Collect();
                DataReporter.ReportProgress("Parsing data...");
                if (encoder)
                {
                    var parsed = EncoderCommunication.Parse(data, freq, swap);
                    DoneReportErrors();
                    Console.WriteLine(DataReporter.CreateEncoderReportString(parsed));
                }
                else
                {
                    var parsed = MechatrolinkCommunication.Parse(data, freq, swap);
                    DoneReportErrors();
                    Console.WriteLine(DataReporter.CreateMechatrolinkReportString(parsed));
                }
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

        public static void DoneReportErrors()
        {
            if (DataReporter.EnableProgressOutput && ErrorListener.Exceptions.Any())
            {
                Console.WriteLine(Environment.NewLine + "Warnings:");
                Console.WriteLine(ErrorListener.ToString());
            }
            DataReporter.ReportProgress("Done." + Environment.NewLine);
        }

        public static void ArgumentHelper(string prefix, string[] args, Action<int> act)
        {
            var tmp = args.Where(x => x.StartsWith(prefix));
            if (tmp.Any())
            {
                act.Invoke(int.Parse(tmp.Last().Split(':').Last()));
            }
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
}
