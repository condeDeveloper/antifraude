using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Antifraude.Tests.Api;

public class ApiTests : IClassFixture<ApiTests.Fabrica>
{
    public sealed class Fabrica : WebApplicationFactory<Program>
    {
        public string Arquivo { get; } = Path.Combine(Path.GetTempPath(), $"antifraude-{Guid.NewGuid():N}.json");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // parte de uma cópia da política padrão do projeto, para não escrever no arquivo versionado
            File.Copy(Path.Combine(AppContext.BaseDirectory, "politica.json"), Arquivo, overwrite: true);
            builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Politica:Arquivo"] = Arquivo }));
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try { File.Delete(Arquivo); } catch { /* temporário */ }
        }
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;
    private readonly Fabrica _fabrica;

    public ApiTests(Fabrica fabrica) { _fabrica = fabrica; _http = fabrica.CreateClient(); }

    private static async Task<JsonElement> Corpo(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>(Json);

    // momento fixo (14h UTC) para as regras de horário não dependerem da hora em que os testes rodam
    private static object Tx(string id, decimal valor, string cartao = "c-api", string pais = "BR", bool presente = true, string comerciante = "MERCADO", object? extras = null) =>
        new { id, momento = "2026-09-16T14:00:00Z", valor, cartao, pais, cartaoPresente = presente, comerciante, cliente = "cli", extras };

    [Fact]
    public async Task PoliticaPadraoCarregaEExplicaDecisoes()
    {
        var pol = await Corpo(await _http.GetAsync("/api/politica"));
        pol.GetProperty("limiarNegacao").GetInt32().Should().Be(70);
        pol.GetProperty("regras").GetArrayLength().Should().BeGreaterThanOrEqualTo(8);

        var ok = await Corpo(await _http.PostAsJsonAsync("/api/avaliar", Tx("a1", 100)));
        ok.GetProperty("decisao").GetString().Should().Be("Aprovar");

        var caro = await Corpo(await _http.PostAsJsonAsync("/api/avaliar", Tx("a2", 7000, pais: "US", presente: false)));
        caro.GetProperty("decisao").GetString().Should().Be("Revisar");
        caro.GetProperty("pontuacao").GetInt32().Should().Be(55);
        caro.GetProperty("disparadas").EnumerateArray().Select(d => d.GetProperty("id").GetString()).Should().Contain("valor-alto").And.Contain("exterior-sem-cartao");

        var bloqueado = await Corpo(await _http.PostAsJsonAsync("/api/avaliar", Tx("a3", 10, comerciante: "cassino-x")));
        bloqueado.GetProperty("decisao").GetString().Should().Be("Negar");

        var vip = await Corpo(await _http.PostAsJsonAsync("/api/avaliar", Tx("a4", 9000, pais: "US", presente: false, extras: new { segmento = "vip" })));
        vip.GetProperty("decisao").GetString().Should().Be("Aprovar");
    }

    [Fact]
    public async Task RajadaNoMesmoCartaoEscalaAteNegar()
    {
        string cartao = "rajada-" + Guid.NewGuid().ToString("N")[..8];
        JsonElement ultimo = default;
        for (var i = 0; i < 7; i++) ultimo = await Corpo(await _http.PostAsJsonAsync("/api/avaliar", Tx("r" + i, 6000, cartao)));
        ultimo.GetProperty("decisao").GetString().Should().Be("Negar");
        ultimo.GetProperty("disparadas").EnumerateArray().Select(d => d.GetProperty("id").GetString()).Should().Contain("rajada-no-cartao").And.Contain("gasto-acumulado");
    }

    [Fact]
    public async Task ValidaExpressoesEExplicaErroComPosicao()
    {
        var valida = await Corpo(await _http.PostAsJsonAsync("/api/regras/validar", new { expressao = "valor > 10 E pais == \"BR\"", exemplo = Tx("x", 50) }));
        valida.GetProperty("valida").GetBoolean().Should().BeTrue();
        valida.GetProperty("resultadoExemplo").GetBoolean().Should().BeTrue();

        var invalida = await Corpo(await _http.PostAsJsonAsync("/api/regras/validar", new { expressao = "valor > E" }));
        invalida.GetProperty("valida").GetBoolean().Should().BeFalse();
        invalida.GetProperty("erro").GetString().Should().Contain("esperava um valor");
        invalida.GetProperty("posicao").GetInt32().Should().Be(9);

        var linguagem = await Corpo(await _http.GetAsync("/api/linguagem"));
        linguagem.GetProperty("funcoes").GetArrayLength().Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task CrudDeRegrasPersisteNoArquivo()
    {
        var criada = await _http.PutAsJsonAsync("/api/regras/teste-api", new { nome = "Teste API", expressao = "moeda == \"USD\"", pontuacao = 20 });
        criada.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Corpo(criada)).GetProperty("versao").GetInt32().Should().Be(1);

        var alterada = await Corpo(await _http.PatchAsJsonAsync("/api/regras/teste-api", new { pontuacao = 25, ativa = false }));
        alterada.GetProperty("versao").GetInt32().Should().Be(2);
        alterada.GetProperty("ativa").GetBoolean().Should().BeFalse();

        var sintaxe = await _http.PutAsJsonAsync("/api/regras/ruim", new { nome = "Ruim", expressao = "valor >", pontuacao = 10 });
        sintaxe.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var erro = await Corpo(sintaxe);
        erro.GetProperty("posicao").GetInt32().Should().BeGreaterThan(0);
        erro.GetProperty("title").GetString().Should().Contain("esperava um valor");

        File.ReadAllText(_fabrica.Arquivo).Should().Contain("teste-api").And.NotContain("\"ruim\"");

        (await _http.DeleteAsync("/api/regras/teste-api")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _http.GetAsync("/api/regras/teste-api")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SimulacaoComPoliticaCandidata()
    {
        var lote = Enumerable.Range(0, 20).Select(i => Tx("s" + i, i % 4 == 0 ? 8000 : 50, "cartao-sim", i % 5 == 0 ? "US" : "BR", presente: false)).ToList();
        var candidata = new
        {
            limiarRevisao = 30, limiarNegacao = 60,
            regras = new object[]
            {
                new { id = "caro", nome = "Caro", expressao = "valor > 1000", pontuacao = 35 },
                new { id = "fora", nome = "Fora", expressao = "pais != \"BR\"", pontuacao = 30 },
            },
        };
        var r = await Corpo(await _http.PostAsJsonAsync("/api/simular", new { transacoes = lote, politica = candidata, incluirAvaliacoes = false }));
        r.GetProperty("transacoes").GetInt32().Should().Be(20);
        (r.GetProperty("aprovadas").GetInt32() + r.GetProperty("revisadas").GetInt32() + r.GetProperty("negadas").GetInt32()).Should().Be(20);
        r.GetProperty("disparosPorRegra").GetProperty("caro").GetInt32().Should().Be(5);
        r.GetProperty("disparosPorRegra").GetProperty("fora").GetInt32().Should().Be(4);
        r.GetProperty("avaliacoes").GetArrayLength().Should().Be(0);

        var vazio = await _http.PostAsJsonAsync("/api/simular", new { transacoes = Array.Empty<object>() });
        vazio.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
