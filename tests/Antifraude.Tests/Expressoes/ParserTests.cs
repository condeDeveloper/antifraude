using Antifraude.Core.Expressoes;

namespace Antifraude.Tests.Expressoes;

public class ParserTests
{
    [Fact]
    public void TokenizaNumerosTextosEPalavrasChaveEmDoisIdiomas()
    {
        var t = Lexer.Tokenizar("valor >= 12.5 E pais != 'BR' OU NÃO cartao_presente AND x IN [\"a\"] contains true");
        t.Select(x => x.Tipo).Should().ContainInOrder(TipoToken.Identificador, TipoToken.MaiorIgual, TipoToken.Numero, TipoToken.E, TipoToken.Identificador,
            TipoToken.Diferente, TipoToken.Texto, TipoToken.Ou, TipoToken.Nao, TipoToken.Identificador, TipoToken.E, TipoToken.Identificador, TipoToken.Em,
            TipoToken.AbreColchete, TipoToken.Texto, TipoToken.FechaColchete, TipoToken.Contem, TipoToken.Verdadeiro, TipoToken.Fim);
        t.First(x => x.Tipo == TipoToken.Texto).Texto.Should().Be("BR");
    }

    [Fact]
    public void RespeitaPrecedencia()
    {
        // OU tem precedência menor que E: a OU (b E c)
        var no = Parser.Analisar("a OU b E c").Should().BeOfType<Binario>().Subject;
        no.Operador.Should().Be(TipoToken.Ou);
        no.Direita.Should().BeOfType<Binario>().Which.Operador.Should().Be(TipoToken.E);

        // aritmética antes de comparação: (1 + 2 * 3) > 6
        var cmp = Parser.Analisar("1 + 2 * 3 > 6").Should().BeOfType<Binario>().Subject;
        cmp.Operador.Should().Be(TipoToken.Maior);
        var soma = cmp.Esquerda.Should().BeOfType<Binario>().Subject;
        soma.Operador.Should().Be(TipoToken.Mais);
        soma.Direita.Should().BeOfType<Binario>().Which.Operador.Should().Be(TipoToken.Vezes);

        // NAO liga mais forte que E
        var nao = Parser.Analisar("NAO a E b").Should().BeOfType<Binario>().Subject;
        nao.Operador.Should().Be(TipoToken.E);
        nao.Esquerda.Should().BeOfType<Unario>();
    }

    [Fact]
    public void ParentesesListasEChamadas()
    {
        var no = Parser.Analisar("(a OU b) E contagem(\"cartao\", \"10m\") >= 5 E pais EM [\"BR\", \"AR\"]");
        no.Should().BeOfType<Binario>();
        var texto = no.ToString();
        texto.Should().Contain("Chamada").And.Contain("ListaLiteral");
        Parser.Analisar("[]").Should().BeOfType<ListaLiteral>().Which.Itens.Should().BeEmpty();
        Parser.Analisar("f()").Should().BeOfType<Chamada>().Which.Argumentos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "expressão vazia")]
    [InlineData("valor >", "esperava um valor")]
    [InlineData("(valor > 1", "esperava ')'")]
    [InlineData("valor > 1 )", "símbolo inesperado ')'")]
    [InlineData("\"aberto", "sem fechamento")]
    [InlineData("valor # 1", "caractere inesperado '#'")]
    [InlineData("1.2.3 > 1", "número inválido")]
    public void ErrosDeSintaxeTemMensagemEPosicao(string expressao, string trecho)
    {
        var act = () => Parser.Analisar(expressao);
        act.Should().Throw<ErroDeExpressao>().WithMessage($"*{trecho}*posição*");
    }

    [Fact]
    public void PosicaoDoErroApontaParaOToken()
    {
        var ex = Assert.Throws<ErroDeExpressao>(() => Parser.Analisar("valor > 1 E E"));
        ex.Posicao.Should().Be(12);
        ex.Message.Should().Contain("posição 13");
    }
}
