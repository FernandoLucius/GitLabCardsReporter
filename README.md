# GitLabCardsReporter

Projeto console em .NET 8 para consultar, por projeto do GitLab, os dados dos cards/issues e exportar um CSV com:

- número da issue (`iid`)
- título
- labels
- tempo gasto (`time_stats.total_time_spent` e `human_total_time_spent`)
- status
- URL da issue

## Requisitos

- .NET 8 SDK
- token de acesso do GitLab com permissão de leitura no projeto/API

## Como configurar

Você pode configurar de 3 formas:

### 1. Linha de comando

```bash
dotnet run --project src/GitLabCardsReporter/GitLabCardsReporter.csproj -- \
  --token glpat-xxxx \
  --project meu-grupo/meu-projeto \
  --base-url https://gitlab.com \
  --include-closed true \
  --per-page 100 \
  --output cards-report.csv
```

### 2. appsettings.json

Edite `src/GitLabCardsReporter/appsettings.json`:

```json
{
  "GitLab": {
    "BaseUrl": "https://gitlab.com",
    "PrivateToken": "glpat-xxxx",
    "Project": "meu-grupo/meu-projeto",
    "IncludeClosed": true,
    "PerPage": 100,
    "OutputCsvPath": "cards-report.csv"
  }
}
```

### 3. Variáveis de ambiente

```powershell
$env:GITLAB_REPORTER_GitLab__PrivateToken = "glpat-xxxx"
$env:GITLAB_REPORTER_GitLab__Project = "meu-grupo/meu-projeto"
```

## Como executar

### Pelo terminal

```bash
dotnet restore GitLabCardsReporter.sln
dotnet build GitLabCardsReporter.sln
dotnet run --project src/GitLabCardsReporter/GitLabCardsReporter.csproj -- --token glpat-xxxx --project meu-grupo/meu-projeto
```

### Pelo Visual Studio

1. Abra o arquivo `GitLabCardsReporter.sln`
2. Defina `GitLabCardsReporter` como projeto de inicialização
3. Configure os argumentos da aplicação, por exemplo:

```text
--token glpat-xxxx --project meu-grupo/meu-projeto --output cards-report.csv
```

## Observações

- O parâmetro `--project` aceita tanto o ID numérico do projeto quanto o caminho, por exemplo `grupo/subgrupo/projeto`.
- A aplicação pagina automaticamente até buscar todas as issues.
- No contexto de boards do GitLab, os cards exibidos no board são issues do projeto. Por isso o relatório é montado via endpoint de issues.
- O arquivo CSV é gravado em UTF-8 com BOM para abrir melhor no Excel.

## Estrutura

```text
GitLabCardsReporter.sln
src/
  GitLabCardsReporter/
    GitLabCardsReporter.csproj
    Program.cs
    appsettings.json
```
