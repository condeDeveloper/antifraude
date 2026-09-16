using Antifraude.Core.Dominio;

namespace Antifraude.Tests;

/// <summary>Transações de exemplo para os testes.</summary>
public static class Fabrica
{
    public static readonly DateTimeOffset T0 = new(2026, 9, 16, 14, 0, 0, TimeSpan.Zero);

    public static Transacao Tx(string id = "t1", decimal valor = 100m, string cartao = "c1", string pais = "BR", bool presente = true,
        DateTimeOffset? momento = null, string comerciante = "MERCADO", string cliente = "cli1", Dictionary<string, string>? extras = null, string ip = "10.0.0.1") =>
        new(id, momento ?? T0, valor, "BRL", cartao, cliente, comerciante, "5411", pais, ip, "dev1", presente, extras);
}
