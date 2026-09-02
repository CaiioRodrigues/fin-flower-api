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
| Domínio de evento e lançamento | pronto |
| Migration inicial (SQL Server) | pronto |
| Autenticação: registro, login, refresh, logout, `/me` | pronto |
| Eventos e lançamentos (CRUD, fechar/reabrir) | pronto |
| Relatório de caixa consolidado | pronto |
| Contratos parcelados, com PDF anexado | pronto |
| Fluxo de caixa: vencidos, mês corrente e previsão | pronto |
| Front-end para contratos e fluxo de caixa | próxima etapa |

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
| Segredos | `user-secrets` em dev, variável de ambiente em produção; validados na subida |
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

Ela não vai para o repositório. No Visual Studio: clique com o botão direito no
projeto `FinFlower.Api` → **Gerenciar Segredos do Usuário**, e cole:

```json
{
  "Jwt": {
    "SigningKey": "troque-por-uma-chave-longa-e-aleatoria-de-32-caracteres-ou-mais"
  }
}
```

Sem isso a aplicação nem sobe: a chave é validada na inicialização.

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
dotnet test      # 153 testes
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
| `POST` | `/api/events/{id}/entries` | Bearer | Cadastra um lançamento no evento |
| `PUT` | `/api/events/{id}/entries/{entryId}` | Bearer | Altera um lançamento |
| `DELETE` | `/api/events/{id}/entries/{entryId}` | Bearer | Remove um lançamento |
| `GET` | `/api/reports/cash` | Bearer | Caixa consolidado (realizado) |
| `GET` | `/api/reports/cash-flow` | Bearer | Fluxo de caixa: vencidos, mês corrente e previsão |
| `POST` | `/api/events/{id}/contracts` | Bearer | Cria um contrato com as parcelas geradas |
| `GET` | `/api/contracts` | Bearer | Lista contratos, com o quanto já foi liquidado |
| `GET` `PUT` `DELETE` | `/api/contracts/{id}` | Bearer | Abre, altera e exclui |
| `POST` | `/api/contracts/{id}/installments/{n}/settle` | Bearer | Liquida e gera o lançamento |
| `POST` | `/api/contracts/{id}/installments/{n}/unsettle` | Bearer | Estorna e remove o lançamento |
| `POST` | `/api/contracts/{id}/installments/{n}/cancel` | Bearer | Cancela a parcela |
| `PUT` | `/api/contracts/{id}/installments/{n}/due-date` | Bearer | Altera o vencimento |
| `PUT` | `/api/contracts/{id}/installments/{n}/amount` | Bearer | Altera o valor, redistribuindo a diferença |
| `POST` `GET` `DELETE` | `/api/contracts/{id}/document` | Bearer | Anexa, baixa e remove o PDF |
| `GET` | `/health` | — | Disponibilidade |

A listagem aceita `?from=`, `?to=` e `?status=Open|Closed`; o caixa aceita `?from=` e `?to=`.
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
| `FinFlower.Api.Tests` | A aplicação real via HTTP: pipeline, JWT, validação, cabeçalhos, limite de requisições, CORS, rotas de evento e contrato, e o upload de PDF |

Os testes de API sobem o mesmo `Program.cs` de produção, trocando apenas o SQL
Server por um banco em memória. Como o provedor em memória aceita qualquer LINQ,
`SqlTranslationTests` monta as consultas contra o provedor real do SQL Server só
para garantir que todas viram SQL de verdade.

## Padrão de código

`Directory.Build.props` e `.editorconfig` valem para toda a solution:
nullable habilitado, **warnings tratados como erro**, analisadores no nível
`latest-recommended` e estilo verificado no build. Migrations, por serem código
gerado, ficam fora das regras de estilo.
