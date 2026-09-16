using Antifraude.Core.Expressoes;

namespace Antifraude.Core.Regras;

public enum AcaoRegra
{
    /// <summary>Soma a pontuação ao risco da transação.</summary>
    Pontuar,
    /// <summary>Nega na hora, independentemente da pontuação (lista negra).</summary>
    Negar,
    /// <summary>Aprova na hora, salvo se alguma regra de negação também disparar (lista branca).</summary>
    Aprovar,
}

/// <summary>Regra de negócio: expressão compilada, pontuação e ação. Imutável; alterações geram nova versão.</summary>
public sealed class Regra
{
    public string Id { get; }
    public string Nome { get; }
    public string Descricao { get; }
    public string Expressao { get; }
    public int Pontuacao { get; }
    public AcaoRegra Acao { get; }
    public bool Ativa { get; }
    public int Versao { get; }
    public DateTimeOffset AtualizadaEm { get; }
    public No Arvore { get; }

    public Regra(string id, string nome, string expressao, int pontuacao, AcaoRegra acao, bool ativa = true, string? descricao = null, int versao = 1, DateTimeOffset? atualizadaEm = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 60 || !id.All(c => char.IsLetterOrDigit(c) || c is '_' or '-')) throw new ArgumentException("id da regra deve ser alfanumérico (com _ ou -) e ter até 60 caracteres", nameof(id));
        if (string.IsNullOrWhiteSpace(nome)) throw new ArgumentException("nome obrigatório", nameof(nome));
        if (acao == AcaoRegra.Pontuar && (pontuacao < 1 || pontuacao > 100)) throw new ArgumentException("pontuação deve estar entre 1 e 100", nameof(pontuacao));
        Id = id;
        Nome = nome.Trim();
        Descricao = descricao?.Trim() ?? string.Empty;
        Expressao = expressao;
        Arvore = Parser.Analisar(expressao); // valida a sintaxe já na criação
        Pontuacao = acao == AcaoRegra.Pontuar ? pontuacao : 0;
        Acao = acao;
        Ativa = ativa;
        Versao = versao;
        AtualizadaEm = atualizadaEm ?? DateTimeOffset.UtcNow;
    }

    public Regra ComAlteracoes(string? nome, string? expressao, int? pontuacao, AcaoRegra? acao, bool? ativa, string? descricao, DateTimeOffset agora) =>
        new(Id, nome ?? Nome, expressao ?? Expressao, pontuacao ?? Pontuacao, acao ?? Acao, ativa ?? Ativa, descricao ?? Descricao, Versao + 1, agora);
}
