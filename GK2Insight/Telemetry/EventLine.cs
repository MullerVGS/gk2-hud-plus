using System;
using System.Globalization;
using System.Text;

namespace GK2Insight.Telemetry
{
    /// <summary>Uma linha JSON para o /insert/jsonline do VictoriaLogs (_time, _msg e campos planos).</summary>
    internal sealed class EventLine
    {
        private readonly StringBuilder sb = new StringBuilder(256);

        public EventLine(string kind, string msg, string commonJson)
        {
            sb.Append("{\"_time\":\"").Append(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)).Append('"');
            Field("_msg", msg);
            Field("kind", kind);
            sb.Append(commonJson);
        }

        public EventLine Str(string key, string value)
        {
            if (value != null) Field(key, value);
            return this;
        }

        public EventLine Num(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return this;
            sb.Append(",\"").Append(key).Append("\":").Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            return this;
        }

        public EventLine Bool(string key, bool value)
        {
            sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");
            return this;
        }

        public string Build() => sb.ToString() + "}";

        private void Field(string key, string value)
        {
            sb.Append(",\"").Append(key).Append("\":");
            Quote(sb, value);
        }

        public static void Quote(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
