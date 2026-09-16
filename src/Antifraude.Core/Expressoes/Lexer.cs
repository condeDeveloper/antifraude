using System.Globalization;
using System.Text;

namespace Antifraude.Core.Expressoes;

/// <summary>
/// Transforma o texto da regra em tokens. Palavras-chave aceitam português e inglês
/// (E/AND, OU/OR, NAO/NÃO/NOT, EM/IN, CONTEM/CONTÉM/CONTAINS, VERDADEIRO/TRUE, FALSO/FALSE), sem distinguir maiúsculas.
/// </summary>
public static class Lexer
{
    private static readonly Dictionary<string, TipoToken> PalavrasChave = new(StringComparer.OrdinalIgnoreCase)
    {
        ["E"] = TipoToken.E, ["AND"] = TipoToken.E,
        ["OU"] = TipoToken.Ou, ["OR"] = TipoToken.Ou,
        ["NAO"] = TipoToken.Nao, ["NÃO"] = TipoToken.Nao, ["NOT"] = TipoToken.Nao,
        ["EM"] = TipoToken.Em, ["IN"] = TipoToken.Em,
        ["CONTEM"] = TipoToken.Contem, ["CONTÉM"] = TipoToken.Contem, ["CONTAINS"] = TipoToken.Contem,
        ["VERDADEIRO"] = TipoToken.Verdadeiro, ["TRUE"] = TipoToken.Verdadeiro,
        ["FALSO"] = TipoToken.Falso, ["FALSE"] = TipoToken.Falso,
    };

    public static List<Token> Tokenizar(string texto)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < texto.Length)
        {
            var c = texto[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsDigit(c) || (c == '.' && i + 1 < texto.Length && char.IsDigit(texto[i + 1])))
            {
                var inicio = i;
                while (i < texto.Length && (char.IsDigit(texto[i]) || texto[i] == '.')) i++;
                var num = texto[inicio..i];
                if (!decimal.TryParse(num, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _)) throw new ErroDeExpressao($"número inválido '{num}'", inicio);
                tokens.Add(new Token(TipoToken.Numero, num, inicio));
                continue;
            }

            if (c is '"' or '\'')
            {
                var inicio = i;
                var sb = new StringBuilder();
                i++;
                while (i < texto.Length && texto[i] != c)
                {
                    if (texto[i] == '\\' && i + 1 < texto.Length) { sb.Append(texto[i + 1]); i += 2; continue; }
                    sb.Append(texto[i]); i++;
                }
                if (i >= texto.Length) throw new ErroDeExpressao("texto sem fechamento de aspas", inicio);
                i++;
                tokens.Add(new Token(TipoToken.Texto, sb.ToString(), inicio));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                var inicio = i;
                while (i < texto.Length && (char.IsLetterOrDigit(texto[i]) || texto[i] is '_' or '.')) i++;
                var palavra = texto[inicio..i];
                tokens.Add(PalavrasChave.TryGetValue(palavra, out var tipo) ? new Token(tipo, palavra, inicio) : new Token(TipoToken.Identificador, palavra.ToLowerInvariant(), inicio));
                continue;
            }

            Token Simbolo(TipoToken t, int tam) { var tk = new Token(t, texto.Substring(i, tam), i); i += tam; return tk; }
            string dois = i + 1 < texto.Length ? texto.Substring(i, 2) : c.ToString();
            tokens.Add(dois switch
            {
                "==" => Simbolo(TipoToken.Igual, 2),
                "!=" or "<>" => Simbolo(TipoToken.Diferente, 2),
                "<=" => Simbolo(TipoToken.MenorIgual, 2),
                ">=" => Simbolo(TipoToken.MaiorIgual, 2),
                _ => c switch
                {
                    '=' => Simbolo(TipoToken.Igual, 1),
                    '<' => Simbolo(TipoToken.Menor, 1),
                    '>' => Simbolo(TipoToken.Maior, 1),
                    '+' => Simbolo(TipoToken.Mais, 1),
                    '-' => Simbolo(TipoToken.Menos, 1),
                    '*' => Simbolo(TipoToken.Vezes, 1),
                    '/' => Simbolo(TipoToken.Dividido, 1),
                    '%' => Simbolo(TipoToken.Resto, 1),
                    '(' => Simbolo(TipoToken.AbreParentese, 1),
                    ')' => Simbolo(TipoToken.FechaParentese, 1),
                    '[' => Simbolo(TipoToken.AbreColchete, 1),
                    ']' => Simbolo(TipoToken.FechaColchete, 1),
                    ',' => Simbolo(TipoToken.Virgula, 1),
                    _ => throw new ErroDeExpressao($"caractere inesperado '{c}'", i),
                },
            });
        }
        tokens.Add(new Token(TipoToken.Fim, string.Empty, texto.Length));
        return tokens;
    }
}
