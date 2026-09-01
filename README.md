# Fin Flower API

Back-end do Fin Flower: controle financeiro **por eventos**. Cada evento reúne
seus lançamentos de entrada e saída, e o resultado de cada evento alimenta o
caixa consolidado.

```
Evento (Festa de Ano Novo, 12/12)
  ├── Entrada: Venda de ingressos    R$ 8.000
  ├── Saída:   Aluguel do espaço     R$ 3.000
  └── Saída:   Buffet                R$ 2.500
        → Resultado: +R$ 2.500 (lucro)

Caixa geral = soma do resultado de todos os eventos
```

.NET 10 (LTS), SQL Server, EF Core.

## Estado atual

Esta etapa entrega a fundação: estrutura em camadas, modelo de domínio completo
(usuário, evento, lançamento) e o fluxo de autenticação de ponta a ponta.

| Área | Situação |
|---|---|
| Solution em 4 camadas + testes | pronto |
| Domínio de evento e lançamento | pronto (regras e testes) |
| Migration inicial (SQL Server) | pronto |
| Autenticação: registro, login, refresh, logout, `/me` | pronto |
| Endpoints de evento, lançamento e relatório de caixa | próxima etapa |

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
- **Nome de código em inglês, mensagens ao usuário em português.** O front segue a
  mesma divisão.

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
| Entrada | FluentValidation antes de qualquer regra de negócio, erro por campo em `ProblemDetails` |
| Injeção de SQL | EF Core parametrizado; nenhuma query montada por concatenação |
| Vazamento por erro | 500 genérico ao cliente; detalhe e stack trace só no log |
| Cabeçalhos | CSP, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Cache-Control: no-store` |
| CORS | Lista explícita de origens, nunca `AllowAnyOrigin` |
| Segredos | `user-secrets` em dev, variável de ambiente em produção; validados na subida |
| Auditoria | `CreatedAt`/`UpdatedAt` automáticos e exclusão lógica em tudo |

## Como rodar

**1. Suba o SQL Server**

```bash
cp .env.example .env
docker compose up -d
```

**2. Configure os segredos** (não vão para o repositório)

```bash
cd src/FinFlower.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:SigningKey" "$(openssl rand -base64 48)"
```

A connection string de desenvolvimento já aponta para o container do compose. Em
produção, use variáveis de ambiente: `ConnectionStrings__Default` e `Jwt__SigningKey`.

**3. Aplique as migrations**

```bash
dotnet tool install --global dotnet-ef
dotnet ef database update --project src/FinFlower.Infrastructure --startup-project src/FinFlower.Api
```

**4. Rode**

```bash
dotnet run --project src/FinFlower.Api
```

Swagger em `/swagger` (somente em desenvolvimento).

## Comandos

```bash
dotnet build     # warnings são tratados como erro
dotnet test      # 64 testes
```

Nova migration:

```bash
dotnet ef migrations add NomeDaMigration \
  --project src/FinFlower.Infrastructure \
  --startup-project src/FinFlower.Api \
  --output-dir Persistence/Migrations
```

## Endpoints

| Método | Rota | Auth | Descrição |
|---|---|---|---|
| `POST` | `/api/auth/register` | — | Cria a conta e devolve a sessão |
| `POST` | `/api/auth/login` | — | Autentica por e-mail e senha |
| `POST` | `/api/auth/refresh` | — | Troca o refresh token por um novo par |
| `POST` | `/api/auth/logout` | — | Revoga o refresh token informado |
| `GET` | `/api/auth/me` | Bearer | Dados do usuário autenticado |
| `GET` | `/health` | — | Disponibilidade |

## Testes

| Projeto | Cobre |
|---|---|
| `FinFlower.Domain.Tests` | Resultado do evento, evento fechado, validação de lançamento, bloqueio de conta, ciclo do refresh token |
| `FinFlower.Application.Tests` | Casos de uso de autenticação sobre banco em memória, e o hash de senha |
| `FinFlower.Api.Tests` | A aplicação real via HTTP: pipeline, JWT, validação, cabeçalhos e limite de requisições |

Os testes de API sobem o mesmo `Program.cs` de produção, trocando apenas o SQL
Server por um banco em memória.

## Padrão de código

`Directory.Build.props` e `.editorconfig` valem para toda a solution:
nullable habilitado, **warnings tratados como erro**, analisadores no nível
`latest-recommended` e estilo verificado no build. Migrations, por serem código
gerado, ficam fora das regras de estilo.
