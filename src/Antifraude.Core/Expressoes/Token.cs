namespace Antifraude.Core.Expressoes;

public enum TipoToken
{
    Numero,
    Texto,
    Identificador,
    Verdadeiro,
    Falso,
    E,
    Ou,
    Nao,
    Em,
    Contem,
    Igual,
    Diferente,
    Menor,
    MenorIgual,
    Maior,
    MaiorIgual,
    Mais,
    Menos,
    Vezes,
    Dividido,
    Resto,
    AbreParentese,
    FechaParentese,
    AbreColchete,
    FechaColchete,
    Virgula,
    Fim,
}

/// <summary>Unidade léxica com a posição no texto original, usada nas mensagens de erro.</summary>
public sealed record Token(TipoToken Tipo, string Texto, int Posicao)
{
    public override string ToString() => Tipo == TipoToken.Fim ? "fim da expressão" : $"'{Texto}'";
}

/// <summary>Erro de sintaxe ou de tipo com a posição (base 1) onde ocorreu.</summary>
public sealed class ErroDeExpressao : Exception
{
    public int Posicao { get; }

    public ErroDeExpressao(string mensagem, int posicao) : base($"{mensagem} (posição {posicao + 1})") => Posicao = posicao;
}
