using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    public static class CommandDatabase
    {
        public enum CommonFieldTypes
        {
            Status,
            Alarm,
            MonitorSelect,
            Monitor1,
            Monitor2,
            IO
        }

        public const byte ResponseControlCode = 0x01;
        /// <summary>
        /// Fields are numbered as per the documentation on Sigma-II drives,
        /// -2 offset that results from command code stripping and byte numbering is internally taken care of.
        /// </summary>
        public static readonly Dictionary<byte, CommandInfo> Database;
        public static readonly Dictionary<CommonFieldTypes, Field> CommonFields;

        static CommandDatabase()
        {
            CommonFields = new Dictionary<CommonFieldTypes, Field>
            {
                { CommonFieldTypes.Alarm, new Field("ALARM", 2) },
                { CommonFieldTypes.MonitorSelect, new Field("MON_SEL", 13) { CustomParser = CustomParsers.MonitorSelectParser } },
                { CommonFieldTypes.Status, new Field("STATUS", 3, 4) }
            };
            Database = new List<CommandInfo>()
            {
                new CommandInfo("NOP", "No operation", 0x00,
                    new Field[0],
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        new Field("STATUS", 3) //1-byte status field!!
                    }
                ),
                new CommandInfo("SMON", "Servo status monitoring", 0x30,
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.MonitorSelect]
                    },
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        CommonFields[CommonFieldTypes.Status],
                        new Field("MONITOR1", 5, 6, 7, 8),
                        new Field("MONITOR2", 9, 10, 11, 12),
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        new Field("I/O", 14)
                    }
                )
            }.ToDictionary(x => x.Code);
        }

        public static string GetReport(Packet packet)
        {
            try
            {
                var info = Database[packet.Command.ParsedFields[(int)Command.Fields.Code][0]];
                return info.GetReport(packet.Command.ParsedFields[(int)Command.Fields.Data],
                    packet.ParsedData[(int)Packet.Fields.Control][0] == ResponseControlCode);
            }
            catch (KeyNotFoundException) { }
            catch (Exception e)
            {
                Program.ErrorListener.Add(new Exception("Error during database search. Operation aborted.", e));
            }
            return "";
        }
    }

    public class CommandInfo
    {
        public CommandInfo(string alias, string name, byte code, Field[] request, Field[] response)
        {
            Name = name;
            Code = code;
            FieldsRequest = request;
            FieldsResponse = response;
            Alias = alias;
        }

        public string Name { get; }
        public string Alias { get; }
        public byte Code { get; }
        public Field[] FieldsRequest { get; }
        public Field[] FieldsResponse { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="bytes">Expects stripped data bytes (no command code and WDT)</param>
        /// <param name="response"></param>
        /// <returns></returns>
        public string GetReport(byte[] bytes, bool response)
        {
            StringBuilder res = new StringBuilder(string.Format(@"
============== Info: {0}, {1} ===============
Name: {2}, alias: {3}.
", response ? "RESP" : "REQ", Code.ToString("X"), Name, Alias));
            var f = response ? FieldsResponse : FieldsRequest;
            foreach (var item in f)
            {
                res.Append(item.GetReport(bytes));
            }
            res.AppendLine("==================================================");
            return res.ToString();
        }
    }

    public class Field
    {
        public Field(string name, params int[] bytes)
        {
            Name = name;
            Bytes = bytes;
        }
        public Field(string name, bool endianess, params int[] bytes) : this(name, bytes)
        {
            LittleEndian = endianess;
        }

        public string Name { get; }
        /// <summary>
        /// As per documentation. -2 offset caused by stripped command code and counting from 0 is taken into account internally 
        /// </summary>
        public int[] Bytes { get; }
        public bool LittleEndian { get; } = false;
        public Func<byte[], string> CustomParser { get; set; }

        public string GetReport(params byte[] bytes)
        {
            bytes = bytes.Where((byte i, int j) => { return Bytes.Contains(j + 2); }).ToArray(); //Filter only the bytes of interest
            StringBuilder res = new StringBuilder(string.Format(@"----------------- Field: {0} ----------------
", Name));
            res.Append(string.Join(" ", bytes.Select(x => x.ToString("X2"))));
            res.Append(" = "); //Add binary representation
            res.AppendLine(string.Join(" ", bytes.Select(x => Convert.ToString(x, 2).PadLeft(8, '0'))));
            if (CustomParser != null)
            {
                try
                {
                    res.AppendLine(CustomParser(bytes));
                }
                catch (Exception e)
                {
                    Program.ErrorListener.Add(new Exception(
                        string.Format("Error in a custom parser for field {0}", Name), e));
                }
            }
            //res.AppendLine("------------------");
            return res.ToString();
        }
    }

    /// <summary>
    /// Parsers for field values
    /// </summary>
    public static class CustomParsers
    {
        public static readonly Dictionary<byte, string> MonitorSelectData = new Dictionary<byte, string>
        {
            { 0x00, "Position in the ref. coord. sys." },
            { 0x01, "Position in the mech. coord. sys." },
            { 0x02, "Position error" },
            { 0x03, "Absolute position" },
            { 0x04, "Counter latch position" },
            { 0x05, "Internal position in the ref. coord. sys." },
            { 0x06, "Target position" },
            //{ 0x07, "" },
            { 0x08, "Feedback speed" },
            { 0x09, "Reference speed" },
            { 0x0A, "Target speed" },
            { 0x0B, "Torque reference" },
            //{ 0x0C, "" },
            //{ 0x0D, "" },
            { 0x0E, "Option monitor 1" },
            { 0x0F, "Option monitor 2" },
        };

        public static string MonitorSelectParser(byte[] bytes)
        {
            //This is a one-byte field
            byte data = bytes[0];
            //Upper 4 bits determine Monitor2 response and the same for lower 4 bits and Monitor1
            return string.Format("MON_SEL(1,2): {0}, {1}",
                MonitorSelectData[(byte)(data & 0x0F)], MonitorSelectData[(byte)(data >> 4)]);
        }
    }
}
