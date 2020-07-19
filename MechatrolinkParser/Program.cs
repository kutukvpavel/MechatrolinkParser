using System;
using System.IO;
using System.Linq;
using System.Text;

namespace MechatrolinkParser
{
    public static class Program
    {
        public enum ExitCodes
        {
            OK = 0,
            CommandLineError,
            ParserError,
            TranscoderError,
            EmptyDataset,
            BulkProcessingFailed,
            OutOfMemory
        }

        public static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            if (args.Length < 1)
            {
                Console.WriteLine(@"Not enough arguments. Usage:
MechatrolinkParser.exe <Exported text file path> <Line/time limit> <Frequency, Hz> <Options>
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
[-pq:X] = set request preamble length to X (defaults to 16)
[-ps:X] = set response preamble length to X (defaults to 16)
[-n] = encoder mode (sets suitable preamble lengths, enables inversion, changes database and reporter - for CC-Link/Mechatrolink encoder communications)
[-c:X] = set Kingst LA channel index (i.e. CSV column index) to X (defaults to 1)
[-o:X] = start parsing from time offset X (w.r. to 0), units are x10nS
[-imax:X] = max request-response delay (defaults to 1000x10nS)
[-imin:X] = min request-response delay (defaults to 50x10nS)
[-t] = use time limit instead of line limit

Returns:
    OK = 0,
    CommandLineError = 1,
    ParserError = 2,
    TranscoderError = 3,
    EmptyDataset = 4,
    BulkProcessingFailed = 5,
    OutOfMemory = 6

");
                Console.ReadLine();
                return (int)ExitCodes.CommandLineError;
            }

            //DataReporter.ReportProgress("Parsing command line...");
            int limit = 20000;
            int freq = 4000000;
            int column = 1;
            int startOffset = int.MinValue;
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
                LogicAnalyzerData.UseTimeLimit = args.Contains("-t");
                if (args.Contains("-b"))
                    return BulkMain(args, limit, freq);
                swap = args.Contains("-e");
                transcode = args.Contains("-stm");
                if (args.Contains("-n")) //Mode switches override on/off switches
                {
                    invert = true;
                    encoder = true;
                    HDLCManchesterDecoder.RequestPreambleLength = 15;
                    HDLCManchesterDecoder.ResponsePreambleLength = 4;
                }
                //Switches with arguments override mode switches
                ArgumentHelper("-pq:", args, (int i) => { HDLCManchesterDecoder.RequestPreambleLength = i; });
                ArgumentHelper("-ps:", args, (int i) => { HDLCManchesterDecoder.ResponsePreambleLength = i; });
                ArgumentHelper("-c:", args, (int i) => { column = i; });
                ArgumentHelper("-imin:", args, (int i) => { HDLCManchesterDecoder.MinRequestResponseDelay = i; });
                ArgumentHelper("-imax:", args, (int i) => { HDLCManchesterDecoder.MaxRequestResponseDelay = i; });
                ArgumentHelper("-o:", args, (int i) => { startOffset = i; });
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse command line:");
                Console.WriteLine(e.ToString());
                return ReturnHelper(ExitCodes.CommandLineError, args);
            }
            DataReporter.ReportProgress(string.Format("{3} limit: {0}, frequency: {1}, endianess swap: {2}.", 
                limit, freq, swap, LogicAnalyzerData.UseTimeLimit ? "Time" : "Line"));

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
                if (data.Count == 0)
                {
                    Console.WriteLine("Given current time constraints, the dataset empty!");
                    return ReturnHelper(ExitCodes.EmptyDataset, args);
                }
                if (invert) data = data.Invert();
                data = data.SkipUntil(startOffset);
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
            catch (OutOfMemoryException)
            {
                return ReturnHelper(ExitCodes.OutOfMemory, args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Unable to parse the data:");
                Console.WriteLine(e.ToString()); 
                return ReturnHelper(ExitCodes.ParserError, args);
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
                    return ReturnHelper(ExitCodes.TranscoderError, args);
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

        public static int ReturnHelper(ExitCodes code, string[] args)
        {
            if (args.Contains("-k")) Console.ReadKey();
            return (int)code;
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
                    ? ExitCodes.OK : ExitCodes.BulkProcessingFailed, args);
            }
            catch (Exception e)
            {
                Console.WriteLine("Bulk processor setup error: " + e.ToString());
            }
            return ReturnHelper(ExitCodes.CommandLineError, args);
        }
    }
}
