# DurtyCloth — Paradise Cloth Studio

Aplicativo Windows independente para organizar arquivos de roupas GTA V/FiveM.

## Estado atual

- Fluxo de interface baseado na gravação fornecida: menus Project/Options/View/Help,
  catálogo agrupado à esquerda, edição à direita, Settings e Build project no rodapé.
- Menus de contexto em peças e texturas: Remove, Replace, Replace with Dummy,
  copiar para outro gênero, renomear, copiar nomes/caminhos, abrir no Explorer,
  exportar arquivo/pacote/arquivo com texturas e XML/DDS/PNG. As ações disponíveis
  dependem da seleção; placeholders não oferecem exportação de arquivo ausente.
  Labels de textura são salvos e acompanham a reordenação, sem renomear os assets.
- Options > Add tattoo / Edit tattoos abre o editor local: PNG/DDS/YTD, gênero,
  zona, UV, escala, rotação, custo, substituição de imagem e prévia da textura.
  Import tattoo resource lê overlays XML e YTD existentes. Tatuagens são salvas
  com seus assets em `.fcsproj`/`.fcsbundle` e exportadas nos quatro destinos.
- Build Project > Show GTA DLC replace options gera uma coleção substituta com
  o recurso e nomes masculinos/femininos informados, em nova pasta. Substitui a
  coleção completa; não instala os arquivos nem modifica o DLC original.
  DLC load order alimenta setup2.xml em single player/RageMP.
- Tattoo info dump e configs opcionais de illenium-appearance, ESX-TattooShop
  (esx-community) e Menyoo usam os hashes dos overlays gerados. São arquivos para
  mesclar manualmente; o aplicativo não conecta nem altera scripts de servidor.
- Janelas Project Settings, Build Project, Error List e Add duplicate texture;
  abas General, Shop Meta e Creator Info. Configurações persistem no projeto.
- Autor, versão e descrição são escritos no manifesto; o build selecionado vira
  dependência de gameBuild. Custo, label e lockHash alimentam shop.meta. Flags
  numéricas e áudio são gravados no YMT. Creator Info vai para o relatório opcional.
- Construção FiveM com pasta configurável, subpastas por tipo, relatório JSON
  e opção para omitir as regras extras de variações alternativas.
- Build Project preserva alvo e opções no projeto após uma construção concluída.
  Remove default added prefix altera os nomes internos da coleção; Disable extra
  YMT files creation omite creature metadata e os modificadores dependentes de
  cabelo/salto, mantendo o YMT da coleção e os registros de primeira pessoa.
  Seletores editáveis de áudio, flags e TimeCycle mantêm valores importados.
- Confirmação de duplicatas pelo conteúdo das texturas, com escolha individual
  ou para o lote. Posição de exportação é independente do índice original.
- Placeholders de drawable e textura reservam posições no YMT sem criar arquivos
  visuais. Projetos e pacotes preservam essas reservas; a opção de substituir
  removidos por dummy e de preencher dummies na importação é funcional.
- Chapéus: Show/Cut/Remove/Custom cut hairs, com modificador de expressão no YMT
  e geração do creature metadata necessário. Heel Height usa o mesmo sistema
  para sapatos. Regras de cabelo/hoodie em componentes apontam para modelos
  alternativos de cabelo importados no projeto; não criam geometrias novas.
- Primeira pessoa: YDD alternativo registrado, Mask Model com offsets e
  TimeCycle/Intensity por textura. A geração inclui first_person.meta e
  first_person_alternates.meta com registros nos manifestos.
- Exportadores FiveM, alt:V (resource.toml/stream.toml), single player (dlc.rpf)
  e RageMP (client_packages/game_resources/dlcpacks). RPFs gerados são reabertos
  e seus arquivos conferidos; a exportação não instala recursos em servidores.

- Interface de catálogo por tipo, lista de peças e painel de detalhes.
- Leitura da estrutura dos YDD: quantidade de drawables, modelos e geometrias.
- Prévia 3D dos YDD com rotação e zoom (até 25 mil triângulos amostrados
  proporcionalmente entre as geometrias), aplicando texturas do YTD quando o
  nome coincide com o shader e há UVs.
- Lista dos nomes de textura pedidos pelo modelo na prévia, indicando quais
  estão presentes no YTD selecionado e quais faltam.
- Prévia 2D com seleção das texturas contidas em cada YTD.
- Exportação da textura selecionada como PNG em resolução original.
- Exportação da textura completa como DDS, incluindo todos os mipmaps, com
  verificação por releitura; adição/substituição de texturas internas
  usando PNG ou DDS; PNG é convertido para BGRA32 sem compressão e recebe uma
  cadeia completa de mipmaps. O PNG precisa ter dimensões em potência de 2,
  até 4096 px; o YTD resultante pode ficar maior que um arquivo com DDS
  comprimido. Também permite remover ou renomear uma textura interna. A
  renomeação atualiza o hash do nome sem alterar os pixels e rejeita duplicatas.
  Cada edição cria um novo YTD, preserva o original, relê o resultado
  e confere os dados das demais texturas. O projeto passa a usar o novo arquivo
  após a edição; salve o projeto para manter a referência.
- Importação de arquivos e pastas com YDD, YTD e YLD binários.
- Reimportar o YDD de uma peça já presente acrescenta variantes YTD, modelos
  alternativos e YLD que ainda faltam, sem duplicar a peça ou os caminhos já
  associados. A importação confere o limite de 26 variantes antes de alterar
  o projeto.
- Associação de YTD por coleção, gênero, tipo e índice; YLD pelo nome do
  modelo, aceitando o nome físico sem o sufixo `_u` ou `_r` do YDD. Ao importar
  um YDD isolado, procura também os YDD alternativos
  `_1` a `_3` da mesma pasta. Não descarta YTD apenas pelo sufixo `_uni` ou
  `_whi`, pois ambos podem pertencer à mesma peça.
- Edição das variantes YTD da peça selecionada: adicionar, substituir,
  remover e mudar a ordem. A ordem define as letras `a` a `z` na exportação.
- Substituição do YDD principal, gestão de até três YDD alternativos e
  associação ou remoção do arquivo de física YLD por peça. O YDD selecionado
  é lido antes de entrar no projeto; o YLD é lido como arquivo de física e o
  painel mostra sua quantidade de entradas de tecido.
- Importação de arquivos contendo apenas YTD adiciona as variantes à peça
  selecionada. O limite do addon é 26 variantes por peça.
- Ajuste manual de nome, gênero e categoria.
- Projeto portátil `.fcsproj` em JSON com cópias dos arquivos em `assets/`;
  comandos Salvar e Salvar como preservam as alterações de texturas.
- Importação e exportação de pacote único `.fcsbundle`, contendo o projeto e
  todos os YDD/YTD/YLD referenciados; a importação verifica os caminhos e
  extrai para uma pasta de projeto nova.
- Importação e exportação da peça selecionada em `.fcsitem`, com seus modelos,
  variantes YTD e física; a importação adiciona a peça ao projeto aberto.
- Exportação direta dos YDD/YTD/YLD da peça para uma pasta nova. Usa os nomes
  originais e renumera as letras das variantes YTD após edição da ordem;
  compara SHA-256 de cada cópia com o arquivo de origem. Se uma cópia ou
  verificação falhar, remove a pasta incompleta.
- Importação somente leitura de `.dctproj` MessagePack/LZ4 da versão local
  3.2.0.5; localiza cada arquivo tanto na pasta de dados ao lado do projeto
  quanto em `TargetBuildOutputPath/data`, inclusive quando os dois diretórios
  contêm partes diferentes dos arquivos. Ao salvar, cria um `.fcsproj`
  independente.
- Validação de arquivos ausentes e nomes duplicados.
- Validação estática de recursos FiveM abertos: manifesto, leitura do YMT,
  índices de componentes/props em `shop.meta`, contador de texturas e presença
  dos YDD/YTD e modelos alternativos declarados no `stream`; também confere o
  YLD de cada drawable cujo YMT declara `ownsCloth=true`, incluindo a leitura
  do arquivo e a presença de entradas de tecido.
- Exportação de `stream/` e `fxmanifest.lua` com nomes GTA originais mesmo após
  salvar o projeto; aplica a ordem atual das variantes YTD, detecta colisões
  antes de criar a saída, verifica cada cópia por SHA-256 e remove a pasta se
  a exportação falhar. Este modo não gera
  YMT ou `shop.meta`.
- Abertura de recurso FiveM já construído e cópia integral dos arquivos com
  verificação SHA-256, incluindo metadados e manifesto existentes.
- Geração de addon FiveM para componentes de roupa e seis
  âncoras de props, com YMT binário, `shop.meta`, `stream/` e `fxmanifest.lua`.

## Limites desta versão

Collections publicadas podem ter seus índices protegidos em **Project Settings > General**.
Veja [o fluxo de publicação](docs/COLLECTIONS.md): snapshot persistido, adição no final,
remoção com dummy e `collection-index.json` em cada build de addon.

A saída de addon foi comparada estaticamente com um recurso real, mas ainda
precisa de teste no FXServer. A prévia 3D ainda não reproduz todos os materiais
e efeitos do jogo. PNG gera mipmaps automaticamente; DDS mantém os mipmaps do
arquivo importado. Faltam edição de materiais,
importação de `.dctexport`/`.dctpackage` e exportação nos formatos próprios
do Durty Cloth Tool. Esta versão não afirma paridade funcional com o
aplicativo de referência.

A reorganização visual ainda não é uma cópia exata de todas as janelas e opções
do vídeo. Behaviour Flags usa valor numérico e ainda não
tem a lista de nomes da referência. Alguns diagnósticos continuam em português.
Os novos efeitos e exportadores passaram por verificação estrutural; ainda não
foram testados nos clientes GTA V/FiveM/alt:V/RageMP. A prévia usa componentes base do ped extraídos da instalação local do GTA V; não simula os efeitos de câmera do jogo nem a deformação do cabelo. Máscaras e hoodie
exigem cabelo importado com os alternativos adequados: Cut usa _1, Hoodie usa
_2 e Remove usa _3; o gerador rejeita a ausência desses modelos. Para chapéus,
o cabelo usado em jogo precisa suportar o controle MH_Hair_Scale.

## Executar

O aplicativo também oferece um [MCP local para roupas](docs/MCP.md): inspeção de
pacotes/recursos, validação, preparação de uma cópia atualizada do recurso FiveM
e prévias WebP por UUID. Mantém os índices e UUIDs existentes. Veja o guia para
compilar e conectar.

```powershell
dotnet run --project D:\FooxClothStudio\FooxClothStudio.csproj
```

Também é possível passar o caminho de um `.fcsproj` ou `.dctproj` como
argumento para abrir o projeto na inicialização.

O projeto usa .NET 8 WPF e não exige conta ou autenticação.

## Atualização de 20/10/2026

- Chapéus usam a transformação SKEL_Head do ped, preservando os modelos de origem.
- Fundo de estúdio com degradê azul-escuro nas duas prévias.
- Ped reto e centralizado por padrão: arraste horizontal gira e a roda mantém o zoom.
- A caixa **Destravar ped**, ao lado da prévia, libera inclinação e deslocamento.
  Desmarcar restaura o alinhamento; a prévia embutida também bloqueia inclinação.
- Salvamento mostra progresso real por peça; resumo informa peças, variantes de textura,
  slots reservados e tatuagens. Build mostra totais e confirmação após sucesso.

## Compilar em outro computador

Requisitos: Windows, SDK .NET 8 e as dependências locais compatíveis `CodeWalker.Core.dll`,
`SharpDX.dll` e `SharpDX.Mathematics.dll`. Coloque-as em `deps/CodeWalker/`, ou informe
`-p:CodeWalkerDir="C:/caminho/das/dependencias"` nos comandos dotnet.
Consulte [THIRD_PARTY.md](THIRD_PARTY.md) para origem e avisos.
Os binários de terceiros e os arquivos do GTA não são versionados.

```powershell
dotnet restore FooxClothStudio.csproj
dotnet build FooxClothStudio.csproj -c Release
dotnet publish FooxClothStudio.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

O repositório se chama DurtyCloth; o aplicativo continua com o nome Paradise Cloth Studio.
É uma implementação independente, sem código do Durty Cloth Tool.
Consulte [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md) para processo e testes,
e [COMPATIBILITY.md](COMPATIBILITY.md) para o histórico técnico e os limites.

- Revisão de duplicadas no rodapé: prévias da existente e da nova, slots/arquivos e remoção individual do projeto, preservando originais.

- Project > Recent files persiste os 5 últimos projetos salvos/abertos e permite reabrir com um clique.
