using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.HashFunction.CRC;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    /// <summary>
    /// Static method wrapper
    /// </summary>
    public static class HDLCManchesterDecoder
    {
        /// <summary>
        /// Mechatrolink-II uses the same flag (packet start and end marker) value the HDLC does.
        /// </summary>
        public const byte Flag = 0x7E;

        /// <summary>
        /// Remove zero bits stuffed in for transparency of the protocol
        /// </summary>
        /// <param name="data">Bool stand for already decoded bits, not just transitions</param>
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
                    Program.ErrorListener.Add(new ArgumentException("Warning: incomplete bit sequence detected!"));
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
        /// <summary>
        /// Decodes Manchester-encoded data (edges).
        /// </summary>
        /// <param name="data">Array of edges</param>
        /// <param name="freq">Communication frequency (equal to speed, 4Mbps = 4MHz for Mechatrolink-I and 10MHz for M-II)</param>
        /// <param name="error">Allowed tolerance for edge position (fraction of a period, usually 0.25 = 25%)</param>
        /// <returns>Array of bits</returns>
        public static SortedList<int, bool> Decode(SortedList<int, bool> data, int freq, double error)
        {
            data = new SortedList<int, bool>(data);
            int p = (int)Math.Round(1E8 / freq);
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
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] ComputeFCS(byte[] data)
        {
            var crc = CRCFactory.Instance.Create(CRCConfig.X25);
            return crc.ComputeHash(data).Hash;
        }
    }
}
