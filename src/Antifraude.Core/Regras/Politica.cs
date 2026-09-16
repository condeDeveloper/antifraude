using System.Diagnostics;
using Antifraude.Core.Dominio;
using Antifraude.Core.Expressoes;

namespace Antifraude.Core.Regras;

public enum Decisao
{
    Aprovar,
    Revisar,
    Negar,
}

public sealed record RegraDisparada(string Id, string Nome, AcaoRegra Acao, int Pontuacao);

public sealed record RegraComErro(string Id, string Nome, string Erro);

/// <summary>Resultado explicável: decisão, pontuação, regras que dispararam e regras que falharam ao avaliar.</summary>
public sealed record Avaliacao(
    string TransacaoId,
    Decisao Decisao,
    int Pontuacao,
    string Motivo,
    IReadOnlyList<RegraDisparada> Disparadas,
    IReadOnlyList<RegraComErro> Erros,
    int RegrasAvaliadas,
    double DuracaoMs,
    DateTimeOffset AvaliadaEm);

/// <summary>Conjunto de regras e limiares. Pontuação ≥ limiar de negação nega; ≥ limiar de revisão manda para revisão manual.</summary>
public sealed class Politica
{
    private readonly Dictionary<string, Regra> _regras = new(StringComparer.OrdinalIgnoreCase);

    public Politica(int limiarRevisao = 40, int limiarNegacao = 70)
    {
        DefinirLimiares(limiarRevisao, limiarNegacao);
    }

    public int LimiarRevisao { get; private set; }
    public int LimiarNegacao { get; private set; }
    public IReadOnlyCollection<Regra> Regras => _regras.Values.OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToArray();

    public void DefinirLimiares(int revisao, int negacao)
    {
        if (revisao < 1 || negacao <= revisao || negacao > 1000) throw new ArgumentException("limiares inválidos: 1 ≤ revisão < negação ≤ 1000");
        LimiarRevisao = revisao;
        LimiarNegacao = negacao;
    }

    public Regra? Obter(string id) => _regras.GetValueOrDefault(id);
    public void Salvar(Regra regra) => _regras[regra.Id] = regra;
    public bool Remover(string id) => _regras.Remove(id);

    /// <summary>Avalia todas as regras ativas. Erros de avaliação em uma regra não derrubam as outras: são reportados.</summary>
    public Avaliacao Avaliar(Transacao t, IHistorico historico, DateTimeOffset agora)
    {
        var relogio = Stopwatch.StartNew();
        var av = new Avaliador(t, historico);
        var disparadas = new List<RegraDisparada>();
        var erros = new List<RegraComErro>();
        var pontos = 0;
        var negar = false;
        var aprovar = false;

        var ativas = _regras.Values.Where(r => r.Ativa).OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var r in ativas)
        {
            bool disparou;
            try { disparou = av.Condicao(r.Arvore); }
            catch (Exception e) when (e is ErroDeExpressao or CampoDesconhecidoException)
            {
                erros.Add(new RegraComErro(r.Id, r.Nome, e.Message));
                continue;
            }
            if (!disparou) continue;
            disparadas.Add(new RegraDisparada(r.Id, r.Nome, r.Acao, r.Pontuacao));
            switch (r.Acao)
            {
                case AcaoRegra.Negar: negar = true; break;
                case AcaoRegra.Aprovar: aprovar = true; break;
                default: pontos += r.Pontuacao; break;
            }
        }

        Decisao decisao;
        string motivo;
        if (negar) { decisao = Decisao.Negar; motivo = "regra de negação direta: " + string.Join(", ", disparadas.Where(d => d.Acao == AcaoRegra.Negar).Select(d => d.Id)); }
        else if (aprovar) { decisao = Decisao.Aprovar; motivo = "regra de aprovação direta: " + string.Join(", ", disparadas.Where(d => d.Acao == AcaoRegra.Aprovar).Select(d => d.Id)); }
        else if (pontos >= LimiarNegacao) { decisao = Decisao.Negar; motivo = $"pontuação {pontos} ≥ {LimiarNegacao}"; }
        else if (pontos >= LimiarRevisao) { decisao = Decisao.Revisar; motivo = $"pontuação {pontos} ≥ {LimiarRevisao}"; }
        else { decisao = Decisao.Aprovar; motivo = pontos == 0 ? "nenhuma regra disparou" : $"pontuação {pontos} < {LimiarRevisao}"; }

        return new Avaliacao(t.Id, decisao, pontos, motivo, disparadas, erros, ativas.Count, Math.Round(relogio.Elapsed.TotalMilliseconds, 3), agora);
    }
}
