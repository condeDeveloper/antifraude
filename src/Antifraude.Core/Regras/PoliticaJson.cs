using System.Text.Json;
using System.Text.Json.Serialization;

namespace Antifraude.Core.Regras;

/// <summary>Carrega e salva a política em JSON (arquivo de regras versionado junto com o código ou editado pela API).</summary>
public static class PoliticaJson
{
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public sealed record RegraDto(string Id, string Nome, string Expressao, int Pontuacao, AcaoRegra Acao, bool Ativa = true, string? Descricao = null, int Versao = 1, DateTimeOffset? AtualizadaEm = null);

    public sealed record PoliticaDto(int LimiarRevisao, int LimiarNegacao, List<RegraDto> Regras);

    public static Politica Carregar(string json)
    {
        var dto = JsonSerializer.Deserialize<PoliticaDto>(json, Opcoes) ?? throw new InvalidOperationException("política vazia");
        var p = new Politica(dto.LimiarRevisao, dto.LimiarNegacao);
        foreach (var r in dto.Regras) p.Salvar(new Regra(r.Id, r.Nome, r.Expressao, r.Pontuacao, r.Acao, r.Ativa, r.Descricao, r.Versao, r.AtualizadaEm));
        return p;
    }

    public static string Salvar(Politica p) => JsonSerializer.Serialize(new PoliticaDto(p.LimiarRevisao, p.LimiarNegacao,
        p.Regras.Select(r => new RegraDto(r.Id, r.Nome, r.Expressao, r.Pontuacao, r.Acao, r.Ativa, r.Descricao, r.Versao, r.AtualizadaEm)).ToList()), Opcoes);

    public static Politica CarregarArquivo(string caminho) => Carregar(File.ReadAllText(caminho));

    public static void SalvarArquivo(Politica p, string caminho)
    {
        var pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
        File.WriteAllText(caminho, Salvar(p));
    }
}
