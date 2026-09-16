namespace Antifraude.Core.Dominio;

/// <summary>Transação avaliada pelo motor. Campos fixos mais um dicionário de extras acessível nas regras como extras.nome.</summary>
public sealed record Transacao(
    string Id,
    DateTimeOffset Momento,
    decimal Valor,
    string Moeda,
    string Cartao,
    string Cliente,
    string Comerciante,
    string Categoria,
    string Pais,
    string Ip,
    string Dispositivo,
    bool CartaoPresente,
    IReadOnlyDictionary<string, string>? Extras = null)
{
    public IReadOnlyDictionary<string, string> Extras { get; init; } = Extras ?? new Dictionary<string, string>();

    /// <summary>Valor de um campo pelo nome usado nas regras, ou null se não existir.</summary>
    public object? Campo(string nome)
    {
        switch (nome)
        {
            case "id": return Id;
            case "momento": return Momento;
            case "valor": return Valor;
            case "moeda": return Moeda;
            case "cartao": return Cartao;
            case "cliente": return Cliente;
            case "comerciante": return Comerciante;
            case "categoria": return Categoria;
            case "pais": return Pais;
            case "ip": return Ip;
            case "dispositivo": return Dispositivo;
            case "cartao_presente": return CartaoPresente;
            case "hora": return (decimal)Momento.Hour;
            case "dia_semana": return (decimal)(int)Momento.DayOfWeek;
        }
        if (nome.StartsWith("extras.", StringComparison.Ordinal))
        {
            var chave = nome["extras.".Length..];
            if (!Extras.TryGetValue(chave, out var v)) return null;
            return decimal.TryParse(v, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : v;
        }
        throw new CampoDesconhecidoException(nome);
    }

    public static readonly IReadOnlyList<string> Campos = new[]
    {
        "id", "momento", "valor", "moeda", "cartao", "cliente", "comerciante", "categoria", "pais", "ip", "dispositivo", "cartao_presente", "hora", "dia_semana", "extras.*",
    };
}

public sealed class CampoDesconhecidoException : Exception
{
    public CampoDesconhecidoException(string campo) : base($"campo desconhecido: {campo}") { }
}
