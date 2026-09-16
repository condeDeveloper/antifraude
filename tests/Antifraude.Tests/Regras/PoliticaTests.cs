using Antifraude.Core.Expressoes;
using Antifraude.Core.Motor;
using Antifraude.Core.Regras;

namespace Antifraude.Tests.Regras;

public class PoliticaTests
{
    private static readonly DateTimeOffset T0 = Fabrica.T0;

    private static Politica PoliticaPadrao()
    {
        var p = new Politica(40, 70);
        p.Salvar(new Regra("valor-alto", "Valor alto", "valor > 5000", 30, AcaoRegra.Pontuar));
        p.Salvar(new Regra("exterior", "Exterior sem cartão", "pais != \"BR\" E NAO cartao_presente", 25, AcaoRegra.Pontuar));
        p.Salvar(new Regra("rajada", "Rajada", "contagem(\"cartao\", \"10m\") >= 3", 40, AcaoRegra.Pontuar));
        p.Salvar(new Regra("bloqueado", "Comerciante bloqueado", "maiusculo(comerciante) EM [\"CASSINO-X\"]", 0, AcaoRegra.Negar));
        p.Salvar(new Regra("vip", "VIP", "extras.segmento == \"vip\" E valor <= 20000", 0, AcaoRegra.Aprovar));
        p.Salvar(new Regra("desativada", "Desativada", "valor > 0", 100, AcaoRegra.Pontuar, ativa: false));
        return p;
    }

    [Fact]
    public void RegraCompilaNaCriacaoEValidaCampos()
    {
        var act = () => new Regra("x", "X", "valor >", 10, AcaoRegra.Pontuar);
        act.Should().Throw<ErroDeExpressao>();
        var id = () => new Regra("com espaço", "X", "valor > 1", 10, AcaoRegra.Pontuar);
        id.Should().Throw<ArgumentException>();
        var pontos = () => new Regra("x", "X", "valor > 1", 0, AcaoRegra.Pontuar);
        pontos.Should().Throw<ArgumentException>();
        new Regra("x", "X", "valor > 1", 0, AcaoRegra.Negar).Pontuacao.Should().Be(0);
    }

    [Fact]
    public void DecideAprovarRevisarNegarPelaPontuacao()
    {
        var motor = new MotorAntifraude(PoliticaPadrao());
        var normal = motor.Avaliar(Fabrica.Tx("a", 100), T0);
        normal.Decisao.Should().Be(Decisao.Aprovar);
        normal.Pontuacao.Should().Be(0);
        normal.Motivo.Should().Be("nenhuma regra disparou");
        normal.RegrasAvaliadas.Should().Be(5); // a desativada não conta

        var pontuada = motor.Avaliar(Fabrica.Tx("b", 6000), T0);
        pontuada.Decisao.Should().Be(Decisao.Aprovar); // 30 < 40
        pontuada.Pontuacao.Should().Be(30);
        pontuada.Motivo.Should().Be("pontuação 30 < 40");
        pontuada.Disparadas.Should().ContainSingle(d => d.Id == "valor-alto");

        var revisar = motor.Avaliar(Fabrica.Tx("c", 6000, pais: "US", presente: false), T0);
        revisar.Pontuacao.Should().Be(55);
        revisar.Decisao.Should().Be(Decisao.Revisar);
        revisar.Disparadas.Select(d => d.Id).Should().BeEquivalentTo(new[] { "valor-alto", "exterior" });
    }

    [Fact]
    public void VelocidadeAcumulaEntreAvaliacoes()
    {
        var motor = new MotorAntifraude(PoliticaPadrao());
        for (var i = 0; i < 3; i++) motor.Avaliar(Fabrica.Tx("r" + i, 6000, momento: T0.AddMinutes(i)), T0).Decisao.Should().Be(Decisao.Aprovar);
        var quarta = motor.Avaliar(Fabrica.Tx("r4", 6000, momento: T0.AddMinutes(3)), T0);
        quarta.Pontuacao.Should().Be(70); // 30 + 40
        quarta.Decisao.Should().Be(Decisao.Negar);
        quarta.Motivo.Should().Contain("70 ≥ 70");
        motor.TotalAvaliadas.Should().Be(4);
    }

    [Fact]
    public void ConsultarNaoRegistraNoHistorico()
    {
        var motor = new MotorAntifraude(PoliticaPadrao());
        for (var i = 0; i < 5; i++) motor.Consultar(Fabrica.Tx("q" + i, momento: T0.AddSeconds(i)), T0);
        motor.Historico.Total.Should().Be(0);
        motor.TotalAvaliadas.Should().Be(0);
    }

    [Fact]
    public void NegacaoDiretaVenceEAprovacaoDiretaPassa()
    {
        var motor = new MotorAntifraude(PoliticaPadrao());
        var negada = motor.Avaliar(Fabrica.Tx("n", 10, comerciante: "cassino-x", extras: new() { ["segmento"] = "vip" }), T0);
        negada.Decisao.Should().Be(Decisao.Negar);
        negada.Motivo.Should().Contain("bloqueado");

        var vip = motor.Avaliar(Fabrica.Tx("v", 15000, pais: "US", presente: false, extras: new() { ["segmento"] = "vip" }), T0);
        vip.Decisao.Should().Be(Decisao.Aprovar);
        vip.Pontuacao.Should().Be(55); // pontuou, mas a aprovação direta prevalece
        vip.Motivo.Should().Contain("vip");
    }

    [Fact]
    public void ErroEmUmaRegraNaoDerrubaAsOutras()
    {
        var p = PoliticaPadrao();
        p.Salvar(new Regra("quebrada", "Quebrada", "extras.parcelas > 3", 50, AcaoRegra.Pontuar)); // extras ausente -> null comparado
        var a = new MotorAntifraude(p).Avaliar(Fabrica.Tx("e", 6000), T0);
        a.Erros.Should().ContainSingle(e => e.Id == "quebrada" && e.Erro.Contains("valor ausente"));
        a.Decisao.Should().Be(Decisao.Aprovar);
        a.Pontuacao.Should().Be(30); // a regra válida pontuou normalmente
    }

    [Fact]
    public void SimulacaoIsolaHistoricoEContaDisparos()
    {
        var p = PoliticaPadrao();
        var lote = Enumerable.Range(0, 6).Select(i => Fabrica.Tx("s" + i, i == 5 ? 9000 : 100, momento: T0.AddMinutes(i))).ToList();
        lote.Add(Fabrica.Tx("bloq", 10, comerciante: "CASSINO-X", momento: T0.AddMinutes(10)));

        var r = MotorAntifraude.Simular(p, lote, T0);
        r.Transacoes.Should().Be(7);
        r.Negadas.Should().Be(2);   // s5: rajada(40)+valor alto(30)=70 ; bloq: negação direta
        r.Aprovadas.Should().Be(3); // s0, s1, s2 sem anteriores suficientes
        r.Revisadas.Should().Be(2); // s3 e s4 já têm 3 anteriores em 10m -> 40 pontos
        r.DisparosPorRegra["rajada"].Should().Be(4); // s3, s4, s5 e bloq (5 anteriores nos últimos 10 minutos)
        r.DisparosPorRegra["bloqueado"].Should().Be(1);
        r.Avaliacoes.Should().HaveCount(7);
        var motor = new MotorAntifraude(p);
        motor.Historico.Total.Should().Be(0); // simulação não toca o motor real
    }

    [Fact]
    public void PoliticaVaiEVoltaDeJson()
    {
        var p = PoliticaPadrao();
        var json = PoliticaJson.Salvar(p);
        var copia = PoliticaJson.Carregar(json);
        copia.LimiarRevisao.Should().Be(40);
        copia.Regras.Should().HaveCount(6);
        copia.Obter("desativada")!.Ativa.Should().BeFalse();
        copia.Obter("bloqueado")!.Acao.Should().Be(AcaoRegra.Negar);
        PoliticaJson.Salvar(copia).Should().Be(json);
    }

    [Fact]
    public void AlterarRegraGeraNovaVersao()
    {
        var r = new Regra("x", "X", "valor > 1", 10, AcaoRegra.Pontuar);
        var r2 = r.ComAlteracoes(null, "valor > 2", 20, null, false, "nova", T0);
        r2.Versao.Should().Be(2);
        r2.Expressao.Should().Be("valor > 2");
        r2.Pontuacao.Should().Be(20);
        r2.Ativa.Should().BeFalse();
        r2.Descricao.Should().Be("nova");
        r.Versao.Should().Be(1); // imutável
    }

    [Fact]
    public void LimiaresInvalidosSaoRejeitados()
    {
        var act = () => new Politica(70, 40);
        act.Should().Throw<ArgumentException>();
    }
}
