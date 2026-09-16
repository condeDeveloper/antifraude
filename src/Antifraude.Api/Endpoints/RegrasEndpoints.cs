using Antifraude.Api.Contratos;
using Antifraude.Core.Motor;
using Antifraude.Core.Regras;

namespace Antifraude.Api.Endpoints;

/// <summary>Persiste a política em arquivo depois de cada alteração.</summary>
public sealed class ArmazenamentoDePolitica
{
    private readonly string _caminho;
    private readonly object _trava = new();
    public ArmazenamentoDePolitica(string caminho) => _caminho = caminho;
    public void Salvar(Politica p) { lock (_trava) PoliticaJson.SalvarArquivo(p, _caminho); }
}

public static class RegrasEndpoints
{
    public static IEndpointRouteBuilder MapRegras(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api").WithTags("Política e regras");

        g.MapGet("/politica", (MotorAntifraude motor) => Results.Ok(PoliticaResponse.De(motor.Politica)))
            .WithSummary("Política atual: limiares e regras");

        g.MapPut("/politica/limiares", (LimiaresRequest req, MotorAntifraude motor, ArmazenamentoDePolitica arq) =>
        {
            motor.Politica.DefinirLimiares(req.LimiarRevisao, req.LimiarNegacao);
            arq.Salvar(motor.Politica);
            return Results.Ok(PoliticaResponse.De(motor.Politica));
        }).WithSummary("Altera os limiares de revisão e negação");

        g.MapGet("/regras", (MotorAntifraude motor) => Results.Ok(motor.Politica.Regras.Select(RegraResponse.De)))
            .WithSummary("Lista as regras");

        g.MapGet("/regras/{id}", (string id, MotorAntifraude motor) =>
            motor.Politica.Obter(id) is { } r ? Results.Ok(RegraResponse.De(r)) : Results.NotFound(new { title = "regra não encontrada", status = 404 }))
            .WithSummary("Consulta uma regra");

        g.MapPut("/regras/{id}", (string id, RegraRequest req, MotorAntifraude motor, ArmazenamentoDePolitica arq) =>
        {
            var existente = motor.Politica.Obter(id);
            var regra = existente is null
                ? new Regra(id, req.Nome, req.Expressao, req.Pontuacao, req.Acao, req.Ativa, req.Descricao)
                : existente.ComAlteracoes(req.Nome, req.Expressao, req.Pontuacao, req.Acao, req.Ativa, req.Descricao, DateTimeOffset.UtcNow);
            motor.Politica.Salvar(regra);
            arq.Salvar(motor.Politica);
            return existente is null ? Results.Created($"/api/regras/{id}", RegraResponse.De(regra)) : Results.Ok(RegraResponse.De(regra));
        }).WithSummary("Cria ou substitui uma regra (a expressão é compilada e validada; alterações geram nova versão)");

        g.MapPatch("/regras/{id}", (string id, RegraPatchRequest req, MotorAntifraude motor, ArmazenamentoDePolitica arq) =>
        {
            var existente = motor.Politica.Obter(id);
            if (existente is null) return Results.NotFound(new { title = "regra não encontrada", status = 404 });
            var regra = existente.ComAlteracoes(req.Nome, req.Expressao, req.Pontuacao, req.Acao, req.Ativa, req.Descricao, DateTimeOffset.UtcNow);
            motor.Politica.Salvar(regra);
            arq.Salvar(motor.Politica);
            return Results.Ok(RegraResponse.De(regra));
        }).WithSummary("Altera parte de uma regra (ex.: ativar/desativar)");

        g.MapDelete("/regras/{id}", (string id, MotorAntifraude motor, ArmazenamentoDePolitica arq) =>
        {
            if (!motor.Politica.Remover(id)) return Results.NotFound(new { title = "regra não encontrada", status = 404 });
            arq.Salvar(motor.Politica);
            return Results.NoContent();
        }).WithSummary("Remove uma regra");

        return app;
    }
}
