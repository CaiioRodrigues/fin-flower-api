# Fin Flower API

Back-end do Fin Flower: controle financeiro **por eventos**. Cada evento reúne
seus lançamentos de entrada e saída, e o resultado de cada evento alimenta o
caixa consolidado.

```
Evento (Festa de Ano Novo, 12/12)
  ├── Lançamentos ......... realizado: o que já entrou e saiu
  │     ├── Entrada: Venda de ingressos    R$ 8.000
  │     └── Saída:   Buffet                R$ 2.500
  │
  └── Contratos ........... previsto: o que foi acordado
        └── Prefeitura — R$ 9.000 em 3x, boleto, PDF anexado
              ├── 1/3  R$ 3.000  05/10  liquidada → virou lançamento
              ├── 2/3  R$ 3.000  05/11  em aberto
              └── 3/3  R$ 3.000  05/12  em aberto

Caixa       = soma do resultado dos eventos (realizado)
Fluxo de caixa = realizado + parcelas em aberto por mês
```

.NET 10 (LTS), SQL Server, EF Core.

## Estado atual

| Área | Situação |
|---|---|
| Solution em 4 camadas + testes | pronto |
| Autenticação: registro, login, refresh, logout, `/me` | pronto |
| **Livro-caixa: entrada e saída, com ou sem evento** | pronto |
| **Fechamento mensal com saldo acumulado** | pronto |
| **Gastos fixos e pró-labore, gerados por competência** | pronto |
| **Orçamentos linha a linha, que viram contrato ao aprovar** | pronto |
| Eventos como agrupador (CRUD, fechar/reabrir) | pronto |
| Contratos parcelados, com PDF anexado | pronto |
| Fluxo de caixa: vencidos, mês corrente e previsão | pronto |
| Exportação dos relatórios em Excel e PDF | pronto |

## Arquitetura

Dependências apontam sempre para dentro: o domínio não conhece ninguém.

```
FinFlower.Api ──────────► FinFlower.Infrastructure ──────────► FinFlower.Application ──────────► FinFlower.Domain
  endpoints                 EF Core, JWT, hash                    casos de uso, DTOs               entidades e regras
  middleware                repositórios                          validações, interfaces           sem dependência externa
```

| Projeto | Responsabilidade |
|---|---|
| `FinFlower.Domain` | Entidades e invariantes. Sem EF, sem ASP.NET, sem pacote externo. |
| `FinFlower.Application` | Casos de uso, DTOs, validações e as interfaces que a infraestrutura implementa. |
| `FinFlower.Infrastructure` | EF Core, repositórios, hash de senha, emissão de token. |
| `FinFlower.Api` | Endpoints, pipeline HTTP, autenticação e tradução de erro para status. |

Decisões que sustentam o desenho:

- **`Event` é raiz de agregação.** Lançamento só nasce e muda através do evento,
  então "evento fechado não aceita alteração" vale para qualquer caminho de código.
- **Erro de negócio é valor de retorno** (`Result`/`Result<T>`), não exceção. O
  fluxo fica na assinatura do método, e a tradução para HTTP acontece num lugar só.
- **Dinheiro é `decimal(18,2)`**, com o valor sempre positivo e o sentido no campo
  `Type`. Nada de `double` e nada de valor negativo espalhado pelos relatórios.
- **Leitura separada da escrita.** `IEventRepository` devolve o agregado para as
  regras do domínio; `IEventQueries` projeta direto para DTO, então a listagem e o
  caixa somam no banco em vez de carregar todos os lançamentos para a memória.
- **Todo dado é filtrado pelo dono na própria consulta.** O `ownerId` vem do token e
  entra no `WHERE`, não numa checagem posterior — evento de outra pessoa responde 404.
- **Nome de código em inglês, mensagens ao usuário em português.** O front segue a
  mesma divisão.

### O caixa é o centro, o evento é um atributo

O sistema começou girando em torno do evento: o lançamento vivia dentro dele e
não existia dinheiro fora de um. Isso não descreve um negócio de verdade —
aluguel, contador, pró-labore e software não pertencem a evento nenhum, e mesmo
assim são as saídas mais previsíveis do mês.

Hoje **`Entry` é a raiz**: tem dono próprio e um `EventId` opcional. O evento
continua existindo para apurar resultado por trabalho realizado, mas virou um
rótulo do lançamento, não o dono dele.

A regra "evento fechado não muda" sobreviveu à inversão. Ela continua no
domínio, em `Event.EnsureAcceptsChanges()`; o que mudou foi quem pergunta —
antes o agregado se protegia sozinho, agora o caso de uso carrega o evento e
consulta antes de criar, mover ou remover um lançamento ligado a ele.

Consequência prática: excluir um evento com lançamentos é recusado. O dinheiro é
do caixa, e apagar o evento não pode fazê-lo sumir nem deixar lançamento
apontando para um evento que já não existe.

### Competência: `YearMonth`, não `DateOnly`

Todo número do caixa é apurado por mês, e usar uma data para representar "mês"
convida ao erro clássico de `>= 01/09` deixar o resto de setembro de fora.
`YearMonth` é um `readonly record struct` com comparação, aritmética de meses e
`DayOrLast(dia)` — que é como um gasto fixo todo dia 31 cai em 28/02 sem estourar.

### Gasto fixo e pró-labore: um motor, duas telas

São a mesma mecânica — um valor que se repete todo mês — então há um agregado só,
`RecurringItem`, separado por `RecurringKind`. O pró-labore precisa ser
distinguível porque **retirada de sócio não é custo do negócio**, e o fechamento
mensal responde as duas coisas em separado.

Gerar a competência é **idempotente**: quem opera vai clicar duas vezes. A
consulta prévia evita o trabalho e um índice único filtrado em
`(RecurringItemId, RecurringMonth)` fecha a porta mesmo com duas requisições
simultâneas.

Alterar o valor do item vale **para frente**: o aluguel de março já foi pago, e o
reajuste não reescreve o passado.

### Orçamento → contrato → caixa

`Quote` é a proposta montada linha a linha, com quantidade, unitário e desconto.
Aprovar é o único ponto em que uma venda vira previsão de caixa: gera um
`Contract` com as parcelas, na mesma transação, e grava o elo nos dois lados —
um orçamento vira um contrato só.

O total de cada linha é arredondado **antes** da soma, porque o cliente confere
linha a linha: `3 × R$ 33,33 = R$ 99,99`. Guardar `33,333` e arredondar no fim
daria R$ 100,00 numa linha que mostra R$ 33,33, e o centavo ficaria inexplicável.

### O saldo começa onde o dinheiro já estava

Sem um marco, o "saldo em caixa" é a soma do que foi digitado: quem começa a usar
o sistema em setembro lê **variação desde setembro** achando que lê saldo — e a
projeção erra pelo mesmo valor, que é justamente o número usado para decidir se
dá para pagar as contas.

`PUT /api/cash/opening` declara quanto havia em caixa numa data. Aceita valor
**negativo**: começar no vermelho é uma situação real, e recusá-la obrigaria a
mentir para o próprio caixa.

A data é um corte, não um detalhe. Lançamento anterior a ela **não entra no
saldo**, porque o valor declarado já o contém — somar os dois contaria o mesmo
dinheiro duas vezes. Como um lançamento que some da conta sem explicação parece
defeito, a resposta traz `ignoredEntries`, e a tela diz quantos são e por quê.

Um saldo inicial por dono, garantido por índice único no banco: dois deles se
somariam em silêncio, e o saldo passaria a mentir sem nenhum sintoma.

### Realizado e previsto na mesma linha do tempo

`/api/cash/monthly` devolve uma série só: meses passados pelo que de fato se
moveu, meses futuros pelo que está previsto. A janela padrão é **centrada no mês
corrente** — seis para trás e seis para a frente —, porque um caixa serve tanto
para ver de onde se veio quanto para saber se dá para pagar as contas do
trimestre.

O previsto tem **duas fontes**, e ignorar qualquer uma delas dá um número
otimista: as parcelas de contrato em aberto (o que entra) e os itens fixos que
ainda não viraram lançamento (o aluguel e o pró-labore que vão sair de qualquer
jeito). Contar só as parcelas mostraria dinheiro entrando sem o custo que vem
junto.

Duas regras que decorrem disso:

- **Mês passado não tem previsto.** O que aconteceu, aconteceu; prever o passado
  inventaria despesa que já foi paga ou nunca existiu.
- **Vencido sai à parte.** Uma parcela que venceu em julho e não foi paga não é
  previsão de julho nem de nenhum mês futuro: é dívida de agora. Somá-la a um
  mês inflaria um mês que já fechou ou um mês que não a espera.

### Realizado x previsto

**Lançamento** é o que já aconteceu; **parcela de contrato** é o que foi acordado
e ainda vai acontecer. Liquidar uma parcela cria o lançamento correspondente e
guarda o vínculo, então o mesmo dinheiro nunca é contado duas vezes — e estornar
desfaz os dois juntos. Um lançamento que veio de contrato não pode ser alterado
nem removido por fora: quem manda é a parcela.

Contrato é raiz de agregação própria, não parte do evento. Se vivesse dentro
dele, abrir um evento carregaria os PDFs junto.

**Parcela vencida é lido da data, não guardado.** Nenhuma rotina precisa varrer o
banco à meia-noite para virar status.

**A soma das parcelas é sempre igual ao contratado.** Dividir e arredondar cada
uma perderia centavos: R$ 1.000 em 3x daria 333,33 três vezes e o contrato
fecharia em 999,99. A divisão é feita em centavos inteiros e a sobra vai para as
últimas parcelas — 333,33 / 333,33 / 333,34.

### Relatórios exportados

Um relatório é montado como `ReportDocument` — um modelo neutro de métricas e
tabelas com colunas tipadas. `ExcelReportWriter` e `PdfReportWriter` apenas
renderizam esse modelo, então **um relatório novo não encosta nos geradores**.

No Excel, valor vai como número e data como data, com formato aplicado por cima:
texto formatado impediria somar, ordenar e usar tabela dinâmica, que é o motivo de
exportar para planilha. Cada tabela vira uma aba, com cabeçalho congelado e filtro.

No PDF, colunas de valor, data e contagem têm largura fixa — coluna proporcional
deixava `R$ 2.666,67` quebrar em duas linhas quando havia muito texto ao lado.
Quando a soma dessas larguras não cabe na página, o gerador **encolhe todas na
mesma proporção**, junto com a fonte. Sem isso o caixa mês a mês, com dez colunas
de dinheiro, derrubava a geração inteira com 500 em vez de apertar a tabela.

Um detalhe que só aparece abrindo o arquivo: o rótulo `Resultado por evento` não
é "caixa". Aquele relatório soma apenas o que está preso a um evento, e a maior
parte do custo de um mês — aluguel, contador, pró-labore — não está. Chamá-lo de
saldo daria um número que discorda do caixa mensal.

### Licença do QuestPDF

A geração de PDF usa **QuestPDF** sob a licença **Community**, declarada em
`AddInfrastructure`. Ela é gratuita para organizações abaixo do faturamento anual
definido pela licença; acima disso é preciso adquirir a versão paga. Vale conferir
em <https://www.questpdf.com/license/> antes de usar em produção. O Excel usa
**ClosedXML**, que é MIT e sem restrição.

### Erro esperado x invariante violada

Desfecho esperado da aplicação (não encontrado, sessão inválida, período invertido)
volta como `Result` e vira o status correspondente. Violação de invariante — lançar
em evento fechado, valor não positivo — é lançada pelo domínio como `DomainException`
e o middleware devolve 400. Assim o domínio não precisa confiar em quem o chama.

## Segurança

| Proteção | Implementação |
|---|---|
| Senha | PBKDF2-HMAC-SHA512, 210.000 iterações, sal por senha (parâmetros OWASP) |
| Verificação de senha | Comparação em tempo fixo (`CryptographicOperations.FixedTimeEquals`) |
| Access token | JWT HMAC-SHA256, 15 minutos, sem tolerância de relógio |
| Refresh token | 256 bits aleatórios, **só o hash vai para o banco**, rotação a cada uso |
| Reuso de token | Reapresentar um token já rotacionado derruba toda a cadeia do usuário |
| Força bruta | Bloqueio da conta por 15 min após 5 falhas + limite por IP nas rotas de credencial |
| Enumeração de usuário | Resposta idêntica para e-mail inexistente e senha errada, inclusive no tempo |
| Autorização | O id do usuário vem sempre do token, nunca do corpo ou da URL |
| IDOR | Evento de outro usuário responde 404 em toda rota, sem revelar que o id existe |
| Entrada | FluentValidation antes de qualquer regra de negócio, erro por campo em `ProblemDetails` |
| Injeção de SQL | EF Core parametrizado; nenhuma query montada por concatenação |
| Vazamento por erro | 500 genérico ao cliente; detalhe e stack trace só no log |
| Cabeçalhos | CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store` |
| CORS | Lista explícita de origens, nunca `AllowAnyOrigin` |
| Segredos | `user-secrets` em dev, variável de ambiente em produção; validados na subida, com mensagem que diz como configurar |
| Auditoria | `CreatedAt`/`UpdatedAt` automáticos e exclusão lógica em tudo |
| Upload de arquivo | Assinatura `%PDF` conferida no conteúdo, não na extensão nem no content-type que o cliente declara |
| Download de arquivo | Servido sempre como `application/pdf`: deixar o navegador interpretar um arquivo do usuário como HTML seria um XSS |
| Nome de arquivo | Só o nome, sem caminho — `../../web.config` não vira travessia de diretório |
| Tamanho do upload | Recusado antes da leitura, para um arquivo enorme não ocupar memória até o domínio rejeitá-lo |

## Como rodar no Visual Studio

O banco de desenvolvimento é o **LocalDB**, que já vem instalado com o Visual
Studio — não precisa de container nem de senha de `sa`. A connection string em
`appsettings.Development.json` já aponta para ele.

**1. Defina a chave do JWT**

Ela não vai para o repositório — segredo não se versiona. **Cada clone precisa
definir a sua**, inclusive o seu segundo clone na mesma máquina.

No Visual Studio: clique com o botão direito no projeto `FinFlower.Api` →
**Gerenciar Segredos do Usuário**, e cole:

```json
{
  "Jwt": {
    "SigningKey": "troque-por-uma-chave-longa-e-aleatoria-de-32-caracteres-ou-mais"
  }
}
```

Sem isso a aplicação nem sobe, e a mensagem de erro repete estas instruções.

> O `UserSecretsId` fica versionado no `.csproj` de propósito. Ele não é
> segredo — é só o endereço do cofre. Sem ele, `dotnet user-secrets` recusa o
> comando e o .NET não carrega nada, mesmo com o arquivo de segredos no lugar.
> O Visual Studio acrescenta a propriedade sozinho quando alguém usa o menu de
> segredos, mas aí ela existe só naquela máquina e some no clone seguinte.

**2. Rode (F5)**

O banco `FinFlower` é criado na primeira execução: em desenvolvimento a aplicação
aplica as migrations ao subir (`Database:MigrateOnStartup`). Não é preciso rodar
nada antes.

Para inspecionar os dados: Exibir → **Pesquisador de Objetos do SQL Server** →
`(localdb)\MSSQLLocalDB` → Bancos de Dados → `FinFlower`. Se o banco não
aparecer, clique com o botão direito no nó e escolha **Atualizar**.

Se preferir criar o banco à mão, no **Console do Gerenciador de Pacotes**:

```powershell
Update-Database -Project src\FinFlower.Infrastructure -StartupProject src\FinFlower.Api
```

Abre o Swagger em `https://localhost:7046/swagger`. A API também escuta em
`http://localhost:5212`.

## Como rodar com Docker

```bash
cp .env.example .env    # defina MSSQL_SA_PASSWORD e JWT_SIGNING_KEY
docker compose up -d --build
```

Sobe o SQL Server e a API em `http://localhost:5212`. A API espera o banco ficar
saudável antes de subir e aplica as migrations sozinha — no container não há quem
rode `Update-Database` antes.

O compose falha na hora se `MSSQL_SA_PASSWORD` ou `JWT_SIGNING_KEY` não estiverem
definidas, em vez de subir com um valor padrão inseguro.

```bash
docker compose logs -f api    # acompanhar
docker compose down           # parar (o volume do banco fica)
docker compose down -v        # parar e apagar os dados
```

### Só o banco em container

Para desenvolver no Visual Studio mas sem instalar SQL Server:

```bash
docker compose up -d sqlserver
```

E troque a connection string de desenvolvimento por:

```
Server=localhost,1433;Database=FinFlower;User Id=sa;Password=<sua senha>;TrustServerCertificate=True
```

### Migrations no start

`Database:MigrateOnStartup` aplica as migrations pendentes quando a aplicação
sobe. O padrão é **false** e o compose a liga explicitamente. Em produção o schema
deve ser aplicado por um passo próprio do deploy, não por uma instância que acabou
de subir — duas instâncias subindo juntas migrariam o mesmo banco ao mesmo tempo.

### Pela linha de comando

```bash
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)" --project src/FinFlower.Api
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/FinFlower.Infrastructure --startup-project src/FinFlower.Api
dotnet run --project src/FinFlower.Api
```

Em produção, tudo por variável de ambiente: `ConnectionStrings__Default` e
`Jwt__SigningKey`.

### Ligando o front

O front roda em `http://localhost:5173`, origem que já está liberada no CORS de
desenvolvimento. No `.env.local` dele: `VITE_API_URL=http://localhost:5212`.

## Comandos

```bash
dotnet build     # warnings são tratados como erro
dotnet test      # 165 testes
```

Nova migration:

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/FinFlower.Infrastructure \
  --startup-project src/FinFlower.Api \
  --output-dir Persistence/Migrations
```

Ou, no Console do Gerenciador de Pacotes:

```powershell
Add-Migration NomeDaMigration -Project src\FinFlower.Infrastructure -StartupProject src\FinFlower.Api -OutputDir Persistence\Migrations
```

## Endpoints

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| `POST` | `/api/auth/register` | — | Cria a conta e devolve a sessão |
| `POST` | `/api/auth/login` | — | Autentica por e-mail e senha |
| `POST` | `/api/auth/refresh` | — | Troca o refresh token por um novo par |
| `POST` | `/api/auth/logout` | — | Revoga o refresh token informado |
| `GET` | `/api/auth/me` | Bearer | Dados do usuário autenticado |
| `GET` | `/api/events` | Bearer | Lista os eventos com os totais de cada um |
| `POST` | `/api/events` | Bearer | Cria um evento |
| `GET` | `/api/events/{id}` | Bearer | Abre o evento com todos os seus lançamentos |
| `PUT` | `/api/events/{id}` | Bearer | Altera os dados do evento |
| `DELETE` | `/api/events/{id}` | Bearer | Exclui o evento (exclusão lógica) |
| `POST` | `/api/events/{id}/close` | Bearer | Fecha o evento e congela o resultado |
| `POST` | `/api/events/{id}/reopen` | Bearer | Reabre um evento fechado |
| `GET` | `/api/entries` | Bearer | Livro-caixa, com filtros e totais do período |
| `POST` | `/api/entries` | Bearer | Registra uma entrada ou saída |
| `GET` `PUT` `DELETE` | `/api/entries/{id}` | Bearer | Abre, altera e remove um lançamento |
| `GET` | `/api/entries/categories` | Bearer | Categorias já usadas, para sugerir no formulário |
| `GET` | `/api/cash/monthly` | Bearer | Caixa mês a mês, com saldo acumulado |
| `GET` | `/api/cash/opening` | Bearer | Saldo inicial declarado — 204 quando não há |
| `PUT` | `/api/cash/opening` | Bearer | Declara quanto havia em caixa numa data |
| `DELETE` | `/api/cash/opening` | Bearer | Remove o marco: o saldo volta a somar tudo |
| `GET` | `/api/recurring-items` | Bearer | Gastos fixos e pró-labore, com a situação da competência |
| `POST` | `/api/recurring-items` | Bearer | Cadastra um item fixo |
| `PUT` `DELETE` | `/api/recurring-items/{id}` | Bearer | Altera e exclui |
| `POST` | `/api/recurring-items/{id}/activate` | Bearer | Reativa o item |
| `POST` | `/api/recurring-items/{id}/deactivate` | Bearer | Suspende sem apagar o histórico |
| `POST` | `/api/recurring-items/generate` | Bearer | Lança a competência no caixa (idempotente) |
| `GET` | `/api/quotes` | Bearer | Lista os orçamentos |
| `POST` | `/api/quotes` | Bearer | Abre um orçamento em rascunho |
| `GET` `PUT` `DELETE` | `/api/quotes/{id}` | Bearer | Abre, altera e exclui |
| `POST` | `/api/quotes/{id}/items` | Bearer | Acrescenta uma linha |
| `PUT` `DELETE` | `/api/quotes/{id}/items/{itemId}` | Bearer | Altera e remove a linha |
| `PUT` | `/api/quotes/{id}/discount` | Bearer | Aplica desconto sobre o subtotal |
| `POST` | `/api/quotes/{id}/send` \| `/reject` \| `/reopen` | Bearer | Move o orçamento pelo fluxo |
| `POST` | `/api/quotes/{id}/approve` | Bearer | Aprova e gera o contrato com as parcelas |
| `GET` | `/api/reports/monthly/export` | Bearer | Caixa mês a mês em `xlsx` ou `pdf` |
| `GET` | `/api/reports/cash` | Bearer | Resultado por evento (não é o saldo do caixa) |
| `GET` | `/api/reports/cash-flow` | Bearer | Fluxo de caixa: vencidos, mês corrente e previsão |
| `GET` | `/api/reports/cash/export` | Bearer | Caixa por evento em `xlsx` ou `pdf` |
| `GET` | `/api/reports/cash-flow/export` | Bearer | Fluxo de caixa em `xlsx` ou `pdf` |
| `GET` | `/api/reports/installments/export` | Bearer | Parcelas em aberto em `xlsx` ou `pdf` |
| `GET` | `/api/events/{id}/statement/export` | Bearer | Extrato do evento em `xlsx` ou `pdf` |
| `POST` | `/api/contracts` | Bearer | Cria um contrato (evento opcional) com as parcelas |
| `GET` | `/api/contracts` | Bearer | Lista contratos, com o quanto já foi liquidado |
| `GET` `PUT` `DELETE` | `/api/contracts/{id}` | Bearer | Abre, altera e exclui |
| `POST` | `/api/contracts/{id}/installments/{n}/settle` | Bearer | Liquida e gera o lançamento |
| `POST` | `/api/contracts/{id}/installments/{n}/unsettle` | Bearer | Estorna e remove o lançamento |
| `POST` | `/api/contracts/{id}/installments/{n}/cancel` | Bearer | Cancela a parcela |
| `PUT` | `/api/contracts/{id}/installments/{n}/due-date` | Bearer | Altera o vencimento |
| `PUT` | `/api/contracts/{id}/installments/{n}/amount` | Bearer | Altera o valor, redistribuindo a diferença |
| `POST` `GET` `DELETE` | `/api/contracts/{id}/document` | Bearer | Anexa, baixa e remove o PDF |
| `GET` | `/health` | — | Disponibilidade |

O livro-caixa aceita `?from=`, `?to=`, `?type=`, `?source=`, `?eventId=`,
`?withoutEvent=`, `?category=`, `?search=`, `?page=` e `?pageSize=`. Os totais
que ele devolve são **do filtro inteiro, não da página**: quem olha o mês quer o
saldo do mês, ainda que esteja vendo as cinquenta primeiras linhas.

Competência viaja como `aaaa-mm` (`?from=2026-01&to=2026-12`). Em branco, o
fechamento mensal devolve os doze meses que terminam no mês corrente.

Enums viajam como texto no JSON (`"Income"`, `"Expense"`, `"Open"`, `"Closed"`).

### Exemplo do caixa

```json
GET /api/reports/cash

{
  "totalIncome": 40800.00,
  "totalExpense": 26800.00,
  "balance": 14000.00,
  "eventCount": 5,
  "profitableEventCount": 3,
  "unprofitableEventCount": 2,
  "breakEvenEventCount": 0,
  "events": [
    {
      "eventId": "01a05ec7-...",
      "name": "Show de rock",
      "eventDate": "2026-07-10",
      "totalIncome": 12000.00,
      "totalExpense": 7000.00,
      "result": 5000.00,
      "isProfitable": true
    }
  ]
}
```

## Testes

| Projeto | Cobre |
|---|---|
| `FinFlower.Domain.Tests` | Resultado do evento, evento fechado, validação de lançamento, bloqueio de conta, ciclo do refresh token, divisão de parcelas sem perder centavos |
| `FinFlower.Application.Tests` | Casos de uso de autenticação, evento, contrato e caixa; liquidação ligando previsto e realizado; isolamento entre contas; hash de senha; tradução das consultas para SQL Server |
| `FinFlower.Api.Tests` | A aplicação real via HTTP: pipeline, JWT, validação, cabeçalhos, limite de requisições, CORS, rotas de evento e contrato, o upload de PDF, e os relatórios exportados abertos e conferidos célula a célula |

Os testes de API sobem o mesmo `Program.cs` de produção, trocando apenas o SQL
Server por um banco em memória. Como o provedor em memória aceita qualquer LINQ,
`SqlTranslationTests` monta as consultas contra o provedor real do SQL Server só
para garantir que todas viram SQL de verdade.

## Padrão de código

`Directory.Build.props` e `.editorconfig` valem para toda a solution:
nullable habilitado, **warnings tratados como erro**, analisadores no nível
`latest-recommended` e estilo verificado no build. Migrations, por serem código
gerado, ficam fora das regras de estilo.
