using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GK2Shared
{
    /// <summary>
    /// Trabalho fatiado entre frames (time slicing): uma fila de passos pequenos que roda só até gastar o orçamento do
    /// frame. Serve para o que é preguiçoso mas pode ser adiantado sem pressa (pré-carregar o que está perto da vista,
    /// decodificar arte antes do primeiro uso), sem que uma tela grande vire uma travada. O que o jogador precisa ver já
    /// é feito na hora por quem chama; a fila só adianta o resto.
    /// </summary>
    internal sealed class FrameBudget
    {
        private readonly List<Action> queue = new List<Action>();
        private int head;

        public int Pending => queue.Count - head;

        public void Add(Action work)
        {
            if (work != null) queue.Add(work);
        }

        public void Clear()
        {
            queue.Clear();
            head = 0;
        }

        /// <summary>Roda passos até gastar <paramref name="ms"/> (ao menos um, para sempre avançar). Devolve quantos rodaram.</summary>
        public int Run(double ms)
        {
            if (head >= queue.Count)
            {
                if (queue.Count > 0) Clear();
                return 0;
            }
            long end = Stopwatch.GetTimestamp() + (long)(ms * Stopwatch.Frequency / 1000.0);
            int n = 0;
            while (head < queue.Count)
            {
                Action step = queue[head];
                queue[head++] = null;
                n++;
                step();
                if (Stopwatch.GetTimestamp() >= end) break;
            }
            if (head >= queue.Count) Clear();
            return n;
        }
    }
}
