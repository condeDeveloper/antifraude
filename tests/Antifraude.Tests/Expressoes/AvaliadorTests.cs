using Antifraude.Core.Dominio;
using Antifraude.Core.Expressoes;
using Antifraude.Core.Historico;

namespace Antifraude.Tests.Expressoes;

public class AvaliadorTests
{
    private static bool Cond(string expr, Transacao? t = null, IHistorico? h = null) =>
        new Avaliador(t ?? Fabrica.Tx(), h ?? new JanelaDeslizante()).Condicao(Parser.Analisar(expr));

    private static object? Val(string expr, Transacao? t = null) => new Avaliador(t ?? Fabrica.Tx(), new JanelaDeslizante()).Avaliar(Parser.Analisar(expr));

    [Theory]
    [InlineData("valor > 50", true)]
    [InlineData("valor > 100", false)]
    [InlineData("valor >= 100 E pais == \"BR\"", true)]
    [InlineData("pais == \"br\"", true)] // comparação de texto ignora maiúsculas
    [InlineData("pais != \"BR\" OU cartao_presente", true)]
    [InlineData("NAO cartao_presente", false)]
    [InlineData("valor * 2 - 50 == 150", true)]
    [InlineData("valor / 3 > 33.33", true)]
    [InlineData("valor % 7 == 2", true)]
    [InlineData("-valor < 0", true)]
    [InlineData("pais EM [\"AR\", \"BR\"]", true)]
    [InlineData("pais EM [\"AR\", \"US\"]", false)]
    [InlineData("comerciante CONTEM \"merc\"", true)]
    [InlineData("[\"a\", \"b\"] CONTEM \"B\"", true)]
    [InlineData("entre(valor, 50, 100)", true)]
    [InlineData("hora == 14 E dia_semana == 3", true)] // 16/09/2026 é quarta
    [InlineData("hora(momento) == 14", true)]
    [InlineData("minusculo(comerciante) == \"mercado\" E tamanho(comerciante) == 7", true)]
    [InlineData("comeca_com(comerciante, \"MER\") E termina_com(comerciante, \"ado\")", true)]
    [InlineData("abs(-3) == 3 E arredondar(2.555, 2) == 2.56", true)]
    [InlineData("vazio(extras.inexistente) E NAO vazio(comerciante)", true)]
    [InlineData("extras.inexistente == 1", false)] // campo extra ausente é null, nunca igual
    [InlineData("(valor > 1000 OU pais == \"BR\") E NAO (valor < 10)", true)]
    public void AvaliaExpressoesBasicas(string expr, bool esperado) => Cond(expr).Should().Be(esperado);

    [Fact]
    public void ExtrasNumericosViramNumeros()
    {
        var t = Fabrica.Tx(extras: new() { ["parcelas"] = "12", ["segmento"] = "vip" });
        Cond("extras.parcelas > 6", t).Should().BeTrue();
        Cond("extras.segmento == \"VIP\"", t).Should().BeTrue();
        Val("extras.parcelas * 2", t).Should().Be(24m);
    }

    [Fact]
    public void ConcatenaTextos()
    {
        Val("pais + \"-\" + moeda").Should().Be("BR-BRL");
        Val("\"valor: \" + valor").Should().Be("valor: 100");
    }

    [Theory]
    [InlineData("valor > \"a\"", "não dá para comparar")]
    [InlineData("valor E pais", "E/OU exigem verdadeiro ou falso")]
    [InlineData("valor + 1", "precisa resultar em verdadeiro ou falso")]
    [InlineData("valor / 0 > 1", "divisão por zero")]
    [InlineData("pais EM \"BR\"", "precisa ser uma lista")]
    [InlineData("funcao_inexistente(1)", "função desconhecida")]
    [InlineData("contagem(\"cartao\")", "exige 2 argumento")]
    [InlineData("contagem(\"cartao\", \"10x\")", "janela inválida")]
    [InlineData("contagem(\"cartao\", \"40d\")", "janela máxima")]
    [InlineData("campo_que_nao_existe > 1", "campo desconhecido")]
    [InlineData("hora(valor) > 1", "esperava um instante")]
    public void ErrosDeTipoSaoClarosENaoDerrubamOProcesso(string expr, string trecho)
    {
        var act = () => Cond(expr);
        act.Should().Throw<Exception>().Where(e => e is ErroDeExpressao || e is CampoDesconhecidoException).WithMessage($"*{trecho}*");
    }

    [Fact]
    public void CurtoCircuitoEvitaAvaliarOLadoDireito()
    {
        // o lado direito dividiria por zero, mas o esquerdo já decide
        Cond("valor > 1000 E valor / 0 > 1").Should().BeFalse();
        Cond("valor > 1 OU valor / 0 > 1").Should().BeTrue();
    }
}
