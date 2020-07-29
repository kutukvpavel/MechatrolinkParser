using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MechatrolinkParser
{
    class EncoderCommandDatabase
    {
        public enum CommonFieldTypes
        {
            
        }

        //public const byte ResponseControlCode = 0x01;
        //public const byte RequestControlCode = 0x03;
        //public const byte SyncFrameAddress = 0xFF;

        /// <summary>
        /// -2 offset that results from command code stripping and byte numbering is internally taken care of.
        /// </summary>
        public static readonly Dictionary<byte, CommandInfo> Database;
        public static readonly Dictionary<CommonFieldTypes, Field> CommonFields;           
        public static readonly string BroadcastPacketReport = "================ Info: Broadcast message ================" + Environment.NewLine;


        static EncoderCommandDatabase()
        {
            CommonFields = new Dictionary<CommonFieldTypes, Field>
            {
                //{ CommonFieldTypes.Alarm, new Field("ALARM", 2) }
            };
            Database = new List<CommandInfo>()
            {
                /*new CommandInfo("NOP", "No operation", 0x00,
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
                ),*/
            }.ToDictionary(x => x.Code);
        }

        public static string GetReport(EncoderPacket packet)
        {
            try
            {
                /*if (packet.ParsedData[(int)MechatrolinkPacket.Fields.Address][0] == SyncFrameAddress) return SyncFrameReport;
                var info = Database[packet.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Code][0]];
                return info.GetReport(packet.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Data],
                    packet.ParsedData[(int)MechatrolinkPacket.Fields.Control][0] == ResponseControlCode);*/
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
    public static class EncoderCustomParsers
    {
        
    }
}
