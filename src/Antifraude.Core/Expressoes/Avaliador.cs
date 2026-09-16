using System.Globalization;
using Antifraude.Core.Dominio;

namespace Antifraude.Core.Expressoes;

/// <summary>Provê as funções de velocidade (histórico) para o avaliador.</summary>
public interface IHistorico
{
    /// <summary>Transações anteriores com o mesmo valor de <paramref name="chave"/> que a atual, dentro da janela.</summary>
    int Contagem(Transacao atual, string chave, TimeSpan janela);
    decimal Soma(Transacao atual, string campo, string chave, TimeSpan janela);
    int Distintos(Transacao atual, string campo, string chave, TimeSpan janela);
    /// <summary>Segundos desde a transação anterior com a mesma chave, ou null se não houver.</summary>
    decimal? SegundosDesdeUltima(Transacao atual, string chave);
}

/// <summary>
/// Avalia a árvore contra uma transação. Tipos: decimal, string, bool, DateTimeOffset e listas.
/// Comparações entre tipos incompatíveis e campos inexistentes geram ErroDeExpressao com a posição.
/// </summary>
public sealed class Avaliador
{
    private readonly Transacao _t;
    private readonly IHistorico _historico;

    public Avaliador(Transacao transacao, IHistorico historico)
    {
        _t = transacao;
        _historico = historico;
    }

    public static readonly IReadOnlyList<string> Funcoes = new[]
    {
        "contagem(chave, janela)", "soma(campo, chave, janela)", "distintos(campo, chave, janela)", "segundos_desde_ultima(chave)",
        "minusculo(texto)", "maiusculo(texto)", "tamanho(texto ou lista)", "comeca_com(texto, prefixo)", "termina_com(texto, sufixo)",
        "abs(numero)", "entre(numero, minimo, maximo)", "arredondar(numero, casas)", "hora(momento)", "dia_semana(momento)", "vazio(valor)",
    };

    /// <summary>Avalia como condição. Null conta como falso.</summary>
    public bool Condicao(No no)
    {
        var v = Avaliar(no);
        return v switch { bool b => b, null => false, _ => throw new ErroDeExpressao($"a regra precisa resultar em verdadeiro ou falso, mas resultou em {Descrever(v)}", no.Posicao) };
    }

    public object? Avaliar(No no) => no switch
    {
        Literal l => l.Valor,
        Campo c => _t.Campo(c.Nome) is var v ? v : null,
        ListaLiteral l => l.Itens.Select(Avaliar).ToList(),
        Unario u => AvaliarUnario(u),
        Binario b => AvaliarBinario(b),
        Chamada ch => AvaliarChamada(ch),
        _ => throw new ErroDeExpressao("nó desconhecido", no.Posicao),
    };

    private object? AvaliarUnario(Unario u)
    {
        var v = Avaliar(u.Operando);
        return u.Operador switch
        {
            TipoToken.Nao => v is null ? true : !Bool(v, u.Posicao),
            TipoToken.Menos => -Numero(v, u.Posicao),
            _ => throw new ErroDeExpressao("operador unário inválido", u.Posicao),
        };
    }

    private object? AvaliarBinario(Binario b)
    {
        // curto-circuito lógico
        if (b.Operador == TipoToken.E) return CondicaoOuNull(b.Esquerda) && CondicaoOuNull(b.Direita);
        if (b.Operador == TipoToken.Ou) return CondicaoOuNull(b.Esquerda) || CondicaoOuNull(b.Direita);

        var e = Avaliar(b.Esquerda);
        var d = Avaliar(b.Direita);
        switch (b.Operador)
        {
            case TipoToken.Igual: return Iguais(e, d);
            case TipoToken.Diferente: return !Iguais(e, d);
            case TipoToken.Menor: return Comparar(e, d, b.Posicao) < 0;
            case TipoToken.MenorIgual: return Comparar(e, d, b.Posicao) <= 0;
            case TipoToken.Maior: return Comparar(e, d, b.Posicao) > 0;
            case TipoToken.MaiorIgual: return Comparar(e, d, b.Posicao) >= 0;
            case TipoToken.Em:
                if (d is not List<object?> lista) throw new ErroDeExpressao("o lado direito de EM precisa ser uma lista, ex.: [\"a\", \"b\"]", b.Posicao);
                return lista.Any(x => Iguais(e, x));
            case TipoToken.Contem:
                if (e is List<object?> l2) return l2.Any(x => Iguais(x, d));
                return Texto(e, b.Posicao).Contains(Texto(d, b.Posicao), StringComparison.OrdinalIgnoreCase);
            case TipoToken.Mais:
                if (e is string || d is string) return Texto(e, b.Posicao) + Texto(d, b.Posicao);
                return Numero(e, b.Posicao) + Numero(d, b.Posicao);
            case TipoToken.Menos: return Numero(e, b.Posicao) - Numero(d, b.Posicao);
            case TipoToken.Vezes: return Numero(e, b.Posicao) * Numero(d, b.Posicao);
            case TipoToken.Dividido:
            {
                var divisor = Numero(d, b.Posicao);
                if (divisor == 0) throw new ErroDeExpressao("divisão por zero", b.Posicao);
                return Numero(e, b.Posicao) / divisor;
            }
            case TipoToken.Resto:
            {
                var divisor = Numero(d, b.Posicao);
                if (divisor == 0) throw new ErroDeExpressao("divisão por zero", b.Posicao);
                return Numero(e, b.Posicao) % divisor;
            }
            default: throw new ErroDeExpressao("operador inválido", b.Posicao);
        }
    }

    private bool CondicaoOuNull(No no)
    {
        var v = Avaliar(no);
        return v is bool b ? b : v is null ? false : throw new ErroDeExpressao($"E/OU exigem verdadeiro ou falso, encontrei {Descrever(v)}", no.Posicao);
    }

    private object? AvaliarChamada(Chamada ch)
    {
        var a = ch.Argumentos;
        object? Arg(int i) => i < a.Count ? Avaliar(a[i]) : throw new ErroDeExpressao($"função {ch.Funcao} exige mais argumentos", ch.Posicao);
        void Exatos(int n) { if (a.Count != n) throw new ErroDeExpressao($"função {ch.Funcao} exige {n} argumento(s), recebeu {a.Count}", ch.Posicao); }

        switch (ch.Funcao)
        {
            case "contagem": Exatos(2); return (decimal)_historico.Contagem(_t, Chave(Arg(0), ch), Janela(Arg(1), ch));
            case "soma": Exatos(3); return _historico.Soma(_t, Chave(Arg(0), ch), Chave(Arg(1), ch), Janela(Arg(2), ch));
            case "distintos": Exatos(3); return (decimal)_historico.Distintos(_t, Chave(Arg(0), ch), Chave(Arg(1), ch), Janela(Arg(2), ch));
            case "segundos_desde_ultima": Exatos(1); return _historico.SegundosDesdeUltima(_t, Chave(Arg(0), ch));
            case "minusculo": Exatos(1); return Texto(Arg(0), ch.Posicao).ToLowerInvariant();
            case "maiusculo": Exatos(1); return Texto(Arg(0), ch.Posicao).ToUpperInvariant();
            case "tamanho": Exatos(1); return Arg(0) is List<object?> l ? (decimal)l.Count : (decimal)Texto(Arg(0), ch.Posicao).Length;
            case "comeca_com": Exatos(2); return Texto(Arg(0), ch.Posicao).StartsWith(Texto(Arg(1), ch.Posicao), StringComparison.OrdinalIgnoreCase);
            case "termina_com": Exatos(2); return Texto(Arg(0), ch.Posicao).EndsWith(Texto(Arg(1), ch.Posicao), StringComparison.OrdinalIgnoreCase);
            case "abs": Exatos(1); return Math.Abs(Numero(Arg(0), ch.Posicao));
            case "entre": { Exatos(3); var x = Numero(Arg(0), ch.Posicao); return x >= Numero(Arg(1), ch.Posicao) && x <= Numero(Arg(2), ch.Posicao); }
            case "arredondar": Exatos(2); return Math.Round(Numero(Arg(0), ch.Posicao), (int)Numero(Arg(1), ch.Posicao), MidpointRounding.AwayFromZero);
            case "hora": Exatos(1); return (decimal)Momento(Arg(0), ch.Posicao).Hour;
            case "dia_semana": Exatos(1); return (decimal)(int)Momento(Arg(0), ch.Posicao).DayOfWeek;
            case "vazio": Exatos(1); { var v = Arg(0); return v is null || (v is string s && s.Length == 0) || (v is List<object?> li && li.Count == 0); }
            default: throw new ErroDeExpressao($"função desconhecida: {ch.Funcao}", ch.Posicao);
        }
    }

    private static string Chave(object? v, Chamada ch) => v is string s && s.Length > 0 ? s : throw new ErroDeExpressao($"função {ch.Funcao}: nome de campo deve ser um texto, ex.: \"cartao\"", ch.Posicao);

    /// <summary>Janelas como "30s", "10m", "2h", "7d".</summary>
    public static TimeSpan Janela(object? v, Chamada ch)
    {
        var s = v as string ?? throw new ErroDeExpressao($"função {ch.Funcao}: janela deve ser um texto como \"10m\"", ch.Posicao);
        if (s.Length < 2 || !decimal.TryParse(s[..^1], NumberStyles.Number, CultureInfo.InvariantCulture, out var n) || n <= 0) throw new ErroDeExpressao($"janela inválida '{s}' (use 30s, 10m, 2h ou 7d)", ch.Posicao);
        var ts = char.ToLowerInvariant(s[^1]) switch
        {
            's' => TimeSpan.FromSeconds((double)n),
            'm' => TimeSpan.FromMinutes((double)n),
            'h' => TimeSpan.FromHours((double)n),
            'd' => TimeSpan.FromDays((double)n),
            _ => throw new ErroDeExpressao($"janela inválida '{s}' (use 30s, 10m, 2h ou 7d)", ch.Posicao),
        };
        if (ts > TimeSpan.FromDays(30)) throw new ErroDeExpressao("janela máxima é 30d", ch.Posicao);
        return ts;
    }

    private static bool Iguais(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is decimal da && b is decimal db) return da == db;
        if (a is string sa && b is string sb) return string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase);
        if (a is bool ba && b is bool bb) return ba == bb;
        if (a is DateTimeOffset ta && b is DateTimeOffset tb) return ta == tb;
        return false;
    }

    private static int Comparar(object? a, object? b, int pos)
    {
        if (a is decimal da && b is decimal db) return da.CompareTo(db);
        if (a is string sa && b is string sb) return string.Compare(sa, sb, StringComparison.OrdinalIgnoreCase);
        if (a is DateTimeOffset ta && b is DateTimeOffset tb) return ta.CompareTo(tb);
        if (a is null || b is null) throw new ErroDeExpressao("comparação com valor ausente (null)", pos);
        throw new ErroDeExpressao($"não dá para comparar {Descrever(a)} com {Descrever(b)}", pos);
    }

    private static decimal Numero(object? v, int pos) => v is decimal d ? d : throw new ErroDeExpressao($"esperava um número, encontrei {Descrever(v)}", pos);
    private static string Texto(object? v, int pos) => v switch { string s => s, decimal d => d.ToString(CultureInfo.InvariantCulture), bool b => b ? "verdadeiro" : "falso", null => throw new ErroDeExpressao("esperava um texto, encontrei valor ausente", pos), _ => throw new ErroDeExpressao($"esperava um texto, encontrei {Descrever(v)}", pos) };
    private static bool Bool(object? v, int pos) => v is bool b ? b : throw new ErroDeExpressao($"esperava verdadeiro ou falso, encontrei {Descrever(v)}", pos);
    private static DateTimeOffset Momento(object? v, int pos) => v is DateTimeOffset t ? t : throw new ErroDeExpressao($"esperava um instante (use o campo momento), encontrei {Descrever(v)}", pos);

    private static string Descrever(object? v) => v switch
    {
        null => "valor ausente",
        decimal d => $"o número {d.ToString(CultureInfo.InvariantCulture)}",
        string s => $"o texto \"{s}\"",
        bool b => b ? "verdadeiro" : "falso",
        DateTimeOffset => "um instante",
        List<object?> => "uma lista",
        _ => v.GetType().Name,
    };
}
