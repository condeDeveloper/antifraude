using Antifraude.Core.Dominio;
using Antifraude.Core.Expressoes;

namespace Antifraude.Core.Historico;

/// <summary>
/// Histórico em memória indexado por (campo chave, valor), com janelas deslizantes por tempo.
/// Guarda só o necessário para as funções de velocidade e poda o que passou da janela máxima.
/// </summary>
public sealed class JanelaDeslizante : IHistorico
{
    private readonly Dictionary<string, List<Transacao>> _porChave = new();
    private readonly object _trava = new();
    private readonly string[] _campos;

    public TimeSpan JanelaMaxima { get; }
    public int LimitePorChave { get; }

    public JanelaDeslizante(TimeSpan? janelaMaxima = null, int limitePorChave = 10_000, params string[] camposIndexados)
    {
        JanelaMaxima = janelaMaxima ?? TimeSpan.FromDays(7);
        LimitePorChave = limitePorChave;
        _campos = camposIndexados.Length > 0 ? camposIndexados : new[] { "cartao", "cliente", "ip", "dispositivo", "comerciante" };
    }

    /// <summary>Registra a transação depois de avaliada, para que as funções contem só as anteriores.</summary>
    public void Registrar(Transacao t)
    {
        lock (_trava)
        {
            foreach (var campo in _campos)
            {
                var valor = t.Campo(campo)?.ToString();
                if (string.IsNullOrEmpty(valor)) continue;
                var lista = Lista(campo, valor);
                // mantém ordenado por momento mesmo se chegar fora de ordem
                var idx = lista.FindLastIndex(x => x.Momento <= t.Momento);
                lista.Insert(idx + 1, t);
                Podar(lista, t.Momento);
            }
        }
    }

    public int Contagem(Transacao atual, string chave, TimeSpan janela) => Anteriores(atual, chave, janela).Count();

    public decimal Soma(Transacao atual, string campo, string chave, TimeSpan janela) =>
        Anteriores(atual, chave, janela).Sum(x => x.Campo(campo) is decimal d ? d : 0m);

    public int Distintos(Transacao atual, string campo, string chave, TimeSpan janela) =>
        Anteriores(atual, chave, janela).Select(x => x.Campo(campo)?.ToString() ?? string.Empty).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public decimal? SegundosDesdeUltima(Transacao atual, string chave)
    {
        lock (_trava)
        {
            var valor = atual.Campo(chave)?.ToString();
            if (string.IsNullOrEmpty(valor) || !_porChave.TryGetValue(chave + "=" + valor, out var lista)) return null;
            var ultima = lista.LastOrDefault(x => x.Id != atual.Id && x.Momento <= atual.Momento);
            return ultima is null ? null : (decimal)(atual.Momento - ultima.Momento).TotalSeconds;
        }
    }

    public int Total { get { lock (_trava) return _porChave.Values.Sum(l => l.Count); } }

    private IEnumerable<Transacao> Anteriores(Transacao atual, string chave, TimeSpan janela)
    {
        if (!_campos.Contains(chave)) throw new ErroDeExpressao($"o campo \"{chave}\" não é indexado no histórico (use {string.Join(", ", _campos)})", 0);
        lock (_trava)
        {
            var valor = atual.Campo(chave)?.ToString();
            if (string.IsNullOrEmpty(valor) || !_porChave.TryGetValue(chave + "=" + valor, out var lista)) return Array.Empty<Transacao>();
            var inicio = atual.Momento - janela;
            return lista.Where(x => x.Id != atual.Id && x.Momento > inicio && x.Momento <= atual.Momento).ToArray();
        }
    }

    private List<Transacao> Lista(string campo, string valor)
    {
        var k = campo + "=" + valor;
        if (!_porChave.TryGetValue(k, out var lista)) _porChave[k] = lista = new List<Transacao>();
        return lista;
    }

    private void Podar(List<Transacao> lista, DateTimeOffset agora)
    {
        var limite = agora - JanelaMaxima;
        var remover = 0;
        while (remover < lista.Count && lista[remover].Momento < limite) remover++;
        if (lista.Count - remover > LimitePorChave) remover = lista.Count - LimitePorChave;
        if (remover > 0) lista.RemoveRange(0, remover);
    }
}
