﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace MechatrolinkParser
{
    /// <summary>
    /// Top-level entity
    /// </summary>
    public class MechatrolinkCommunication
    {
        protected MechatrolinkCommunication(MechatrolinkPacket[] data, int period)
        {
            Packets = data.ToArray();
            Period = period;
        }

        /// <summary>
        /// x10 nS
        /// </summary>
        public int Period { get; private set; }

        public MechatrolinkPacket[] Packets { get; private set; }

        /// <summary>
        /// Gets raw logic analyzer data (edges) and does complete decoding.
        /// </summary>
        /// <param name="list">Edge list</param>
        /// <param name="frequency">Communication frequency (equal to speed, 4Mbps = 4MHz for Mechatrolink-I and 10MHz for M-II)</param>
        /// <param name="littleEndian">Publicly available bits of docs are confusing on the subject of command body endianess.
        /// Experience suggests that body bytes on their own are transmitted as big-endian,
        /// though multibyte data pieces inside the body might be encoded as little-endian.</param>
        /// <returns></returns>
        public static MechatrolinkCommunication Parse(SortedList<int, bool> list, int frequency, bool littleEndian = false)
        {
            var tempDecoded = HDLCManchesterDecoder.Decode(list, frequency, 0.25);
            //DataReporter.ReportProgress("Manchester layer decoded...");
            var packets = HDLCManchesterDecoder.SeparatePackets(tempDecoded);
            tempDecoded.Clear(); //Not needed anymore
            tempDecoded.TrimExcess();
            GC.Collect();
            DataReporter.ReportProgress("HDLC layer decoded...");
            MechatrolinkPacket[] decoded = new MechatrolinkPacket[packets.Length];
            for (int i = 0; i < packets.Length; i++)
            {
                byte[] temp = HDLCManchesterDecoder.PackIntoBytes(
                    HDLCManchesterDecoder.DestuffZeroes(packets[i])).Values.ToArray();
                //DataReporter.ReportProgress(string.Format("Packet {0} out of {1} packed...", i + 1, packets.Length));
                decoded[i] = MechatrolinkPacket.Parse(temp, packets[i].Keys.First(), littleEndian);
                //DataReporter.ReportProgress(string.Format("Packet {0} out of {1} decoded...", i + 1, packets.Length));
            }
            DataReporter.ReportProgress("Packets parsed...");
            return new MechatrolinkCommunication(decoded, (int)Math.Round(1E8 / frequency));
        }
    }

    /// <summary>
    /// Single packet (with flags and preambles already removed)
    /// </summary>
    public class MechatrolinkPacket
    {
        /// <summary>
        /// Properly ordered, starting from 0, subcommand separated as an independent field.
        /// Assumes that flags (and the preamble) have already been separated.
        /// </summary>
        public enum Fields
        {
            Address,
            Control,
            CommandData,
            SubcommandData,
            FCS
        }
        /// <summary>
        /// In bytes
        /// </summary>
        public static readonly Dictionary<Fields, int> FieldLength = new Dictionary<Fields, int>
        {
            { Fields.Address, 1 },
            { Fields.Control, 1 },
            { Fields.CommandData, 16 },
            { Fields.SubcommandData, 15 },
            { Fields.FCS, 2 }

        };
        /// <summary>
        /// Without subcommand (bytes)
        /// </summary>
        public const int OrdinaryPacketLength = 20;
        /// <summary>
        /// With a subcommand (bytes)
        /// </summary>
        public const int FullPacketLength = 35;
        /// <summary>
        /// Exclude subcommand fields for ordinary packets
        /// </summary>
        public static readonly Fields[] OrdinaryPacketFieldsToExclude = { Fields.SubcommandData };

        protected MechatrolinkPacket(byte[][] d, MechatrolinkCommand cmd)
        {
            ParsedData = d.Select(x => (x != null) ? x.ToArray() : null).ToArray();
            Command = cmd;
        }
        protected MechatrolinkPacket(byte[][] d, MechatrolinkCommand cmd, int time) : this(d, cmd)
        {
            Timestamp = time;
        }

        public byte[][] ParsedData { get; private set; }
        public MechatrolinkCommand Command { get; private set; }
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
        public static MechatrolinkPacket Parse(byte[] data, int time, bool littleEndian = false)
        {
            bool containsSub = data.Length > OrdinaryPacketLength;
            byte[][] result = new byte[FieldLength.Count][];
            List<byte> fcs = new List<byte>(data.Length - FieldLength[Fields.FCS]);
            int current = 0;
            for (int i = 0; i < FieldLength.Count; i++)
            {
                if (OrdinaryPacketFieldsToExclude.Any(x => x == (Fields)i)) continue;   
                int l = FieldLength[(Fields)i];
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
            var toParse = result[(int)Fields.CommandData];
            if (containsSub) toParse = toParse.Concat(result[(int)Fields.SubcommandData]).ToArray();
            var packet = new MechatrolinkPacket(result, MechatrolinkCommand.Parse(toParse), time);
            packet.ComputedFCS = HDLCManchesterDecoder.ComputeFCS(fcs.ToArray());
            packet.FCSError = !packet.ComputedFCS.SequenceEqual(packet.ParsedData[(int)Fields.FCS]);
            packet.DatabaseReport = MechatrolinkCommandDatabase.GetReport(packet);
            return packet;
        }
    }
    /// <summary>
    /// Single command body (address and other headers/trailers removed)
    /// </summary>
    public class MechatrolinkCommand
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
        /// Bytes, without a subcommand
        /// </summary>
        public static int MainCommandLength
        {
            get
            {
                return FieldLength[Fields.Code] + FieldLength[Fields.Data] + FieldLength[Fields.WDT];
            }
        }
        /// <summary>
        /// Without subcommand (in fields! it's just a coincidence that those fields are 1-byte-long)
        /// </summary>
        public const int MainCommandFieldsCount = 3;


        private MechatrolinkCommand(byte[][] f, bool cs)
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
        public static MechatrolinkCommand Parse(byte[] data)
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
            return new MechatrolinkCommand(result, containsSub);
        }
    }
}
