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
        public const byte RequestControlCode = 0x03;
        public const byte SyncFrameAddress = 0xFF;

        /// <summary>
        /// Fields are numbered as per the documentation on Sigma-II drives,
        /// -2 offset that results from command code stripping and byte numbering is internally taken care of.
        /// </summary>
        public static readonly Dictionary<byte, CommandInfo> Database;
        public static readonly Dictionary<CommonFieldTypes, Field> CommonFields;
        public static readonly string SyncFrameReport = "================ Info: SYNC frame ================" + Environment.NewLine;

        static CommandDatabase()
        {
            CommonFields = new Dictionary<CommonFieldTypes, Field>
            {
                { CommonFieldTypes.Alarm, new Field("ALARM", 2) { CustomParser = CustomParsers.AlarmParser } },
                { CommonFieldTypes.MonitorSelect, new Field("MON_SEL", 13) { CustomParser = CustomParsers.MonitorSelectParser } },
                { CommonFieldTypes.Status, new BitField("STATUS", true, 3, 4)
                    {
                        BitsSetDesc = CustomParsers.StatusBitsSet,
                        BitsUnsetDesc = CustomParsers.StatusBitsUnset
                    }
                },
                { CommonFieldTypes.IO, new BitField("I/O", true, 14, 15)
                    {
                        BitsSetDesc = CustomParsers.IOBitsSet,
                        BitsUnsetDesc = CustomParsers.IOBitsUnset
                    }
                }
            };
            Database = new List<CommandInfo>()
            {
                new CommandInfo("NOP", "No operation", 0x00,
                    new Field[0],
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        new BitField("STATUS", 3) //1-byte status field!!
                        {
                            BitsSetDesc = CustomParsers.StatusBitsSet,
                            BitsUnsetDesc = CustomParsers.StatusBitsUnset
                        }
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
                        CommonFields[CommonFieldTypes.IO]
                    }
                )
            }.ToDictionary(x => x.Code);
        }

        public static string GetReport(Packet packet)
        {
            try
            {
                if (packet.ParsedData[(int)Packet.Fields.Address][0] == SyncFrameAddress) return SyncFrameReport;
                var info = Database[packet.Command.ParsedFields[(int)Command.Fields.Code][0]];
                return info.GetReport(packet.Command.ParsedFields[(int)Command.Fields.Data],
                    packet.ParsedData[(int)Packet.Fields.Control][0] == ResponseControlCode);
            }
            catch (KeyNotFoundException) { }
            catch (Exception e)
            {
                ErrorListener.Add(new Exception("Error during database search. Operation aborted.", e));
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
================ Info: {0}, {1} =================
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

        public virtual string GetReport(params byte[] bytes)
        {
            //Get only the bytes that belong to the field
            bytes = bytes.Where((byte i, int j) => { return Bytes.Contains(j + 2); }).ToArray();
            if (LittleEndian) bytes = bytes.Reverse().ToArray();
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
                    ErrorListener.Add(new Exception(
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
        /// <summary>
        /// According to Sigma-II manuals
        /// </summary>
        public static readonly Dictionary<byte, string> MonitorSelectData = new Dictionary<byte, string>
        {
            { 0x00, "Position in the ref. coord. sys." },
            { 0x01, "Position in the mech. coord. sys." },
            { 0x02, "Position error" },
            { 0x03, "Absolute position" },
            { 0x04, "Counter latch position" },
            { 0x05, "Internal position in the ref. coord. sys." },
            { 0x06, "Target position" },
            { 0x08, "Feedback speed" },
            { 0x09, "Reference speed" },
            { 0x0A, "Target speed" },
            { 0x0B, "Torque reference" },
            { 0x0E, "Option monitor 1" },
            { 0x0F, "Option monitor 2" },
        };
        /// <summary>
        /// According to general command specifications (servo/stepper-related)
        /// Most devices use vendor-specific codes, that are allowed to start from 0x80
        /// </summary>
        public static readonly Dictionary<byte, string> AlarmData = new Dictionary<byte, string>
        {
            { 0x00, "Normal" },
            { 0x01, "Invalid command" },
            { 0x02, "Cmd not allowed" },
            { 0x03, "Invalid data" },
            { 0x04, "Sync error" },
            { 0x05, "Transmission setting not supported" },
            { 0x06, "Communication error (warning happened twice)" },
            { 0x07, "Communication warning" },
            { 0x08, "Transmission cycle changed during comm." }
        };
        /// <summary>
        /// According to general command specifications (servo/stepper-related)
        /// </summary>
        public static Dictionary<int, string> StatusBitsSet = new Dictionary<int, string>
        {
            { 0, "Alarm" },
            { 1, "Warning" },
            { 3, "Servo ON" },
            { 4, "Main PSU ON" },
            { 6, "Inside Zero Point" },
            { 7, "Pos./vel. reached" },
            { 8, "Zero speed det./Ref out. completed" },
            { 10, "Latch completed" },
            { 11, "Inside pos./vel. limits" },
            { 12, "Outside forward limit" },
            { 13, "Outside reverse limit" }
        };
        /// <summary>
        /// According to general command specifications (servo/stepper-related)
        /// </summary>
        public static Dictionary<int, string> StatusBitsUnset = new Dictionary<int, string>
        {
            { 2, "Busy" },
            { 3, "Servo OFF" },
            { 4, "Main PSU OFF" },
            { 6, "Outside Zero Point" },
            { 7, "Pos./vel. NOT reached" },
            { 8, "Zero speed NOT det./ref. out NOT completed" },
            { 10, "Latch NOT completed" },
            { 11, "Outside pos./vel. limits" }
        };
        /// <summary>
        /// According to general command specifications (servo/stepper-related)
        /// May differ for some devices!
        /// </summary>
        public static Dictionary<int, string> IOBitsSet = new Dictionary<int, string>
        {
            { 0, "Forward over-travel" },
            { 1, "Reverse over-travel" },
            { 2, "Deceleration limit switch ON" },
            { 6, "First external latch ON" },
            { 7, "Second external latch ON" },
            { 8, "Third external latch ON" },
            { 9, "Brake ON" },
            { 12, "GPI 1 ON" },
            { 13, "GPI 2 ON" },
            { 14, "GPI 3 ON" },
            { 15, "GPI 4 ON" }
        };
        /// <summary>
        /// According to general command specifications (servo/stepper-related)
        /// May differ for some devices!
        /// </summary>
        public static Dictionary<int, string> IOBitsUnset = new Dictionary<int, string>
        {
            { 3, "Phase A OFF" },
            { 4, "Phase B OFF" },
            { 5, "Phase C OFF" },
            { 9, "Brake OFF" }
        };


        /// <summary>
        /// According to Sigma-II series manuals
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public static string MonitorSelectParser(byte[] bytes)
        {
            //This is a one-byte field
            byte data = bytes[0];
            //Upper 4 bits determine Monitor2 response and the same for lower 4 bits and Monitor1
            return string.Format("MON_SEL(1,2): {0}, {1}",
                MonitorSelectData[(byte)(data & 0x0F)], MonitorSelectData[(byte)(data >> 4)]);
        }

        /// <summary>
        /// According to general command specifications ("Command specification for Stepper Motors")
        /// It seems there's no publicly available specification for servos
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public static string AlarmParser(byte[] bytes)
        {
            //This is a single-byte field
            byte data = bytes[0];
            //There's is just a table of alarm codes
            return AlarmData[data];
        }
    }

    public class BitField : Field
    {
        public BitField(string name, params int[] bytes) : base(name, bytes)
        { }
        public BitField(string name, bool endianess, params int[] bytes) : base(name, endianess, bytes)
        { }

        //public new Func<int, bool, string> CustomParser { get; set; }
        public Dictionary<int, string> BitsSetDesc { get; set; }
        public Dictionary<int, string> BitsUnsetDesc { get; set; }

        public override string GetReport(params byte[] bytes)
        {
            StringBuilder res = new StringBuilder(base.GetReport(bytes));  //This sets up "Field" headline and binary representation
            //Now parse bits
            bytes = bytes.Where((byte b, int i) => { return Bytes.Contains(i + 2); }).ToArray();
            if (!LittleEndian) bytes = bytes.Reverse().ToArray();  //The following cycle reverses the order one again!
            for (int i = 0; i < bytes.Length; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    bool val = (bytes[i] & (1u << j)) != 0;
                    Dictionary<int, string> dic = val ? BitsSetDesc : BitsUnsetDesc;
                    if (dic != null)
                    {
                        int num = i * 8 + j;
                        if (dic.ContainsKey(num)) res.AppendLine(string.Format("#{0}={1} - {2}", num, val ? '1' : '0' , dic[num]));
                    }
                }
            }
            return res.ToString();
        }
    }
}
