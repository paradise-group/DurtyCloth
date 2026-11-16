# Desenvolvimento e verificação

Este repositório reúne o código fonte atual, ferramentas de verificação e o histórico
registrado em COMPATIBILITY.md. Como a pasta original não tinha Git, o primeiro commit
é um retrato do trabalho acumulado, e não uma reconstrução de commits antigos.

## Organização

- MainWindow: catálogo, edição, importação, salvamento e comandos de exportação.
- ModelPreview, GamePedAssets, ClothPreviewWindow e PreviewCameraMotion: prévia e ped.
- AddonExporter, PlatformExporter e exportadores específicos: pacotes GTA/FiveM/alt:V/RageMP.
- ProjectBundle: projetos portáteis; DctProjectImporter: importação de projetos existentes.
- tools/*Probe: testes executáveis de integração e inspeção.

## Validação da atualização

- `HatPlacementProbe/BoneProbe.csproj`: seis verificações de encaixe, rigidez e preservação
  da geometria com chapéus reais e esqueletos masculino/feminino locais.
- `ClothPreviewUiProbe`: YDD/YTD reais, troca de textura, renderização, reabertura,
  câmera interpolada, modo travado, checkbox de desbloqueio e realinhamento.
- `OperationFeedbackProbe`: salvamento real, progresso com contagem de peças sem incluir
  dummy como roupa, roundtrip de assets, fechamento do progresso e resumo no Build.
- Build Release e publish win-x64 autocontido concluídos sem erros.

Os probes que dependem de fixtures não são testes autossuficientes de CI.

Para verificar as falhas do lote de fotos de 16/11, `PhotoProbe` aceita
`--verify-photo-failures <projeto.fcsproj> <resource> <index.jsonl> <pasta-nova> <GTA>`.
O projeto fornece os metadados e os assets são procurados pelo nome no resource.
A saída deve ser uma pasta nova, separada da exportação original. O teste renderiza
as 85 entradas recuperáveis, preserva a validação estrita de tatuagens no build,
verifica os mips BC7 de 1024 e 512 px e a geometria completa do LOD disponível.
As quatro entradas com fontes inválidas ficam identificadas em `report.json`.

Exigem os projetos e assets locais referidos nos argumentos ou caminhos do programa.
Assets GTA, caches do jogo, exportações e projetos pessoais não estão no Git.
Para executar com seus dados, ajuste os caminhos de fixtures ou passe os argumentos
exigidos por cada Program.cs. Não execute contra uma cópia única de um projeto pessoal.

Exemplo com um projeto de teste local que contenha um drawable e YTD:

```powershell
dotnet run --project tools/ClothPreviewUiProbe/ClothPreviewUiProbe.csproj -c Release -- C:/fixtures/hat.fcsproj C:/temp/preview-test
```

## Entrega

O executável principal fica em publish/ParadiseClothStudio.exe. A publicação é separada
antes da troca do executável de uso; a versão anterior fica como .exe.previous no PC.
A atualização de 20/10 foi instalada após o usuário encerrar sua sessão.
O release do GitHub contém o executável autocontido e documentação; não inclui modelos
ou texturas de roupas. A compilação local usa Directory.Build.local.props ignorado no Git.
