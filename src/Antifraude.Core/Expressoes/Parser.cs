using System.Globalization;

namespace Antifraude.Core.Expressoes;

/// <summary>
/// Analisador descendente recursivo com precedência: OU &lt; E &lt; NAO &lt; comparação &lt; soma &lt; produto &lt; unário &lt; primário.
/// <code>valor > 5000 E pais != "BR" OU contagem("cartao", "10m") >= 5</code>
/// </summary>
public sealed class Parser
{
    private readonly List<Token> _tokens;
    private int _pos;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static No Analisar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) throw new ErroDeExpressao("expressão vazia", 0);
        var p = new Parser(Lexer.Tokenizar(texto));
        var no = p.Ou();
        if (p.Atual.Tipo != TipoToken.Fim) throw new ErroDeExpressao($"símbolo inesperado {p.Atual}", p.Atual.Posicao);
        return no;
    }

    private Token Atual => _tokens[_pos];
    private Token Avancar() => _tokens[_pos++];
    private bool Aceitar(TipoToken t) { if (Atual.Tipo != t) return false; _pos++; return true; }
    private Token Exigir(TipoToken t, string oQue) => Atual.Tipo == t ? Avancar() : throw new ErroDeExpressao($"esperava {oQue}, encontrei {Atual}", Atual.Posicao);

    private No Ou()
    {
        var e = E();
        while (Atual.Tipo == TipoToken.Ou) { var op = Avancar(); e = new Binario(TipoToken.Ou, e, E(), op.Posicao); }
        return e;
    }

    private No E()
    {
        var e = Nao();
        while (Atual.Tipo == TipoToken.E) { var op = Avancar(); e = new Binario(TipoToken.E, e, Nao(), op.Posicao); }
        return e;
    }

    private No Nao()
    {
        if (Atual.Tipo == TipoToken.Nao) { var op = Avancar(); return new Unario(TipoToken.Nao, Nao(), op.Posicao); }
        return Comparacao();
    }

    private No Comparacao()
    {
        var e = Soma();
        var t = Atual.Tipo;
        if (t is TipoToken.Igual or TipoToken.Diferente or TipoToken.Menor or TipoToken.MenorIgual or TipoToken.Maior or TipoToken.MaiorIgual or TipoToken.Em or TipoToken.Contem)
        {
            var op = Avancar();
            var d = Soma();
            e = new Binario(t, e, d, op.Posicao);
        }
        return e;
    }

    private No Soma()
    {
        var e = Produto();
        while (Atual.Tipo is TipoToken.Mais or TipoToken.Menos) { var op = Avancar(); e = new Binario(op.Tipo, e, Produto(), op.Posicao); }
        return e;
    }

    private No Produto()
    {
        var e = Unario();
        while (Atual.Tipo is TipoToken.Vezes or TipoToken.Dividido or TipoToken.Resto) { var op = Avancar(); e = new Binario(op.Tipo, e, Unario(), op.Posicao); }
        return e;
    }

    private No Unario()
    {
        if (Atual.Tipo == TipoToken.Menos) { var op = Avancar(); return new Unario(TipoToken.Menos, Unario(), op.Posicao); }
        return Primario();
    }

    private No Primario()
    {
        var t = Avancar();
        switch (t.Tipo)
        {
            case TipoToken.Numero: return new Literal(decimal.Parse(t.Texto, CultureInfo.InvariantCulture), t.Posicao);
            case TipoToken.Texto: return new Literal(t.Texto, t.Posicao);
            case TipoToken.Verdadeiro: return new Literal(true, t.Posicao);
            case TipoToken.Falso: return new Literal(false, t.Posicao);
            case TipoToken.AbreParentese:
            {
                var e = Ou();
                Exigir(TipoToken.FechaParentese, "')'");
                return e;
            }
            case TipoToken.AbreColchete:
            {
                var itens = new List<No>();
                if (!Aceitar(TipoToken.FechaColchete))
                {
                    do itens.Add(Ou()); while (Aceitar(TipoToken.Virgula));
                    Exigir(TipoToken.FechaColchete, "']'");
                }
                return new ListaLiteral(itens, t.Posicao);
            }
            case TipoToken.Identificador:
            {
                if (Aceitar(TipoToken.AbreParentese))
                {
                    var args = new List<No>();
                    if (!Aceitar(TipoToken.FechaParentese))
                    {
                        do args.Add(Ou()); while (Aceitar(TipoToken.Virgula));
                        Exigir(TipoToken.FechaParentese, "')'");
                    }
                    return new Chamada(t.Texto, args, t.Posicao);
                }
                return new Campo(t.Texto, t.Posicao);
            }
            default:
                throw new ErroDeExpressao($"esperava um valor, campo ou função, encontrei {t}", t.Posicao);
        }
    }
}
