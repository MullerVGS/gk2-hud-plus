using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace GK2Shared
{
    /// <summary>
    /// Gravação de arquivo fora da thread do jogo. Disco lento, antivírus ou nuvem sincronizando a pasta travavam o frame
    /// em dezenas de ms quando o arquivo era escrito ali. Quem chama entrega uma cópia do que quer gravar (o conteúdo é
    /// produzido na thread de fundo, então não pode tocar em nada que o jogo mexe); pedidos para o mesmo caminho se fundem
    /// (vale o mais novo). A escrita é atômica: <c>.tmp</c> e troca. Erros ficam guardados para a thread do jogo ler e
    /// registrar no log (<see cref="TakeError"/>).
    /// </summary>
    internal sealed class AsyncFileWriter
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, Func<string>> pending = new Dictionary<string, Func<string>>();
        private bool busy;
        private string error;

        public void Write(string path, Func<string> content)
        {
            lock (gate)
            {
                pending[path] = content;
                if (busy) return;
                busy = true;
            }
            ThreadPool.QueueUserWorkItem(_ => Drain());
        }

        private void Drain()
        {
            while (true)
            {
                string path;
                Func<string> content;
                lock (gate)
                {
                    if (pending.Count == 0)
                    {
                        busy = false;
                        Monitor.PulseAll(gate);
                        return;
                    }
                    var e = pending.GetEnumerator();
                    e.MoveNext();
                    path = e.Current.Key;
                    content = e.Current.Value;
                    pending.Remove(path);
                }
                try { WriteAtomic(path, content()); }
                catch (Exception ex) { lock (gate) error = path + ": " + ex.Message; }
            }
        }

        public static void WriteAtomic(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        /// <summary>Espera o que está na fila ser gravado (troca de save, saída, hot-reload). Falso se o tempo acabou.</summary>
        public bool WaitIdle(int timeoutMs)
        {
            lock (gate)
            {
                int end = Environment.TickCount + timeoutMs;
                while (busy)
                {
                    int left = end - Environment.TickCount;
                    if (left <= 0 || !Monitor.Wait(gate, left)) return !busy;
                }
                return true;
            }
        }

        /// <summary>Último erro de gravação (e o limpa); null se não houve.</summary>
        public string TakeError()
        {
            lock (gate)
            {
                string e = error;
                error = null;
                return e;
            }
        }
    }
}
