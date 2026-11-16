# MCP de roupas

O `ParadiseClothMcp` oferece as operações do Paradise Cloth Studio por MCP local
(stdio), usando o [SDK oficial C#](https://github.com/modelcontextprotocol/csharp-sdk).
Ele chama os importadores, mapas de UUID e exportadores deste repositório.
É um processo separado da janela WPF: trabalha nos arquivos em disco e não altera
o projeto que estiver aberto na interface. Windows é necessário.

## Compilar e conectar

```powershell
dotnet publish tools/ClothMcp/ClothMcp.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish-mcp
```

As dependências CodeWalker são as mesmas do aplicativo, descritas em
[DEVELOPMENT.md](DEVELOPMENT.md). O executável publicado inclui o runtime .NET.

No cliente MCP, cadastre um servidor **stdio** com o comando absoluto
`D:/Git/DurtyCloth/publish-mcp/ParadiseClothMcp.exe`, sem argumentos. Adapte o caminho
se mover a instalação. [cloth-mcp.example.json](cloth-mcp.example.json) contém um
exemplo para clientes que usam a chave `mcpServers`; cada cliente tem seu próprio
local de configuração. A publicação não registra automaticamente o servidor no
cliente de IA. Reinicie/reconecte esse cliente depois de cadastrá-lo.

Para desenvolvimento, também é possível usar `dotnet` como comando e o caminho
absoluto de `tools/ClothMcp/bin/Release/net8.0-windows/ParadiseClothMcp.dll` como
único argumento, após `dotnet build tools/ClothMcp/ClothMcp.csproj -c Release`.
Evite `dotnet run` na configuração stdio, pois mensagens de compilação usam stdout.

## Operações

| Ferramenta | Argumentos principais | Resultado |
| --- | --- | --- |
| `inspect_clothing_package` | `packageRoot` | Modelos, YTD/YLD, gênero, peças e impedimentos à importação |
| `inspect_clothing_resource` | `resourceRoot`, `search`, `offset`, `limit` | Contagens e página de peças com collection, slot, índice e UUID |
| `validate_clothing_resource` | `resourceRoot` | Diagnósticos estáticos de metadados e mapa de UUID |
| `prepare_clothing_update` | `resourceRoot`, `packageRoot`, `outputParent`, `gameFolder` opcional | Cópia atualizada, projeto e relatório das adições |
| `export_clothing_photos` | `resourceRoot`, `itemIds`, `outputRoot`, `gameFolder` opcional | WebP 512 px por variante, com caminhos UUID para nations3 e manifesto SHA-256 |

Extraia `.rar`/`.zip` antes de passar a pasta. O MCP não executa arquivos do pacote.
Ele reconhece os nomes GTA e pastas `male`/`female`, `Masculino`/`Feminino` e `M`/`F`.
Pastas de corporações com nomes de arquivos iguais recebem UUIDs distintos e
repetíveis pelo caminho relativo no pacote. Mantenha essa organização entre
inspeção e construção. Não há deduplicação entre corporações: cada YDD principal
e todas as suas variantes continuam sendo uma peça separada.

Gênero indefinido, slot desconhecido, YTD ausente, mais de 26 variantes e assets
sem associação impedem a preparação. Os binários YDD/YTD/YLD são lidos antes de
criar a saída. Peças sem YTD precisam de um pacote com as dependências resolvidas.

`outputParent` deve ser uma pasta **nova**, fora dos dois diretórios de entrada.
O exportador copia o recurso completo, confere SHA-256 e acrescenta coleções novas
no mesmo recurso. Mantém UUIDs, coleções e índices nativos das peças existentes;
não oferece inserção antes de peças publicadas nem substituição por este MCP.
Peças sem prefixo de coleção de origem antes de `^` são agrupadas apenas por
gênero: uma coleção nova por gênero por atualização. Categoria/slot não divide
coleções; componentes e props compartilham a mesma. Uma divisão adicional só
ocorre ao exceder 128 drawables em algum slot (`CollectionIndices.DrawablesPerSlot`).
A repetição do mesmo pacote no recurso já atualizado é rejeitada pelos UUIDs.

A preparação pode copiar dezenas de GB. Configure um tempo de execução adequado
no cliente. São enviadas notificações MCP de progresso quando o cliente fornece
um token de progresso; logs usam stderr e o protocolo usa exclusivamente stdout.
Cancelamento é verificado na fila e antes da exportação. Depois que a cópia começa,
o exportador termina ou desfaz sua saída; não há cancelamento durante cada cópia.

A saída contém:

- `<nome-do-recurso>/`: recurso FiveM com arquivos originais e roupas novas.
- `update-report.json`: origem, contagens, verificações, problemas preexistentes,
  e cada roupa/textura adicionada com UUID, collection e índice local. O array
  `addedCollections`, no nível superior, lista cada coleção nova como
  `{ "collection", "gender", "items" }`.
- `update.fcsproj`: projeto para abrir no Studio, com referências absolutas aos
  assets originais e ao pacote extraído. Salve pela interface para copiar os assets
  para um projeto portátil antes de mover ou apagar essas pastas.

O projeto preserva as roupas do catálogo; tatuagens e assets de aparência do recurso
são mantidos pela cópia integral, sem serem importados para edição no `.fcsproj`.

## Validação e aplicação no servidor

O validador do MCP aceita metadados em subpastas. Confere declarações literais
`data_file`, leitura dos YMTs, referência de cada shop ao seu YMT, unicidade dos
UUIDs e presença dos arquivos mapeados. Declarações com glob exigem revisão manual.
É uma verificação estática parcial; não executa o manifesto Lua nem comprova a
aparência ou o carregamento no FXServer. O exportador também aplica suas próprias
validações aos addons novos. A preparação recusa problemas novos e registra os
problemas que já existiam na origem.

Este MCP prepara arquivos locais. Instalar a atualização, reiniciar o recurso e
cadastrar combinações/permissões no vestiário da polícia são operações separadas.
Use `additions` no relatório para obter os endereços corretos; não estime índices
globais do servidor. Faça o teste no jogo antes de publicar.

`export_clothing_photos` recebe uma lista explícita de UUIDs e fotografa todas as
variantes dessas peças usando o renderizador do Studio em uma thread STA. A pasta
de saída deve ser nova e ficar fora do recurso. Máscaras, cabelos e acessórios
faciais usam o ped da instalação GTA V configurada. As imagens usam WebP de 512 px,
qualidade 85 e o mesmo fundo cinza da exportação pela interface. O manifesto
`photo-manifest.json` registra peça, textura, endereço nativo, caminho e SHA-256.
Não registra fotos automaticamente no servidor nem publica na CDN.

## Antes de aplicar uma atualização preparada ao recurso do servidor

- Confira se `addedCollections` tem tantas entradas quanto gêneros no pacote;
  mais coleções só são válidas quando algum slot excede 128 drawables.
- Confira se as quantidades de `.ymt` de coleção e `_shop.meta` adicionados
  correspondem à quantidade de entradas em `addedCollections`.
- Confira se `files { 'data/pcs/*' }` aparece uma única vez no manifesto.
- Compare com a origem: quantidade de entradas originais, coleções, índices e
  UUIDs de peças/texturas devem ser preservados.
- Execute `validate_clothing_resource` na origem e na saída: nenhum diagnóstico novo.

Mais coleções que o previsto é um bug a corrigir na ferramenta antes de aplicar,
comitar ou publicar. Informe ao usuário a quantidade de coleções criadas antes
dessas operações; preparação não autoriza commit nem push.

UUIDs de peças e texturas independem da coleção. `collection` e `index` só podem
mudar antes do uso da peça no jogo; após uso em roupas salvas ou presets de
vestiário, ficam congelados. Fotos em `D:/Git/web-assets` usam
`<itemId>.<textureId>`; o `clothingAddresses` de
`D:/Git/high/resources/vrp/config/AppearanceImages.json` usa
`gender|kind|slot|collection|index|texture` e precisa acompanhar qualquer mudança
de coleção/índice antes desse uso. Publique na ordem `web-assets` →
`high-clothes-s3` → `high`.

## Verificar o MCP

```powershell
dotnet run --project tools/ClothMcpProbe/ClothMcpProbe.csproj -c Release -- D:/Git/DurtyCloth/publish-mcp/ParadiseClothMcp.exe
```

O probe usa um cliente do SDK oficial para negociar o protocolo, descobrir as
cinco ferramentas e verificar importação, UUIDs, paginação, erros de metadados,
mensagens de erro e proteção das entradas/saídas com fixtures temporárias.
Também confere seleções inválidas de fotos e limpeza de uma renderização com falha.

Teste completo com assets reais, gerando uma pasta nova:

```powershell
dotnet run --project tools/ClothMcpProbe/ClothMcpProbe.csproj -c Release -- D:/Git/DurtyCloth/publish-mcp/ParadiseClothMcp.exe --prepare 'D:/Git/high/resources/[addons]/high-clothes-s3' D:/Git/DurtyCloth/work/police-high/extracted D:/Git/DurtyCloth/outputs/police-high-mcp
```

O teste completo chama inspeção, preparação e validação pelo MCP. Não execute
novamente com a mesma pasta de saída: use outra pasta nova.

## Primeiro caso: uniformes HIGH

O pacote `ROUPAS_POLICIA_HIGH.rar` fornecido em 16/11/2026 foi extraído em
`work/police-high/extracted`. A preparação pelo MCP gerou
`outputs/police-high-mcp/high-clothes-s3`, preservando as 5.069 peças originais
e acrescentando 38 peças (19 por gênero), com os 416 arquivos YTD do pacote.
O relatório completo está em `outputs/police-high-mcp/update-report.json`.

Na primeira aplicação, foram criadas 17 coleções: a chave de agrupamento usava
`(Gender, Category, nome antes de '^')` e, sem `^`, o próprio nome do arquivo
abria outra coleção. Com o agrupamento corrigido, a atualização foi refeita a
partir do recurso anterior à primeira aplicação e gerou duas coleções, uma por
gênero, com 19 peças cada (`outputs/police-high-mcp-v2`). Os 38 UUIDs de peças
e os 416 de texturas ficaram iguais aos da primeira aplicação, com os mesmos
arquivos; mudaram apenas `collection`, `index` e os nomes nativos, e as 416
chaves de `clothingAddresses` foram reescritas para os novos endereços.

A validação da primeira preparação inspecionou 28 YMTs, 25 shops e 5.107 UUIDs de peças. Três YMTs
`mp_creaturemetadata_*` da origem usam uma compressão que o leitor local não
conseguiu decodificar; os mesmos três diagnósticos aparecem na cópia, sem
diagnósticos novos. Isso não demonstra que esses arquivos estejam defeituosos.
Os bytes originais foram preservados. O recurso de origem permaneceu inalterado.
Ainda é necessário testar os uniformes no jogo e configurar o vestiário desejado.

Na etapa seguinte, a atualização foi aplicada em
`D:/Git/high/resources/[addons]/high-clothes-s3`, mantendo integralmente as entradas
originais do mapa e copiando apenas `stream/pcs`, `data/pcs`, manifesto e mapa de IDs.
O backup local está em `work/police-high/resource-backup`.
As 416 prévias foram geradas pelo MCP em `outputs/police-high-photos` e copiadas para
`D:/Git/web-assets/game/images/nations3/nation_skinshop/clothes`. O manifesto do
servidor `D:/Git/high/resources/vrp/config/AppearanceImages.json` recebeu as 416 fotos
e seus endereços nativos. As imagens antigas e os mapas de barbearia e tatuagens
foram preservados. Essas alterações são locais, para commit e publicação posterior.
