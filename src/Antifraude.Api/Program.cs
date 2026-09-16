using System.Text.Json.Serialization;
using Antifraude.Api.Endpoints;
using Antifraude.Core.Dominio;
using Antifraude.Core.Expressoes;
using Antifraude.Core.Motor;
using Antifraude.Core.Regras;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var caminho = config["Politica:Arquivo"] ?? "politica.json";
    var politica = File.Exists(caminho) ? PoliticaJson.CarregarArquivo(caminho) : new Politica();
    return new MotorAntifraude(politica);
});
builder.Services.AddSingleton(sp => new ArmazenamentoDePolitica(sp.GetRequiredService<IConfiguration>()["Politica:Arquivo"] ?? "politica.json"));

builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o => o.SwaggerDoc("v1", new OpenApiInfo
{
    Title = "Antifraude",
    Version = "v1",
    Description = "Motor de regras antifraude com linguagem própria: expressões compiladas, funções de velocidade em janelas deslizantes "
                  + "(contagem, soma, distintos por cartão/cliente/ip/dispositivo), pontuação com decisão Aprovar/Revisar/Negar, explicação das regras disparadas e simulação em lote.",
}));

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (ErroDeExpressao e) { await Problema(ctx, 422, e.Message, e.Posicao + 1); }
    catch (CampoDesconhecidoException e) { await Problema(ctx, 422, e.Message, null); }
    catch (ArgumentException e) { await Problema(ctx, 422, e.Message, null); }
    catch (BadHttpRequestException e) { await Problema(ctx, 400, e.Message, null); }
});
app.UseSwagger();
app.UseSwaggerUI(o => { o.RoutePrefix = "docs"; o.DocumentTitle = "Antifraude"; });

app.MapAvaliacao();
app.MapRegras();
app.MapGet("/saude", (MotorAntifraude motor) => Results.Ok(new { status = "ok", regras = motor.Politica.Regras.Count, avaliadas = motor.TotalAvaliadas, historico = motor.Historico.Total }));

app.Run();

static async Task Problema(HttpContext ctx, int status, string detalhe, int? posicao)
{
    if (ctx.Response.HasStarted) return;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new { title = detalhe, status, posicao });
}

public partial class Program { }
