using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MechatrolinkParser
{
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
                        if (dic.ContainsKey(num)) res.AppendLine(string.Format("#{0}={1} - {2}", num, val ? '1' : '0', dic[num]));
                    }
                }
            }
            return res.ToString();
        }
    }
}
