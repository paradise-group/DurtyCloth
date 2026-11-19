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
| `inspect_clothing_package` | `packageRoot` | Modelos, YTD/YLD, gênero, peças, impedimentos à importação e avisos de peso |
| `audit_clothing_assets` | `root`, `limit` opcional | Texturas e modelos fora do orçamento em qualquer pasta, com resolução, formato e MB |
| `inspect_clothing_resource` | `resourceRoot`, `search`, `offset`, `limit` | Contagens e página de peças com collection, slot, índice e UUID |
| `validate_clothing_resource` | `resourceRoot` | Diagnósticos estáticos de metadados e mapa de UUID |
| `prepare_clothing_update` | `resourceRoot`, `packageRoot`, `outputParent`, `gameFolder` e `appendToLatestCollection` opcionais | Cópia atualizada, projeto e relatório das adições |
| `export_clothing_photos` | `resourceRoot`, `itemIds`, `outputRoot`, `gameFolder` e `firstTextureOnly` opcionais | WebP 512 px da primeira textura por peça; todas as disponíveis com `firstTextureOnly=false`; caminhos UUID e manifesto SHA-256 |

Extraia `.rar`/`.zip` antes de passar a pasta. O MCP não executa arquivos do pacote.
Ele reconhece os nomes GTA e pastas `male`/`female`, `Masculino`/`Feminino` e `M`/`F`.
Pastas de corporações com nomes de arquivos iguais recebem UUIDs distintos e
repetíveis pelo caminho relativo no pacote. Mantenha essa organização entre
inspeção e construção. Antes de criar outro drawable, a atualização compara o
YDD principal, modelos alternativos e física por SHA-256, no mesmo gênero e slot.
Um modelo idêntico só é reaproveitado se comportar **todas** as variantes do lote,
considerando também os lotes anteriores dessa atualização.
Os modelos novos que já foram planejados na mesma remessa também entram nessa
busca; a capacidade deles é reservada antes de considerar o próximo lote.
A comparação distingue o comportamento nativo dos sufixos `_u` e `_r`.
Na exportação, o nome externo das texturas acompanha o modo do modelo e os
metadados nativos: `_u` usa `_uni.ytd` e `_r` usa `_whi.ytd`. Um sufixo
inconsistente no YTD de entrada é normalizado, tanto em drawables novos quanto
nas texturas acrescentadas a um modelo idêntico. O conteúdo do YTD é preservado.
`reuseExistingModels=false` em `prepare_clothing_update` força drawables separados
somente para o pacote dessa chamada. Para impedir apenas alguns destinos, use
`excludedReuseTargetIds` com os UUIDs desses drawables: a busca continua nos demais
modelos compatíveis e nos drawables novos da mesma remessa, respeitando A–Z.
Os parâmetros não mudam o comportamento das chamadas seguintes; o padrão de
reaproveitamento continua `true`. A preparação não altera nem
reorganiza roupas já publicadas. Uma migração explícita de texturas pode conservar
os IDs antigos indisponíveis com `redirectItemId`/`redirectTextureId`, apontando
diretamente para uma variante ativa do mesmo modelo de ped, tipo e slot. O adaptador
resolve a combinação antiga para o novo destino; o catálogo oculta a posição antiga.
São 26 posições, A–Z (índices 0–25), conforme a
[documentação do Durty](https://docs.gta.clothing/game-mechanics/drawable-types-and-textures).
Exemplo: 24 posições ocupadas e 10 texturas chegando geram um novo drawable com
as 10 juntas; o lote nunca é dividido para preencher duas vagas. A busca pode
usar outro drawable idêntico com espaço suficiente. Texturas novas entram depois
da última posição nativa, sem substituir letras existentes. O UUID do drawable
reaproveitado e todos os UUIDs anteriores permanecem iguais; cada textura nova
mantém o UUID que recebeu no pacote. Texturas iguais também contam no lote.
Metadados incompatíveis, props ou metadados de primeira pessoa, cabelo e salto
continuam exigindo drawable separado. Não há reorganização de peças publicadas.

Gênero indefinido, slot desconhecido, YTD ausente, mais de 26 variantes e assets
sem associação impedem a preparação. Os binários YDD/YTD/YLD são lidos antes de
criar a saída. Peças sem YTD precisam de um pacote com as dependências resolvidas.

### Orçamento de assets

O padrão de textura é 1024×1024, ou 2048×2048 quando 1024 deixa um detalhe
ilegível; sempre comprimida (DXT1 opaca, DXT5 com alfa) e com mipmaps.
`inspect_clothing_package` e `audit_clothing_assets` medem cada textura pela
memória que ela ocupa no jogo, não pelo tamanho do arquivo em disco, e informam
resolução, formato, níveis de mipmap e MB:

| Situação | Resultado |
| --- | --- |
| Mais de 64 MB de memória | **Impedimento** (`errors`): não cabe em uma página de recurso do RAGE e derruba o cliente com `ERR_STR_FAILURE_3` |
| Maior que 2048 em um lado | Aviso (`warning`) |
| 16 MB ou mais de memória | Aviso (`warning`): o FiveM trata como asset superdimensionado |
| YDD/YLD com 16 MB ou mais em disco | Aviso (`warning`) |
| Sem compressão (A8R8G8B8 e similares) com 1 MB ou mais | Nota (`notice`) |
| Sem mipmaps com 512 ou mais | Nota (`notice`) |

Avisos e notas não impedem a preparação: aparecem em `warnings` na inspeção,
cada um com seu `severity`, e em `packageWarnings` no `update-report.json`. Os
avisos são o que quebra a otimização no jogo: informe-os ao usuário e corrija as
texturas antes de aplicar. As notas são melhorias menores. 2048 comprimida com
mipmaps não gera nada.
`audit_clothing_assets` percorre qualquer pasta (pacote extraído, `stream` ou o
recurso inteiro, o que pode levar minutos) e lista primeiro os casos críticos e
depois os mais pesados.

`outputParent` deve ser uma pasta **nova**, fora dos dois diretórios de entrada.
O exportador copia o recurso completo, confere SHA-256 e acrescenta as roupas
no mesmo recurso. Mantém UUIDs, coleções e índices nativos das peças existentes;
não oferece inserção antes de peças publicadas nem substituição por este MCP.
A cópia confere até quatro arquivos em paralelo, mantendo a verificação de cada
arquivo e o descarte da saída se qualquer cópia falhar.

Por padrão (`appendToLatestCollection` = `true`), os componentes entram **no fim**
da última coleção `pcs` de cada gênero — a última declarada em `data/pcs` no
`fxmanifest.lua` — depois do último índice nativo de cada slot. Nenhum índice
existente muda e nenhuma coleção é criada: o `.ymt` e o `_shop.meta` dessa coleção
são reescritos somente com os acréscimos, e um slot que a coleção ainda não tinha
é criado nela. Uma coleção nova por gênero só é aberta quando:

- o gênero ainda não tem coleção `pcs` no recurso;
- algum slot passaria de 128 drawables (`CollectionIndices.DrawablesPerSlot`); nesse
  caso todas as peças daquele gênero vão juntas para a coleção nova;
- a peça é um prop ou tem metadados de primeira pessoa, cabelo ou salto, que o
  acréscimo nativo não remapeia.

Com `appendToLatestCollection` = `false`, os drawables que não puderam ser
reaproveitados entram em coleção nova por gênero. Em coleções novas, categoria/slot não divide coleções;
componentes e props compartilham a mesma.
A repetição do mesmo pacote no recurso já atualizado é rejeitada pelos UUIDs.
Essa proteção inclui os UUIDs de texturas anexadas a um drawable reaproveitado.

A preparação pode copiar dezenas de GB. Configure um tempo de execução adequado
no cliente. São enviadas notificações MCP de progresso quando o cliente fornece
um token de progresso; logs usam stderr e o protocolo usa exclusivamente stdout.
Cancelamento é verificado na fila e antes da exportação. Depois que a cópia começa,
o exportador termina ou desfaz sua saída; não há cancelamento durante cada cópia.

A saída contém:

- `<nome-do-recurso>/`: recurso FiveM com arquivos originais e roupas novas.
- `update-report.json`: origem, contagens, verificações, problemas preexistentes,
  e cada roupa/textura adicionada com UUID, collection e índice local. No nível
  superior, `addedCollections` lista cada coleção nova e `appendedCollections`
  cada coleção existente que recebeu peças, como `{ "collection", "gender", "items" }`.
  `reusedDrawables` identifica os drawables reaproveitados e apenas suas novas
  texturas, com os índices efetivos. `addedItems` conta somente os novos 3Ds;
  `appendedCollections` também inclui coleções que receberam apenas texturas.
  `reusedIncomingItems` relaciona cada UUID de entrada reaproveitado ao drawable
  de destino e às novas texturas, inclusive quando o destino nasceu nessa remessa.
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

`export_clothing_photos` recebe uma lista explícita de UUIDs e fotografa, por padrão,
apenas a primeira textura disponível de cada peça, em ordem de índice nativo.
`firstTextureOnly=false` permite fotografar todas as variantes disponíveis.
A renderização usa o Studio em uma thread STA.
Posições indisponíveis ou migradas não são fotografadas; os índices e UUIDs das
variantes ativas permanecem iguais, sem compactar a numeração. A pasta
de saída deve ser nova e ficar fora do recurso. Chapéus, máscaras, cabelos e acessórios
faciais usam o ped da instalação GTA V configurada. As imagens usam WebP de 512 px,
qualidade 85 e o mesmo fundo cinza da exportação pela interface. O manifesto
`photo-manifest.json` registra peça, textura, endereço nativo, caminho e SHA-256.
Não registra fotos automaticamente no servidor nem publica na CDN.

## Antes de aplicar uma atualização preparada ao recurso do servidor

- Confira `packageWarnings`: nenhuma textura fora do orçamento de assets deve ser
  aplicada sem o usuário saber; o ideal é corrigir o pacote e preparar de novo.
- Confira `addedCollections`: vazio quando o gênero já tem coleção `pcs` com espaço;
  no máximo uma entrada por gênero do pacote nos casos listados acima.
- Confira se as quantidades de `.ymt` de coleção e `_shop.meta` adicionados
  correspondem à quantidade de entradas em `addedCollections`.
- Para cada entrada de `appendedCollections`, compare o `.ymt` e o `_shop.meta`
  com a origem: os drawables e itens existentes devem continuar idênticos, e os
  novos índices devem continuar a partir do último de cada slot.
  Em `reusedDrawables`, apenas `aTexData`, o total de texturas do slot e os novos
  registros de textura do shop são acrescentados; o YDD não muda.
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

`ResourceUpdateProbe --reuse-test <pasta-nova>` cobre lote inteiro, encaixe exato
até Z, falta de espaço, busca de outro modelo idêntico, reserva entre lotes,
diferenças de gênero/3D/metadados, importação no catálogo e preservação dos
registros originais (inclusive nomes, proveniência e ordem).
`ResourceUpdateProbe --reuse-real-fixture <pacote-real> <pasta-nova>` gera uma
origem pequena com os modelos reais e uma variante por peça para testar a
preparação MCP completa, sem alterar o recurso publicado.

O probe usa um cliente do SDK oficial para negociar o protocolo, descobrir as
seis ferramentas e verificar importação, UUIDs, paginação, erros de metadados,
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
