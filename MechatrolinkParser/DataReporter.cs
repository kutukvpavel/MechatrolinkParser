using System;
using System.Linq;
using System.Text;

namespace MechatrolinkParser
{
    public static class DataReporter
    {
        public static bool EnableProgressOutput { get; set; } = true;
        public static bool FilterOutput { get; set; } = false;

        public static readonly string BroadcastPacketReport = "================ Info: Broadcast message ================" + Environment.NewLine;

        private static readonly string EncoderPacketFormat = "Timestamp: {0}" + Environment.NewLine +
            "FCS {1}: {2} (computed: {3})" + Environment.NewLine;
        private static readonly string MechatrolinkPacketHeaderFormat = "Timestamp: {0}" + Environment.NewLine +
            "Address: {1}" + Environment.NewLine +
            "Control: {2}" + Environment.NewLine + "FCS {3}: {4} (computed: {5})" + Environment.NewLine;
        private static readonly string MechatrolinkPacketDataFormat = "Command: {0}" + Environment.NewLine +
            "WDR: {1}" + Environment.NewLine + "Data: {2}" + Environment.NewLine;

        public static string CreateEncoderReportString(EncoderCommunication data)
        {
            StringBuilder res = new StringBuilder(
                string.Format("Encoder communication session. Packets: {0}, period: {1}."
                + Environment.NewLine + Environment.NewLine,
                data.Packets.Length, data.Period));
            foreach (var item in data.Packets)
            {
                //if (FilterOutput) if (DetectNonsense(item)) continue;
                res.AppendLine("///////////////////////////////////// Packet ///////////////////////////////////");
                if (item.Command.ParsedFields.All(x => x.Length == 0))
                {
                    res.AppendLine(BroadcastPacketReport);
                    continue;
                }
                res.AppendLine("Raw: " + string.Join(" ",
                    item.ParsedData.Select(x => ArrayToString(x ?? new byte[0]))));
                res.AppendFormat(EncoderPacketFormat, item.Timestamp,
                    item.FCSError ? "ERROR" : "OK",
                    ArrayToString(item.ParsedData[(int)EncoderPacket.Fields.FCS]),
                    ArrayToString(item.ComputedFCS));
                res.AppendLine(item.DatabaseReport);
                //res.AppendLine();
            }
            //ReportProgress("Report created...");
            return res.ToString();
        }

        public static string CreateMechatrolinkReportString(MechatrolinkCommunication data)
        {
            StringBuilder res = new StringBuilder(
                string.Format("Mechatrolink communication session. Packets: {0}, period: {1}."
                + Environment.NewLine + Environment.NewLine,
                data.Packets.Length, data.Period));
            foreach (var item in data.Packets)
            {
                if (FilterOutput) if (DetectNonsense(item)) continue;
                res.AppendLine("///////////////////////////////////// Packet ///////////////////////////////////");
                res.AppendLine("Raw: " + string.Join(" ",
                    item.ParsedData.Select(x => ArrayToString(x ?? new byte[0]))));
                res.AppendLine("================ HEADER ==================");
                res.AppendFormat(MechatrolinkPacketHeaderFormat, item.Timestamp,
                    ArrayToString(item.ParsedData[(int)MechatrolinkPacket.Fields.Address]),
                    ArrayToString(item.ParsedData[(int)MechatrolinkPacket.Fields.Control]),
                    item.FCSError ? "ERROR" : "OK",
                    ArrayToString(item.ParsedData[(int)MechatrolinkPacket.Fields.FCS]),
                    ArrayToString(item.ComputedFCS));
                res.AppendLine("================ COMMAND ==================");
                res.AppendFormat(MechatrolinkPacketDataFormat,
                    ArrayToString(item.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Code]),
                    ArrayToString(item.Command.ParsedFields[(int)MechatrolinkCommand.Fields.WDT]),
                    ArrayToString(item.Command.ParsedFields[(int)MechatrolinkCommand.Fields.Data]));
                if (item.Command.ContainsSubcommand)
                {
                    res.AppendLine("================= SUBCOMMAND ==================");
                    res.AppendFormat(MechatrolinkPacketDataFormat,
                        ArrayToString(item.Command.ParsedFields[(int)MechatrolinkCommand.Fields.SubcommandCode]),
                        " -- ",
                        ArrayToString(item.Command.ParsedFields[(int)MechatrolinkCommand.Fields.SubcommandData]));
                }
                res.AppendLine(item.DatabaseReport);
                //res.AppendLine();
            }
            //ReportProgress("Report created...");
            return res.ToString();
        }

        private static string ArrayToString(byte[] arr)
        {
            return string.Join(" ", arr.Select(x => x.ToString("X2")));
        }
        private static bool DetectNonsense(MechatrolinkPacket packet)
        {
            byte command = packet.Command.ParsedFields[(byte)MechatrolinkCommand.Fields.Code][0];
            if (command == 0x0D || command == 0x0E || command == 0x0F) return false;
            byte addr = packet.ParsedData[(int)MechatrolinkPacket.Fields.Address][0];
            if (addr == 0x00) return true;
            if (addr == MechatrolinkCommandDatabase.SyncFrameAddress) return false;
            byte control = packet.ParsedData[(int)MechatrolinkPacket.Fields.Control][0];
            if (control != MechatrolinkCommandDatabase.RequestControlCode && control != MechatrolinkCommandDatabase.ResponseControlCode) return true;
            return false;
        }

        public static void ReportProgress(string data)
        {
            if (EnableProgressOutput) Console.WriteLine(string.Format("{0:T}: {1}", DateTime.Now, data));
        }
    }
}
