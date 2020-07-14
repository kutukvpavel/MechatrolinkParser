using System;
using System.Collections.Generic;
using System.Linq;

namespace MechatrolinkParser
{
    public static class MechatrolinkCommandDatabase
    {
        public enum CommonFieldTypes
        {
            Status,
            Alarm,
            MonitorSelect,
            IO,
            TargetPosition,
            Monitor1,
            Monitor2,
            FeedForwardSpeed,
            Option
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

        static MechatrolinkCommandDatabase()
        {
            CommonFields = new Dictionary<CommonFieldTypes, Field>
            {
                { CommonFieldTypes.Alarm, new Field("ALARM", 2) { CustomParser = MechatrolinkCustomParsers.AlarmParser } },
                { CommonFieldTypes.MonitorSelect, new Field("MON_SEL", 13) { CustomParser = MechatrolinkCustomParsers.MonitorSelectParser } },
                { CommonFieldTypes.Status, new BitField("STATUS", true, 3, 4)
                    {
                        BitsSetDesc = MechatrolinkCustomParsers.StatusBitsSet,
                        BitsUnsetDesc = MechatrolinkCustomParsers.StatusBitsUnset
                    }
                },
                { CommonFieldTypes.IO, new BitField("I/O", true, 14, 15)
                    {
                        BitsSetDesc = MechatrolinkCustomParsers.IOBitsSet,
                        BitsUnsetDesc = MechatrolinkCustomParsers.IOBitsUnset
                    }
                },
                { CommonFieldTypes.TargetPosition, new Field("TPOS", true, 5, 6, 7, 8) },
                { CommonFieldTypes.Monitor1, new Field("MONITOR1", true, 5, 6, 7, 8) },
                { CommonFieldTypes.Monitor2, new Field("MONITOR2", true, 9, 10, 11, 12) },
                { CommonFieldTypes.FeedForwardSpeed, new Field("VFF", true, 9, 10, 11, 12) },
                { CommonFieldTypes.Option, new Field("OPTION", 3, 4) }
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
                            BitsSetDesc = MechatrolinkCustomParsers.StatusBitsSet,
                            BitsUnsetDesc = MechatrolinkCustomParsers.StatusBitsUnset
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
                        CommonFields[CommonFieldTypes.Monitor1],
                        CommonFields[CommonFieldTypes.Monitor2],
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.IO]
                    }
                ),
                new CommandInfo("INTERPOLATE", "Synchronous positioning", 0x34,
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.Option],
                        CommonFields[CommonFieldTypes.TargetPosition],
                        CommonFields[CommonFieldTypes.FeedForwardSpeed]
                    },
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        CommonFields[CommonFieldTypes.Status],
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.IO],
                        CommonFields[CommonFieldTypes.Monitor1],
                        CommonFields[CommonFieldTypes.Monitor2]
                    }
                ),
                new CommandInfo("SV_ON", "Turn servo ON", 0x31,
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Option],
                        CommonFields[CommonFieldTypes.MonitorSelect]
                    },
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        CommonFields[CommonFieldTypes.Status],
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.IO],
                        CommonFields[CommonFieldTypes.Monitor1],
                        CommonFields[CommonFieldTypes.Monitor2]
                    }
                ),
                new CommandInfo("SV_OFF", "Turn servo OFF", 0x32,
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.MonitorSelect]
                    },
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        CommonFields[CommonFieldTypes.Status],
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.IO],
                        CommonFields[CommonFieldTypes.Monitor1],
                        CommonFields[CommonFieldTypes.Monitor2]
                    }
                ),
                new CommandInfo("POSING", "Async positioning", 0x35,
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.Option],
                        CommonFields[CommonFieldTypes.TargetPosition],
                        new Field("TSPD", 9, 10, 11, 12)
                    },
                    new Field[]
                    {
                        CommonFields[CommonFieldTypes.Alarm],
                        CommonFields[CommonFieldTypes.Status],
                        CommonFields[CommonFieldTypes.MonitorSelect],
                        CommonFields[CommonFieldTypes.IO],
                        CommonFields[CommonFieldTypes.Monitor1],
                        CommonFields[CommonFieldTypes.Monitor2]
                    }
                )
            }.ToDictionary(x => x.Code);
        }

        public static string GetReport(MechatrolinkPacket packet)
        {
            try
            {
                if (packet.ParsedData[(int)MechatrolinkPacket.Fields.Address][0] == SyncFrameAddress) return SyncFrameReport;
                var info = Database[packet.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Code][0]];
                return info.GetReport(packet.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Data],
                    packet.ParsedData[(int)MechatrolinkPacket.Fields.Control][0] == ResponseControlCode);
            }
            catch (KeyNotFoundException) { }
            catch (Exception e)
            {
                ErrorListener.Add(new Exception("Error during database search. Operation aborted.", e));
            }
            return "";
        }
    }


    /// <summary>
    /// Parsers for field values
    /// </summary>
    public static class MechatrolinkCustomParsers
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
            //{ 3, "Phase A OFF" },  not true for SGDM
            //{ 4, "Phase B OFF" },
            //{ 5, "Phase C OFF" },
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

}
