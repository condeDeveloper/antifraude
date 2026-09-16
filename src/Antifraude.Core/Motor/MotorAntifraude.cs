using Antifraude.Core.Dominio;
using Antifraude.Core.Historico;
using Antifraude.Core.Regras;

namespace Antifraude.Core.Motor;

/// <summary>Estatísticas de uma simulação em lote.</summary>
public sealed record ResultadoSimulacao(
    int Transacoes,
    int Aprovadas,
    int Revisadas,
    int Negadas,
    IReadOnlyDictionary<string, int> DisparosPorRegra,
    IReadOnlyDictionary<string, int> ErrosPorRegra,
    double DuracaoMediaMs,
    IReadOnlyList<Avaliacao> Avaliacoes);

/// <summary>Orquestra política e histórico: avalia em tempo real e simula lotes sem contaminar o histórico real.</summary>
public sealed class MotorAntifraude
{
    private readonly object _trava = new();

    public MotorAntifraude(Politica politica, JanelaDeslizante? historico = null)
    {
        Politica = politica;
        Historico = historico ?? new JanelaDeslizante();
    }

    public Politica Politica { get; }
    public JanelaDeslizante Historico { get; }
    public long TotalAvaliadas { get; private set; }

    /// <summary>Avalia e registra no histórico (para as próximas funções de velocidade).</summary>
    public Avaliacao Avaliar(Transacao t, DateTimeOffset agora)
    {
        lock (_trava)
        {
            var r = Politica.Avaliar(t, Historico, agora);
            Historico.Registrar(t);
            TotalAvaliadas++;
            return r;
        }
    }

    /// <summary>Avalia sem registrar (consulta "e se").</summary>
    public Avaliacao Consultar(Transacao t, DateTimeOffset agora)
    {
        lock (_trava) return Politica.Avaliar(t, Historico, agora);
    }

    /// <summary>
    /// Reproduz um lote em ordem cronológica num histórico isolado, com a política informada
    /// (a atual ou uma candidata), e devolve as estatísticas. Não altera o estado do motor.
    /// </summary>
    public static ResultadoSimulacao Simular(Politica politica, IEnumerable<Transacao> lote, DateTimeOffset agora, bool incluirAvaliacoes = true)
    {
        var historico = new JanelaDeslizante();
        var avaliacoes = new List<Avaliacao>();
        var disparos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var erros = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int ap = 0, rev = 0, neg = 0;
        double duracao = 0;
        foreach (var t in lote.OrderBy(x => x.Momento))
        {
            var a = politica.Avaliar(t, historico, agora);
            historico.Registrar(t);
            switch (a.Decisao) { case Decisao.Aprovar: ap++; break; case Decisao.Revisar: rev++; break; default: neg++; break; }
            foreach (var d in a.Disparadas) disparos[d.Id] = disparos.GetValueOrDefault(d.Id) + 1;
            foreach (var e in a.Erros) erros[e.Id] = erros.GetValueOrDefault(e.Id) + 1;
            duracao += a.DuracaoMs;
            if (incluirAvaliacoes) avaliacoes.Add(a);
        }
        var n = ap + rev + neg;
        return new ResultadoSimulacao(n, ap, rev, neg, disparos, erros, n == 0 ? 0 : Math.Round(duracao / n, 3), avaliacoes);
    }
}
