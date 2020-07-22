using System;
using System.Collections.Generic;
using System.Linq;

namespace MechatrolinkParser
{
    /// <summary>
    /// Top-level entity
    /// </summary>
    public class EncoderCommunication
    {
        protected EncoderCommunication(EncoderPacket[] data, int period)
        {
            Packets = data.ToArray();
            Period = period;
        }

        /// <summary>
        /// x10 nS
        /// </summary>
        public int Period { get; private set; }

        public EncoderPacket[] Packets { get; private set; }

        /// <summary>
        /// Gets raw logic analyzer data (edges) and does complete decoding.
        /// </summary>
        /// <param name="list">Edge list</param>
        /// <param name="frequency">Communication frequency (equal to speed, 4Mbps = 4MHz for Mechatrolink-I and 10MHz for M-II)</param>
        /// <param name="littleEndian">Publicly available bits of docs are confusing on the subject of command body endianess.
        /// Experience suggests that body bytes on their own are transmitted as big-endian,
        /// though multibyte data pieces inside the body might be encoded as little-endian.</param>
        /// <returns></returns>
        public static EncoderCommunication Parse(SortedList<int, bool> list, int frequency, bool littleEndian = false)
        {
            var tempDecoded = HDLCManchesterDecoder.Decode(list, frequency, 0.25);
            //DataReporter.ReportProgress("Manchester layer decoded...");
            var packets = HDLCManchesterDecoder.SeparatePackets(tempDecoded);
            tempDecoded.Clear(); //Not needed anymore
            tempDecoded.TrimExcess();
            GC.Collect();
            DataReporter.ReportProgress("HDLC layer decoded...");
            var decoded = new EncoderPacket[packets.Length];
            for (int i = 0; i < packets.Length; i++)
            {
                byte[] temp = HDLCManchesterDecoder.PackIntoBytes(
                    HDLCManchesterDecoder.DestuffZeroes(packets[i])).Values.ToArray();
                if (temp.Length == 0) continue; 
                //DataReporter.ReportProgress(string.Format("Packet {0} out of {1} packed...", i + 1, packets.Length));
                decoded[i] = EncoderPacket.Parse(temp, packets[i].Keys.First(), littleEndian);
                //DataReporter.ReportProgress(string.Format("Packet {0} out of {1} decoded...", i + 1, packets.Length));
            }
            DataReporter.ReportProgress("Packets parsed...");
            return new EncoderCommunication(decoded, (int)Math.Round(1E8 / frequency));
        }
    }

    /// <summary>
    /// Single packet (with flags and preambles already removed)
    /// </summary>
    public class EncoderPacket
    {
        /// <summary>
        /// Properly ordered, starting from 0, subcommand separated as an independent field.
        /// Assumes that flags (and the preamble) have already been separated.
        /// </summary>
        public enum Fields
        {
            Unknown,
            FCS
        }
        /// <summary>
        /// In bytes
        /// </summary>
        public static readonly Dictionary<Fields, int> FieldLength = new Dictionary<Fields, int>
        {
            { Fields.Unknown, -1 }, //Variable length (only one such field per packet is allowed)
            { Fields.FCS, 2 }
        };
        /// <summary>
        /// Without subcommand (bytes)
        /// </summary>
        public const int OrdinaryPacketLength = 19;
        /// <summary>
        /// With a subcommand (bytes)
        /// </summary>
        //public const int FullPacketLength = 32;
        /// <summary>
        /// Exclude subcommand fields for ordinary packets
        /// </summary>
        public static readonly Fields[] OrdinaryPacketFieldsToExclude = { };

        protected EncoderPacket(byte[][] d, EncoderCommand cmd)
        {
            ParsedData = d.Select(x => (x != null) ? x.ToArray() : null).ToArray();
            Command = cmd;
        }
        protected EncoderPacket(byte[][] d, EncoderCommand cmd, int time) : this(d, cmd)
        {
            Timestamp = time;
        }

        public byte[][] ParsedData { get; private set; }
        public EncoderCommand Command { get; private set; }
        public byte[] ComputedFCS { get; private set; }
        public string DatabaseReport { get; private set; }
        public bool FCSError { get; private set; }
        /// <summary>
        /// x10nS
        /// </summary>
        public int Timestamp { get; private set; }

        /// <summary>
        /// Sorts packet's bytes into easily accessible structures. Manipulates endianess.
        /// </summary>
        /// <param name="data">Expects bytes, composed of fully decoded bits (Decode, then SeparatePackets, then DestuffZeros, then PackIntoBytes).</param>
        /// <param name="time">x10nS</param>
        /// <param name="littleEndian">See Communications.Parse</param>
        /// <returns></returns>
        public static EncoderPacket Parse(byte[] data, int time, bool littleEndian = false)
        {
            //bool containsSub = data.Length > OrdinaryPacketLength;
            byte[][] result = new byte[FieldLength.Count][];
            int dataLen = data.Length - FieldLength[Fields.FCS];
            List<byte> fcs = new List<byte>(dataLen > -1 ? dataLen : 0);
            int current = 0;
            try
            {
                for (int i = 0; i < FieldLength.Count; i++)
                {
                    if (OrdinaryPacketFieldsToExclude.Any(x => x == (Fields)i)) continue;
                    int l = FieldLength[(Fields)i];
                    if (l == -1) l = data.Length - FieldLength.Sum(x => x.Value) - 1; //-1 stands for "variable length"
                    result[i] = new byte[l];
                    //Multibyte fields may be little-endian at physical layer (in fact they should be, but it turns out they're not...)
                    //All in all, we'd better implement a switch
                    for (int j = 0; j < l; j++)
                    {
                        result[i][j] = data[current + (littleEndian ? (l - j - 1) : (j))];
                        if ((Fields)i != Fields.FCS) fcs.Add(result[i][j]);
                    }
                    current += l;
                }
            }
            catch (OverflowException)
            {
                ErrorListener.Add(new Exception(string.Format("Packet at {0} is incomplete!", time)));
            }
            var toParse = result[(int)Fields.Unknown];
            //if (containsSub) toParse = toParse.Concat(result[(int)Fields.SubcommandData]).ToArray();
            var packet = new EncoderPacket(result, EncoderCommand.Parse(toParse), time);
            packet.ComputedFCS = HDLCManchesterDecoder.ComputeFCS(fcs.ToArray());
            try
            {
                packet.FCSError = !packet.ComputedFCS.SequenceEqual(packet.ParsedData[(int)Fields.FCS]);
            }
            catch (ArgumentNullException)
            {
                packet.FCSError = true;
            }
            packet.DatabaseReport = EncoderCommandDatabase.GetReport(packet);
            return packet;
        }
    }
    /// <summary>
    /// Single command body (address and other headers/trailers removed)
    /// </summary>
    public class EncoderCommand
    {
        /// <summary>
        /// Ordered
        /// </summary>
        public enum Fields
        {
            Unknown
        }
        /// <summary>
        /// In bytes
        /// </summary>
        public static readonly Dictionary<Fields, int> FieldLength = new Dictionary<Fields, int>
        {
            { Fields.Unknown, -1 } //Variable length (only one such field per command is allowed)
        };
        /// <summary>
        /// Bytes, without a subcommand
        /// </summary>
        public static int MainCommandLength
        {
            get
            {
                return FieldLength[Fields.Unknown];
            }
        }
        /// <summary>
        /// Without subcommand (in fields! it's just a coincidence that those fields are 1-byte-long)
        /// </summary>
        public const int MainCommandFieldsCount = 1;


        private EncoderCommand(byte[][] f, bool cs)
        {
            ParsedFields = f.Select(x => x.ToArray()).ToArray();
            ContainsSubcommand = cs;
        }

        public byte[][] ParsedFields { get; private set; }
        public bool ContainsSubcommand { get; private set; }

        /// <summary>
        /// Just sorts bytes into structures. Does not touch endianess, until specific command decoders are implemented.
        /// </summary>
        /// <param name="data">Expects stripped command body (without address and FCS)</param>
        /// <returns></returns>
        public static EncoderCommand Parse(byte[] data)
        {
            if (data == null) return null;
            bool containsSub = data.Length > MainCommandLength;
            int length = containsSub ? FieldLength.Count : MainCommandFieldsCount;
            int current = 0; //Current position index
            byte[][] result = new byte[length][];
            for (int i = 0; i < length; i++)
            {
                int l = FieldLength[(Fields)i];
                if (l == -1) l = data.Length - FieldLength.Sum(x => x.Value) - 1; //Variable length fields
                result[i] = new byte[l];
                //Multibyte fields have already been converted to big-endian, if needed (in packet.parse())
                for (int j = 0; j < l; j++)
                {
                    result[i][j] = data[current + j];
                }
                current += l;
            }
            return new EncoderCommand(result, containsSub);
        }
    }
}
