# Orientações para agentes

Paradise Cloth Studio: aplicativo Windows em C#/.NET 8 WPF, com MCP local em
`tools/ClothMcp`. Leia [README.md](README.md), [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)
e [docs/MCP.md](docs/MCP.md) antes de alterar esse fluxo.

## Atualizações de recursos

- Siga os invariantes de agrupamento, capacidade e preservação de identidades e
  o checklist antes de aplicar em [docs/MCP.md](docs/MCP.md#antes-de-aplicar-uma-atualização-preparada-ao-recurso-do-servidor).
- Informe ao usuário a quantidade de coleções criadas antes de aplicar, comitar
  ou fazer push. Quantidade inesperada deve ser corrigida na ferramenta.
- Nunca comite nem faça push de uma atualização de recurso que não foi solicitada.
- Após alterar `ResourceUpdateExporter.cs`, execute os probes abaixo e o teste MCP
  completo `--prepare` documentado em `docs/MCP.md`, com saída nova e assets reais.
  Confira `addedCollections`, preservação das entradas originais e diagnósticos.

```powershell
dotnet run --project tools/ResourceUpdateProbe/ResourceUpdateProbe.csproj -c Release
dotnet run --project tools/CollectionIndicesProbe/CollectionIndicesProbe.csproj -c Release
dotnet run --project tools/StableClothingIdsProbe/StableClothingIdsProbe.csproj -c Release
dotnet run --project tools/ClothMcpProbe/ClothMcpProbe.csproj -c Release -- D:/Git/DurtyCloth/publish-mcp/ParadiseClothMcp.exe
```

Recompile/publique o MCP antes de testar o executável; comandos em `docs/MCP.md`.
Os probes estáticos não substituem teste de carregamento e aparência no FiveM.
