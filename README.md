# Antifraude

[![CI](https://github.com/condeDeveloper/antifraude/actions/workflows/ci.yml/badge.svg)](https://github.com/condeDeveloper/antifraude/actions/workflows/ci.yml)

Motor de regras antifraude para transações de cartão, em C# e .NET 8, com uma linguagem própria de regras. As regras são texto, compiladas em tempo de execução por um lexer, um parser e um avaliador escritos à mão; o motor soma pontos, aplica listas de negação e aprovação, decide entre **Aprovar**, **Revisar** e **Negar** e explica quais regras dispararam.

```text
valor > 5000
pais != "BR" E NAO cartao_presente
contagem("cartao", "10m") >= 5
soma("valor", "cartao", "1h") + valor > 10000
distintos("pais", "cartao", "24h") >= 2
maiusculo(comerciante) EM ["CASSINO-X", "LOJA-FANTASMA"]
extras.segmento == "vip" E valor <= 20000
```

## O que tem dentro

- **Linguagem de regras** com precedência correta (OU < E < NAO < comparação < soma < produto), parênteses, listas, textos, números decimais, booleanos, campos da transação e campos extras (`extras.nome`), palavras-chave em português e inglês (E/AND, OU/OR, NAO/NOT, EM/IN, CONTEM/CONTAINS).
- **Erros com posição**: sintaxe e tipos são reportados com a coluna exata (`esperava ')' , encontrei fim da expressão (posição 12)`), e o endpoint de validação testa a expressão contra uma transação de exemplo.
- **Funções de velocidade** sobre um histórico em memória com janelas deslizantes: `contagem`, `soma`, `distintos` e `segundos_desde_ultima` por cartão, cliente, IP, dispositivo ou comerciante, com poda automática.
- **Política** com regras versionadas e três ações: pontuar, negar direto (lista negra) e aprovar direto (lista branca). Negação vence aprovação. Uma regra com erro não derruba as outras: aparece em `erros` na resposta.
- **Simulação em lote**: reproduz milhares de transações em ordem cronológica num histórico isolado, com a política atual ou com uma candidata, e devolve quantas seriam aprovadas, revisadas e negadas e quantas vezes cada regra disparou. É como se mede o impacto de uma regra nova antes de publicar.
- **API** minimal com Swagger, CRUD de regras que compila e valida a expressão ao salvar e persiste a política em JSON.

## Rodar

```bash
dotnet run --project src/Antifraude.Api
```

- Documentação interativa: http://localhost:5000/docs
- Referência da linguagem: `GET /api/linguagem`
- A política padrão vem de `src/Antifraude.Api/politica.json` (9 regras de exemplo)

```bash
# avaliar uma transação
curl -s localhost:5000/api/avaliar -H 'Content-Type: application/json' -d '{
  "id":"t-1","valor":7500,"cartao":"4111","cliente":"c-9","comerciante":"LOJA ONLINE","pais":"US","cartaoPresente":false
}'
# => {"decisao":"Revisar","pontuacao":55,"motivo":"pontuação 55 ≥ 40","disparadas":[{"id":"exterior-sem-cartao",...},{"id":"valor-alto",...}],...}

# validar uma regra antes de criar
curl -s localhost:5000/api/regras/validar -H 'Content-Type: application/json' -d '{"expressao":"valor > 100 E pais EM [\"AR\",\"UY\"]"}'

# criar a regra
curl -s -X PUT localhost:5000/api/regras/vizinhos -H 'Content-Type: application/json' \
  -d '{"nome":"Países vizinhos","expressao":"valor > 100 E pais EM [\"AR\",\"UY\"]","pontuacao":20}'
```

## Testes

```bash
dotnet test
```

Cobrem o lexer (tokens, dois idiomas), o parser (precedência, parênteses, listas, chamadas, mensagens e posição de erro), o avaliador (operadores, funções, extras, curto-circuito, erros de tipo), as janelas deslizantes (contagem, soma, distintos, poda, exclusão da própria transação e de futuras), a política (decisões por pontuação, negação e aprovação direta, regra com erro isolada, simulação, JSON de ida e volta, versionamento) e a API de ponta a ponta.

## Arquitetura

```
src/Antifraude.Core
  Dominio/      Transacao e acesso a campos
  Expressoes/   Lexer, Parser, Ast, Avaliador (a linguagem)
  Historico/    JanelaDeslizante (funções de velocidade)
  Regras/       Regra, Politica, PoliticaJson
  Motor/        MotorAntifraude (tempo real e simulação)
src/Antifraude.Api      minimal API, Swagger, persistência da política
tests/Antifraude.Tests  xUnit + FluentAssertions
```

O núcleo não depende de nenhum framework. O histórico é em memória e por processo; para produção, a interface `IHistorico` é o ponto de troca por Redis ou similar.

## Licença

MIT
