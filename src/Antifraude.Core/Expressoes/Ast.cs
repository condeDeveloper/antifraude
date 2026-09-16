namespace Antifraude.Core.Expressoes;

/// <summary>Árvore sintática da expressão. Cada nó guarda a posição para mensagens de erro em tempo de avaliação.</summary>
public abstract record No(int Posicao);

public sealed record Literal(object? Valor, int Posicao) : No(Posicao);

public sealed record Campo(string Nome, int Posicao) : No(Posicao);

public sealed record ListaLiteral(IReadOnlyList<No> Itens, int Posicao) : No(Posicao);

public sealed record Unario(TipoToken Operador, No Operando, int Posicao) : No(Posicao);

public sealed record Binario(TipoToken Operador, No Esquerda, No Direita, int Posicao) : No(Posicao);

public sealed record Chamada(string Funcao, IReadOnlyList<No> Argumentos, int Posicao) : No(Posicao);
