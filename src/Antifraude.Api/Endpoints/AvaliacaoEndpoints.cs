using Antifraude.Api.Contratos;
using Antifraude.Core.Dominio;
using Antifraude.Core.Expressoes;
using Antifraude.Core.Historico;
using Antifraude.Core.Motor;
using Antifraude.Core.Regras;

namespace Antifraude.Api.Endpoints;

public static class AvaliacaoEndpoints
{
    public static IEndpointRouteBuilder MapAvaliacao(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Avaliação");

        g.MapPost("/avaliar", (TransacaoRequest req, MotorAntifraude motor) =>
        {
            var agora = DateTimeOffset.UtcNow;
            return Results.Ok(motor.Avaliar(req.ParaDominio(agora), agora));
        }).WithSummary("Avalia uma transação e a registra no histórico de velocidade")
          .WithDescription("Devolve decisão (Aprovar, Revisar, Negar), pontuação, regras disparadas e regras com erro de avaliação.");

        g.MapPost("/consultar", (TransacaoRequest req, MotorAntifraude motor) =>
        {
            var agora = DateTimeOffset.UtcNow;
            return Results.Ok(motor.Consultar(req.ParaDominio(agora), agora));
        }).WithSummary("Avalia sem registrar no histórico (e se?)");

        g.MapPost("/simular", (SimulacaoRequest req, MotorAntifraude motor) =>
        {
            if (req.Transacoes is null || req.Transacoes.Count == 0) return Results.BadRequest(new { title = "informe ao menos uma transação", status = 400 });
            if (req.Transacoes.Count > 50_000) return Results.BadRequest(new { title = "máximo de 50.000 transações por simulação", status = 400 });
            var agora = DateTimeOffset.UtcNow;
            var politica = req.Politica is null ? motor.Politica : Montar(req.Politica);
            var lote = req.Transacoes.Select(t => t.ParaDominio(agora));
            return Results.Ok(MotorAntifraude.Simular(politica, lote, agora, req.IncluirAvaliacoes));
        }).WithSummary("Simula um lote com a política atual ou com uma candidata, em histórico isolado")
          .WithDescription("Útil para medir o impacto de uma regra nova antes de publicar: quantas transações seriam negadas, revisadas e aprovadas, e quantas vezes cada regra disparou.");

        g.MapPost("/regras/validar", (ValidarRequest req) =>
        {
            try
            {
                var arvore = Parser.Analisar(req.Expressao);
                object? resultadoExemplo = null;
                if (req.Exemplo is not null)
                {
                    var agora = DateTimeOffset.UtcNow;
                    resultadoExemplo = new Avaliador(req.Exemplo.ParaDominio(agora), new JanelaDeslizante()).Condicao(arvore);
                }
                return Results.Ok(new { valida = true, resultadoExemplo });
            }
            catch (Exception e) when (e is ErroDeExpressao or CampoDesconhecidoException)
            {
                return Results.Ok(new { valida = false, erro = e.Message, posicao = (e as ErroDeExpressao)?.Posicao + 1 });
            }
        }).WithSummary("Valida a sintaxe de uma expressão e, opcionalmente, avalia contra uma transação de exemplo");

        g.MapGet("/linguagem", () => Results.Ok(new
        {
            campos = Transacao.Campos,
            funcoes = Avaliador.Funcoes,
            operadores = new[] { "E / AND", "OU / OR", "NAO / NOT", "== != < <= > >=", "EM / IN [lista]", "CONTEM / CONTAINS", "+ - * / %", "( )" },
            janelas = "30s, 10m, 2h, 7d (máximo 30d)",
            exemplos = new[]
            {
                "valor > 5000",
                "pais != \"BR\" E NAO cartao_presente",
                "contagem(\"cartao\", \"10m\") >= 5",
                "soma(\"valor\", \"cartao\", \"1h\") + valor > 10000",
                "distintos(\"pais\", \"cartao\", \"24h\") >= 2",
                "maiusculo(comerciante) EM [\"CASSINO-X\"]",
                "extras.segmento == \"vip\" E valor <= 20000",
            },
        })).WithSummary("Referência da linguagem de regras: campos, funções, operadores e exemplos");

        return app;
    }

    internal static Politica Montar(PoliticaRequest req)
    {
        var p = new Politica(req.LimiarRevisao, req.LimiarNegacao);
        foreach (var r in req.Regras) p.Salvar(r.ParaDominio());
        return p;
    }
}
