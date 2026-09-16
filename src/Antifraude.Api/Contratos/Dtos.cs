using System.ComponentModel.DataAnnotations;
using Antifraude.Core.Dominio;
using Antifraude.Core.Regras;

namespace Antifraude.Api.Contratos;

public sealed record TransacaoRequest(
    [Required] string Id,
    DateTimeOffset? Momento,
    decimal Valor,
    string? Moeda,
    [Required] string Cartao,
    string? Cliente,
    string? Comerciante,
    string? Categoria,
    string? Pais,
    string? Ip,
    string? Dispositivo,
    bool CartaoPresente,
    Dictionary<string, string>? Extras)
{
    public Transacao ParaDominio(DateTimeOffset agora) => new(Id, Momento ?? agora, Valor, Moeda ?? "BRL", Cartao, Cliente ?? string.Empty, Comerciante ?? string.Empty,
        Categoria ?? string.Empty, (Pais ?? "BR").ToUpperInvariant(), Ip ?? string.Empty, Dispositivo ?? string.Empty, CartaoPresente, Extras);
}

public sealed record SimulacaoRequest(List<TransacaoRequest> Transacoes, PoliticaRequest? Politica, bool IncluirAvaliacoes = false);

public sealed record RegraRequest([Required] string Nome, [Required] string Expressao, int Pontuacao, AcaoRegra Acao = AcaoRegra.Pontuar, bool Ativa = true, string? Descricao = null);

public sealed record RegraPatchRequest(string? Nome, string? Expressao, int? Pontuacao, AcaoRegra? Acao, bool? Ativa, string? Descricao);

public sealed record PoliticaRequest(int LimiarRevisao, int LimiarNegacao, List<RegraComIdRequest> Regras);

public sealed record RegraComIdRequest([Required] string Id, [Required] string Nome, [Required] string Expressao, int Pontuacao, AcaoRegra Acao = AcaoRegra.Pontuar, bool Ativa = true, string? Descricao = null)
{
    public Regra ParaDominio() => new(Id, Nome, Expressao, Pontuacao, Acao, Ativa, Descricao);
}

public sealed record LimiaresRequest(int LimiarRevisao, int LimiarNegacao);

public sealed record ValidarRequest([Required] string Expressao, TransacaoRequest? Exemplo);

public sealed record RegraResponse(string Id, string Nome, string Descricao, string Expressao, int Pontuacao, AcaoRegra Acao, bool Ativa, int Versao, DateTimeOffset AtualizadaEm)
{
    public static RegraResponse De(Regra r) => new(r.Id, r.Nome, r.Descricao, r.Expressao, r.Pontuacao, r.Acao, r.Ativa, r.Versao, r.AtualizadaEm);
}

public sealed record PoliticaResponse(int LimiarRevisao, int LimiarNegacao, IReadOnlyList<RegraResponse> Regras)
{
    public static PoliticaResponse De(Politica p) => new(p.LimiarRevisao, p.LimiarNegacao, p.Regras.Select(RegraResponse.De).ToList());
}
