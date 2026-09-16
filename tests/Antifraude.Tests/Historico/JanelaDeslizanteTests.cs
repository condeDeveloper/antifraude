using Antifraude.Core.Expressoes;
using Antifraude.Core.Historico;

namespace Antifraude.Tests.Historico;

public class JanelaDeslizanteTests
{
    private static readonly DateTimeOffset T0 = Fabrica.T0;

    [Fact]
    public void ContaSomaEDistintosSoDentroDaJanelaEAntesDaAtual()
    {
        var h = new JanelaDeslizante();
        h.Registrar(Fabrica.Tx("a", 100, momento: T0.AddMinutes(-30), pais: "BR"));
        h.Registrar(Fabrica.Tx("b", 200, momento: T0.AddMinutes(-9), pais: "US"));
        h.Registrar(Fabrica.Tx("c", 300, momento: T0.AddMinutes(-2), pais: "AR"));
        h.Registrar(Fabrica.Tx("outro", 999, cartao: "c2", momento: T0.AddMinutes(-1)));
        var atual = Fabrica.Tx("d", 50, momento: T0, pais: "BR");

        h.Contagem(atual, "cartao", TimeSpan.FromMinutes(10)).Should().Be(2);
        h.Contagem(atual, "cartao", TimeSpan.FromHours(1)).Should().Be(3);
        h.Soma(atual, "valor", "cartao", TimeSpan.FromMinutes(10)).Should().Be(500);
        h.Distintos(atual, "pais", "cartao", TimeSpan.FromHours(1)).Should().Be(3);
        h.Distintos(atual, "pais", "cartao", TimeSpan.FromMinutes(10)).Should().Be(2);
        h.SegundosDesdeUltima(atual, "cartao").Should().Be(120);
        h.SegundosDesdeUltima(Fabrica.Tx("z", cartao: "novo"), "cartao").Should().BeNull();
    }

    [Fact]
    public void NaoContaAPropriaTransacaoNemFuturas()
    {
        var h = new JanelaDeslizante();
        var atual = Fabrica.Tx("x", momento: T0);
        h.Registrar(atual);
        h.Registrar(Fabrica.Tx("futura", momento: T0.AddMinutes(5)));
        h.Contagem(atual, "cartao", TimeSpan.FromHours(1)).Should().Be(0);
    }

    [Fact]
    public void PodaForaDaJanelaMaximaERespeitaLimite()
    {
        var h = new JanelaDeslizante(TimeSpan.FromHours(1), limitePorChave: 3);
        for (var i = 0; i < 10; i++) h.Registrar(Fabrica.Tx("t" + i, momento: T0.AddMinutes(-90 + i * 10)));
        h.Total.Should().BeLessThanOrEqualTo(3 * 5); // 3 por chave indexada (cartao, cliente, ip, dispositivo, comerciante)
        var atual = Fabrica.Tx("agora", momento: T0);
        h.Contagem(atual, "cartao", TimeSpan.FromHours(2)).Should().Be(3);
    }

    [Fact]
    public void ChaveNaoIndexadaGeraErroClaro()
    {
        var h = new JanelaDeslizante();
        var act = () => h.Contagem(Fabrica.Tx(), "pais", TimeSpan.FromMinutes(1));
        act.Should().Throw<ErroDeExpressao>().WithMessage("*não é indexado*");
    }

    [Fact]
    public void FuncoesDeVelocidadeNaExpressao()
    {
        var h = new JanelaDeslizante();
        for (var i = 1; i <= 5; i++) h.Registrar(Fabrica.Tx("p" + i, 1000, momento: T0.AddMinutes(-i)));
        var atual = Fabrica.Tx("atual", 6000, momento: T0);
        var av = new Avaliador(atual, h);
        av.Condicao(Parser.Analisar("contagem(\"cartao\", \"10m\") >= 5")).Should().BeTrue();
        av.Condicao(Parser.Analisar("soma(\"valor\", \"cartao\", \"1h\") + valor > 10000")).Should().BeTrue();
        av.Condicao(Parser.Analisar("segundos_desde_ultima(\"cartao\") < 90")).Should().BeTrue();
    }
}
