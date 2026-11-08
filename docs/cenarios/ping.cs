using UnityEngine;

// Cenário mínimo: prova que o `gk2 run` compila e executa dentro do jogo.
public static class Snippet
{
    public static object Run()
    {
        Debug.Log("[cenario] olá do green, frame " + Time.frameCount);
        return new { scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name, Time.frameCount };
    }
}
