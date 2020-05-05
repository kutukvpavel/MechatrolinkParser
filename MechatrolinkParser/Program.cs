using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    static class HDLCManchesterDecoder
    {
        public const byte Flag = 0x7E;

        public static SortedList<int, bool> DestuffZeroes(SortedList<int, bool> data)
        {
            int ones = 0;
            data = new SortedList<int, bool>(data);
            for (int i = 0; i < data.Count; i++)
            {
                if (data.Values[i])
                {
                    ones++;
                }
                else
                {
                    ones = 0;
                }
                try
                {
                    if ((ones == 5) && !data.Values[i + 1])
                    {
                        data.RemoveAt(i + 1);
                        ones = 0;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    Program.ErrorListener.Add(new ArgumentException("Warning: incomplete bit sequence detected!"));
                    break;
                }
            }
            return data;
        }
        public static SortedList<int, byte> PackIntoBytes(SortedList<int, bool> bits)
        {
            int len = bits.Count;
            if (len % 8 != 0)
            {
                Program.ErrorListener.Add(new ArgumentException(string.Format(
                    "Warning: bit count at {0} is not a multiple 8. It will be truncated.", bits.Keys[0])));
                len -= len % 8;
            }
            int byteLen = len / 8;
            SortedList<int, byte> result = new SortedList<int, byte>(byteLen);
            for (int i = 0; i < byteLen; i++)
            {
                byte temp = new byte();
                for (int j = 0; j < 8; j++)
                {
                    temp |= (byte)((bits.Values[i * 8 + j] ? 1 : 0) << j);
                }
                result.Add(bits.Keys[i * 8], temp);
            }
            return result;
        }
        public static SortedList<int, bool> Decode(SortedList<int, bool> data, int freq, double error)
        {
            data = new SortedList<int, bool>(data);
            int p = (int)Math.Round(1E8 / freq);
            int pe = (int)Math.Round(p * error);
            int ph = (int)Math.Round(p * (1 + error));
            int pl = (int)Math.Round(p * (1 - error));
            SortedList<int, int> pulses = new SortedList<int, int>(data.Count - 1);
            for (int i = 0; i < data.Count - 1; i++)
            {
                pulses.Add(data.Keys[i], data.Keys[i + 1] - data.Keys[i]);
            }
            //Look for the preamble: 16 transitions (15 pulses) approximately a period apart, starts with low-to-high, ends with high-to-low
            SortedList<int, int> preambles = new SortedList<int, int>();
            int current = 0;
            for (int i = 0; i < pulses.Count; i++)
            {
                if (current == 0)
                {
                    if (!data[pulses.Keys[i]]) continue;
                }
                if (pulses.Values[i] > pl && pulses.Values[i] < ph)
                {
                    current++;
                }
                else
                {
                    current = 0;
                }
                try
                {
                    if (current == 15)
                    //Next (last) transition is followed by a period/2 pulse, because the flag field starts with 0 (high-to-low).
                    {
                        if (!data[pulses.Keys[i + 1]])
                        {
                            preambles.Add(pulses.Keys[i - 15], pulses.Keys[i + 1]);
                        }
                        current = 0;
                    }
                }
                catch (ArgumentOutOfRangeException)
                {
                    Program.ErrorListener.Add(new ArgumentException(
                        "Warning: the bitstream contains an incomplete packet. It will be discarded."));
                    break;
                }
            }
            //Program.DataReporter.ReportProgress("Preambles parsed...");
            SortedList<int, bool> bits = new SortedList<int, bool>(data.Capacity);
            //Preamble key = start timestamp, value = end timestamp
            //Now shift a halfperiod to the right and divide the timeline into timeslots
            //For each packet find transitions that fit into the timeslots
            //Adjust the clock at every recovered edge to prevent error accumulation
            int currentHigh = 0;
            for (int i = 0; i < preambles.Count; i++)
            {
                current = preambles.Values[i] + pl;      //Next data edge lower bound
                currentHigh = preambles.Values[i] + ph;   //Next data edge higher bound
                //Get rid of redundant bits   
                while (data.Keys[0] < current)
                {
                    data.RemoveAt(0);
                }
                while (current < data.Keys.Last())
                {
                    try
                    {
                        KeyValuePair<int, bool> v = data.First(x => (x.Key > current && x.Key < currentHigh));
                        bits.Add(v.Key, v.Value); 
                        current = v.Key + pl;
                        currentHigh = v.Key + ph;
                    }
                    catch (InvalidOperationException)
                    {
                        break;
                    }
                }
            }
            return bits;
        }

        public static SortedList<int, bool>[] SeparatePackets(SortedList<int, bool> data)
        {
            List<SortedList<int, bool>> res = new List<SortedList<int, bool>>();
            byte rollingSequence = 0;
            //Setup: 0bXXXXXXX0
            for (int i = 0; i < 7; i++)
            {
                rollingSequence |= (byte)((data.Values[i] ? 1u : 0u) << (i + 1)); //LSB first
            }
            bool copy = false;
            SortedList<int, bool> temp = null;
            //First iteration will shift to the right: 0b0XXXXXXX and fill the 0 in
            for (int i = 7; i < data.Count; i++)
            {
                rollingSequence >>= 1;
                if (data.Values[i]) rollingSequence |= (byte)(1u << 7);
                if (rollingSequence == Flag)
                {
                    copy = !copy;
                    if (copy)
                    {
                        temp = new SortedList<int, bool>();
                    }
                    else
                    {
                        for (int j = 0; j < 7; j++) //Remove the 7 bits of the closing flag we've already copied
                        {
                            temp.RemoveAt(temp.Count - 1);
                        }
                        res.Add(temp);
                    }
                    continue; //Do not copy the last bit of the opening flag
                }
                if (copy) temp.Add(data.Keys[i], data.Values[i]);
            }
            return res.ToArray();
        }
    }

    class Communication
    {
        /// <summary>
        /// x10nS (default is 25 == 4MHz)
        /// </summary>
        public int Period { get; private set; }

        private Communication(Packet[] data, int period)
        {
            Packets = data.ToArray();
            Period = period;
        }

        public Packet[] Packets { get; private set; }

        public static Communication Parse(SortedList<int, bool> list, int frequency, bool littleEndian = false)
        {
            var tempDecoded = HDLCManchesterDecoder.Decode(list, frequency, 0.25);
            //Program.DataReporter.ReportProgress("Manchester layer decoded...");
            var packets = HDLCManchesterDecoder.SeparatePackets(tempDecoded);
            Program.DataReporter.ReportProgress("HDLC layer decoded...");
            Packet[] decoded = new Packet[packets.Length];
            for (int i = 0; i < packets.Length; i++)
            {
                byte[] temp = HDLCManchesterDecoder.PackIntoBytes(
                    HDLCManchesterDecoder.DestuffZeroes(packets[i])).Values.ToArray();
                //Program.DataReporter.ReportProgress(string.Format("Packet {0} out of {1} packed...", i + 1, packets.Length));
                decoded[i] = Packet.Parse(temp, packets[i].Keys.First(), littleEndian);
                //Program.DataReporter.ReportProgress(string.Format("Packet {0} out of {1} decoded...", i + 1, packets.Length));
            }
            Program.DataReporter.ReportProgress("Packets parsed...");
            return new Communication(decoded, (int)Math.Round(1E8 / frequency));
        } 
    }

    class Packet
    {
        /// <summary>
        /// Properly ordered, starting from 0, subcommand separated as an independent field
        /// </summary>
        public enum Fields
        {
            //Preamble,
            //OpeningFlag,
            Address,
            Control,
            CommandData,
            SubcommandData,
            FCS
            //ClosingFlag
        }
        /// <summary>
        /// In bytes
        /// </summary>
        public static readonly Dictionary<Fields, int> FieldLength = new Dictionary<Fields, int>
        {
            //{ Fields.Preamble, 2 },
            //{ Fields.OpeningFlag, 1 },
            { Fields.Address, 1 },
            { Fields.Control, 1 },
            { Fields.CommandData, 16 },
            { Fields.SubcommandData, 15 },
            { Fields.FCS, 2 }
            //{ Fields.ClosingFlag, 1 }
        };
        /// <summary>
        /// Without subcommand (bytes)
        /// </summary>
        public const int OrdinaryPacketLength = 20;
        /// <summary>
        /// With a subcommand (bytes)
        /// </summary>
        public const int FullPacketLength = 35;
        public static readonly Fields[] OrdinaryPacketFieldsToExclude = { Fields.SubcommandData };

        private Packet(byte[][] d, Command cmd)
        {
            ParsedData = d.Select(x => (x != null) ? x.ToArray() : null).ToArray();
            Command = cmd;
        }
        private Packet(byte[][] d, Command cmd, int time) : this(d, cmd)
        {
            Timestamp = time;
        }

        public byte[][] ParsedData { get; private set; }
        public Command Command { get; private set; }
        public int Timestamp { get; private set; }

        public static Packet Parse(byte[] data, int time, bool littleEndian = false)
        {
            bool containsSub = data.Length > OrdinaryPacketLength;
            byte[][] result = new byte[FieldLength.Count][];
            int current = 0;
            for (int i = 0; i < FieldLength.Count; i++)
            {
                if (OrdinaryPacketFieldsToExclude.Any(x => x == (Fields)i)) continue;
                int l = FieldLength[(Fields)i];
                result[i] = new byte[l];
                //Multibyte fields may be little-endian at physical layer (in fact they should be, but it turns out they're not...)
                //All in all, we'd better implement a switch
                if (littleEndian)
                {
                    for (int j = 0; j < l; j++)
                    {
                        result[i][j] = data[current + l - j - 1];
                    }
                }
                else
                {
                    for (int j = 0; j < l; j++)
                    {
                        result[i][j] = data[current + j];
                    }
                }
                current += l;
            }
            var toParse = result[(int)Fields.CommandData];
            if (containsSub) toParse = toParse.Concat(result[(int)Fields.SubcommandData]).ToArray();
            return new Packet(result, Command.Parse(toParse), time);
        }
    }
    class Command
    {
        /// <summary>
        /// Ordered
        /// </summary>
        public enum Fields
        {
            Code,
            Data,
            WDT,
            SubcommandCode,
            SubcommandData
        }
        /// <summary>
        /// In bytes
        /// </summary>
        public static readonly Dictionary<Fields, int> FieldLength = new Dictionary<Fields, int>
        {
            { Fields.Code, 1 },
            { Fields.Data, 14 },
            { Fields.WDT, 1 },
            { Fields.SubcommandCode, 1 },
            { Fields.SubcommandData, 14 }
        };
        /// <summary>
        /// Bytes
        /// </summary>
        public static int MainCommandLength
        {
            get
            {
                return FieldLength[Fields.Code] + FieldLength[Fields.Data] + FieldLength[Fields.WDT];
            }
        }
        public const int MainCommandFieldsCount = 3;


        private Command(byte[][] f, bool cs)
        {
            ParsedFields = f.Select(x => x.ToArray()).ToArray();
            ContainsSubcommand = cs;
        }

        public byte[][] ParsedFields { get; private set; }

        public bool ContainsSubcommand { get; private set; }

        public static Command Parse(byte[] data)
        {
            bool containsSub = data.Length > MainCommandLength;
            int length = containsSub ? FieldLength.Count : MainCommandFieldsCount;
            int current = 0; //Current position index
            byte[][] result = new byte[length][];
            for (int i = 0; i < length; i++)
            {
                int l = FieldLength[(Fields)i];
                result[i] = new byte[l];
                //Multibyte fields have already been converted to big-endian, if needed (in packet.parse())
                for (int j = 0; j < l; j++)
                {
                    result[i][j] = data[current + j];
                }
                current += l;
            }
            return new Command(result, containsSub);
        }
    }



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
                    res.AppendFormat("{0}: {1}" + Environment.NewLine, item.GetType().ToString(), item.Message);
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
                "Control: {2}" + Environment.NewLine + "FCS: {3}" + Environment.NewLine;
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
                    res.AppendLine("/////////////////// Packet //////////////////");
                    res.AppendLine("===== HEADER =====");
                    res.AppendFormat(PacketHeaderFormat, item.Timestamp, 
                        ArrayToString(item.ParsedData[(int)Packet.Fields.Address]),
                        ArrayToString(item.ParsedData[(int)Packet.Fields.Control]),
                        ArrayToString(item.ParsedData[(int)Packet.Fields.FCS]));
                    res.AppendLine("===== COMMAND =====");
                    res.AppendFormat(PacketDataFormat,
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.Code]),
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.WDT]),
                        ArrayToString(item.Command.ParsedFields[(int)Command.Fields.Data]));
                    if (item.Command.ContainsSubcommand)
                    {
                        res.AppendLine("===== SUBCOMMAND =====");
                        res.AppendFormat(PacketDataFormat,
                            ArrayToString(item.Command.ParsedFields[(int)Command.Fields.SubcommandCode]),
                            " -- ",
                            ArrayToString(item.Command.ParsedFields[(int)Command.Fields.SubcommandData]));
                    }
                    res.AppendLine();
                }
                //ReportProgress("Report created...");
                return res.ToString();
            }

            private static string ArrayToString(byte[] arr)
            {
                return string.Join(" ", arr.Select(x => x.ToString("X")));
            }

            public static void ReportProgress(string data)
            {
                if (EnableProgressOutput) Console.WriteLine(string.Format("{0:T}: {1}", DateTime.Now, data));
            }
        }



        static void Main(string[] args)
        {
#if DEBUG
            args = new string[] { @"E:\50MSa.txt", "1000000", "4000000" };
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
