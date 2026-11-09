using System.Globalization;
using System.Text;

namespace GK2Insight.Telemetry
{
    /// <summary>
    /// Linhas no formato de texto do Prometheus com timestamp explícito (ms), para o /api/v1/import/prometheus do
    /// VictoriaMetrics. Os rótulos comuns (instance) entram em todas as linhas.
    /// </summary>
    internal sealed class MetricBatch
    {
        private const string Prefix = "gk2_";
        private readonly StringBuilder sb = new StringBuilder(8 * 1024);
        private long ts;
        private string common = "";

        public int Lines { get; private set; }

        public void Begin(long utcMs, string commonLabels)
        {
            sb.Length = 0;
            Lines = 0;
            ts = utcMs;
            common = commonLabels;
        }

        public void Add(string name, double value, string k1 = null, string v1 = null, string k2 = null, string v2 = null)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return;
            sb.Append(Prefix).Append(name).Append('{').Append(common);
            if (k1 != null) Label(k1, v1);
            if (k2 != null) Label(k2, v2);
            sb.Append("} ").Append(value.ToString("R", CultureInfo.InvariantCulture)).Append(' ').Append(ts).Append('\n');
            Lines++;
        }

        public string Text => sb.ToString();

        private void Label(string k, string v)
        {
            sb.Append(',').Append(k).Append("=\"");
            foreach (char c in v ?? "")
            {
                if (c == '\\' || c == '"') sb.Append('\\');
                sb.Append(c == '\n' ? ' ' : c);
            }
            sb.Append('"');
        }

        public static string Labels(string k, string v) => $"{k}=\"{v.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
    }
}
