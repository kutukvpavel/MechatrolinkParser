using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MechatrolinkParser
{
    public static class ErrorListener
    {
        public static bool PrintOnlyDistinctExceptions { get; set; } = true;

        private static List<Exception> list = new List<Exception>();
        public static Exception[] Exceptions
        {
            get { return list.ToArray(); }
        }
        public static void Add(Exception e)
        {
            list.Add(e);
        }
        public static void Clear()
        {
            list.Clear();
        }
        public static new string ToString()
        {
            StringBuilder res = new StringBuilder();
            var data = list;
            if (PrintOnlyDistinctExceptions)
            {
                ExceptionComparer comparer = new ExceptionComparer();
                data = data.Distinct(comparer).ToList();
            }
            foreach (var item in data)
            {
                res.AppendFormat("{0}: {1}" + Environment.NewLine,
                    item.Message, item.InnerException != null ? item.InnerException.Message : "");
                res.AppendLine();
            }
            return res.ToString();
        }

        private class ExceptionComparer : IEqualityComparer<Exception>
        {
            public bool Equals(Exception x, Exception y)
            {
                bool current = (x.GetType() == y.GetType()) && (x.Message == y.Message);
                bool inner = (x.InnerException.GetType() == y.InnerException.GetType()) &&
                    (x.InnerException.Message == y.InnerException.Message);
                return current && inner;
            }

            public int GetHashCode(Exception obj)
            {
                int res = obj.GetType().GetHashCode();
                unchecked
                {
                    res = res * 31 + obj.Message.GetHashCode();
                    res = res * 31 + obj.InnerException.Message.GetHashCode();
                }
                return res;
            }
        }
    }
}
