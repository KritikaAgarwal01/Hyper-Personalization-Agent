using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CCH.HPSO.Dataverse.Plugins
{
    /// <summary>
    /// Minimal JSON writer for scalar values and flat arrays of scalars.
    /// Kept dependency-free so the plugin assembly needs no external libraries in the sandbox.
    /// </summary>
    internal static class JsonUtil
    {
        public static string WriteScalar(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        public static string WriteArray(IEnumerable<object> values)
        {
            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (var v in values)
            {
                if (!first) sb.Append(',');
                WriteValue(sb, v);
                first = false;
            }
            sb.Append(']');
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            switch (value)
            {
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case decimal m:
                    sb.Append(m.ToString(CultureInfo.InvariantCulture));
                    break;
                case double d:
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case DateTime dt:
                    WriteString(sb, dt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                    break;
                case Guid g:
                    WriteString(sb, g.ToString());
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (ch < ' ')
                            sb.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            sb.Append(ch);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
