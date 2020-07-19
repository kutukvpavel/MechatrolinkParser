using System;
using System.Collections.Generic;
using System.Data.HashFunction.CRC;

namespace MechatrolinkParser
{
    /// <summary>
    /// Static method wrapper
    /// </summary>
    public static class HDLCManchesterDecoder
    {
        private static ICRC CrcFactory = CRCFactory.Instance.Create(CRCConfig.X25);

        /// <summary>
        /// Mechatrolink-II uses the same flag (packet start and end marker) value the HDLC does.
        /// </summary>
        public const byte Flag = 0x7E;

        /// <summary>
        /// Instructs the parser to ignore last preamble (last packet) in case it's incomplete.
        /// Defaults to true.
        /// False forces the parser not to check if the next transition belongs to a new packet.
        /// Therefore setting this to false will cause error if there are no "spaces" between the packets.
        /// For mechatrolink the latter is not the case.
        /// </summary>
        public static bool IgnoreLastPreamble { get; set; } = true;

        public static int RequestPreambleLength { get; set; } = 16;

        public static int ResponsePreambleLength { get; set; } = 16;

        /// <summary>
        /// x10 nS
        /// </summary>
        public static int MaxRequestResponseDelay { get; set; } = 1000;
        /// <summary>
        /// x10 nS
        /// </summary>
        public static int MinRequestResponseDelay { get; set; } = 50;

        /// <summary>
        /// Remove zero bits stuffed in for transparency of the protocol
        /// </summary>
        /// <param name="data">Bool stands for already decoded bits, not just transitions</param>
        /// <returns>Array of bits</returns>
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
                    ErrorListener.Add(new ArgumentException("Warning: incomplete bit sequence detected!"));
                    break;
                }
            }
            return data;
        }
        /// <summary>
        /// Packs arrays of bool-s into an array of bytes.
        /// Mechatrolink-II packet structure is based on bytes, therefore this is convenient.
        /// </summary>
        /// <param name="bits">Bool stand for already decoded bits, not just transitions</param>
        /// <returns></returns>
        public static SortedList<int, byte> PackIntoBytes(SortedList<int, bool> bits)
        {
            int len = bits.Count;
            if (len % 8 != 0)
            {
                ErrorListener.Add(new ArgumentException(string.Format(
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
        /// <summary>
        /// Decodes Manchester-encoded data (edges).
        /// </summary>
        /// <param name="data">Array of edges</param>
        /// <param name="freq">Communication frequency (equal to speed, 4Mbps = 4MHz for Mechatrolink-I and 10MHz for M-II)</param>
        /// <param name="error">Allowed tolerance for edge position (fraction of a period, usually 0.25 = 25%)</param>
        /// <returns>Array of bits</returns>
        public static SortedList<int, bool> Decode(SortedList<int, bool> data, int freq, double error)
        {
            bool req = false; //For interpacket search and then preamble length matching, first look for request preamble (i.e. set req true after interpacket search!)
            data = new SortedList<int, bool>(data);
            int p = (int)Math.Round(1E8 / freq);
            int ph = (int)Math.Round(p * (1 + error));
            int pl = (int)Math.Round(p * (1 - error));
            SortedList<int, int> pulses = new SortedList<int, int>(data.Count - 1);
            for (int i = 0; i < data.Count - 1; i++) //Transform edges into pulses
            {
                //Skip until first long pulse (interpacket)
                if (!req && ((data.Keys[i + 1] - data.Keys[i]) < MaxRequestResponseDelay)) continue;
                pulses.Add(data.Keys[i], data.Keys[i + 1] - data.Keys[i]);
                req = true;
            }
            //Look for the preambles: starts with low-to-high, ends with high-to-low (?)
            SortedList<int, int> preambles = new SortedList<int, int>(pulses.Count / 32); //Assuming the contents are at least as long as the preamble (16*2)
            int current = 0; //Suitable edges found during preamble search
            try
            {
                int state = 0;
                for (int i = 0; i < pulses.Count; i++)
                {
                    switch (state)
                    {
                        case 0: //Look for request preamble
                            if (PreambleHelper(data, pulses, preambles, pl, ph, RequestPreambleLength, ref current, i)) state++;
                            break;
                        case 1: //Look for request-response delay
                            if (pulses.Values[i] > MinRequestResponseDelay)
                            {
                                if (pulses.Values[i] < MaxRequestResponseDelay) state++;
                                else state = 0;
                            }
                            break;
                        case 2: //Look for response preamble
                            if (PreambleHelper(data, pulses, preambles, pl, ph, ResponsePreambleLength, ref current, i)) state++;
                            break;
                        case 3: //Look for response-request delay
                            if (pulses.Values[i] > MaxRequestResponseDelay) state = 0;
                            break;
                        default:
                            ErrorListener.Add(new Exception("Illegal state value for preamble detection state machine."));
                            break;
                    }
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                ErrorListener.Add(new ArgumentException(
                    "Warning: the bitstream contains an incomplete packet. The latter will be discarded."));
            }
            //DataReporter.ReportProgress("Preambles parsed...");
            SortedList<int, bool> bits = new SortedList<int, bool>(data.Capacity);
            int lastPreamble = preambles.Count - 1;
            //Btw, int current is now repurposed
            current = 0;
            for (int i = 0; i < lastPreamble; i++)
            {
                current = PartDecodeManchester(i, current, pl, ph, preambles, data, bits);
            }
            //To avoid processing overhead in previous cycle, process the last packet separately
            if (!IgnoreLastPreamble)
            {
                PartDecodeManchester(lastPreamble, current, pl, ph, preambles, data, bits);
            }
            bits.TrimExcess();
            return bits;
        }
        private static int PartDecodeManchester(int i, int lastIndex, int pl, int ph, 
            SortedList<int, int> preambles, SortedList<int, bool> data, SortedList<int, bool> bits)
        {
            //Preamble key = start timestamp, value = end timestamp
            //Now shift a halfperiod to the right and divide the timeline into timeslots
            //For each packet find transitions that fit into the timeslots
            //Adjust the clock at every recovered edge to prevent error accumulation
            int current = preambles.Values[i] + pl;      //Next data edge lower bound
            int currentHigh = preambles.Values[i] + ph;   //Next data edge higher bound
            for (int j = lastIndex; j < data.Count; j++)
            {
                lastIndex = j; //Optimization purposes: To start next (packet) processing after the last one ended
                if (data.Keys[j] >= preambles.Keys[i + 1]) break; //Detect next packet start
                if (data.Keys[j] > current) //Timeslot fit
                {
                    if (data.Keys[j] < currentHigh)
                    {
                        bits.Add(data.Keys[j], data.Values[j]);
                        current = data.Keys[j] + pl;
                        currentHigh = data.Keys[j] + ph;
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return lastIndex;
        }
        private static bool PreambleHelper(SortedList<int, bool> data, SortedList<int, int> pulses, SortedList<int, int> preambles,
            int pl, int ph, int len, ref int current, int i)
        {
            if ((current == 0) && !data[pulses.Keys[i]]) return false; //Look for a low pulse as a start of preamble
            if (pulses.Values[i] > pl && pulses.Values[i] < ph) //Then check if current pulse suits 10101... pattern (1/2 clock)
            {
                current++;
            }
            else
            {
                current = 0;
                return false;
            }
            if (current == (len - 1))
            //Next (last) transition is determined by the number of preamble bits (assuming the preamble starts with 0)
            //Because preamble is there for a DPLL to lock on the embedded clock, there has to be a
            //010101010... pattern (1/2 of the clock, but phase-aligned!)
            {
                if (len % 2 == 0 ?
                    (!data[pulses.Keys[i + 1]] && pulses.Values[i + 1] < pl) : //Meaningless transition between two zeros if the length is even
                    (data[pulses.Keys[i + 1]] && pulses.Values[i + 1] > pl && pulses.Values[i + 1] < ph)) //No meaningless transition otherwise
                {
                    preambles.Add(pulses.Keys[i - len + 1], pulses.Keys[i + 1]);
                    return true;
                }
                current = 0;
            }
            return false;
        }

        /// <summary>
        /// Searches for flags and extracts contents of the packets.
        /// </summary>
        /// <param name="data">Expects decoded bits with preambles removed.</param>
        /// <returns>Array of packets represented as arrays of bytes</returns>
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
        /// <summary>
        /// Uses CRC-16/X-25 algorithm, also FCS field is little-endian
        /// </summary>
        /// <param name="data">Packet contents without flags and the FCS field itself</param>
        /// <returns>Two bytes (little-endian CRC-16)</returns>
        public static byte[] ComputeFCS(byte[] data)
        {
            return CrcFactory.ComputeHash(data).Hash;
        }
    }
}
