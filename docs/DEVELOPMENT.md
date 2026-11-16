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

`dotnet run --project tools/PhotoProbe/PhotoProbe.csproj -c Release` executa a
suíte de regressão de fotografias, sem modos especiais de correção ou exportação.
O fluxo normal de exportação alinha chapéus pelo osso da cabeça do ped do mesmo
gênero, respeita a opacidade dos materiais e mostra a cabeça limpa nos modelos
de reserva usados como opções sem acessório. Texturas herdadas disponíveis no
GTA são resolvidas automaticamente, sem alterar os modelos do projeto.
As opções vazias de roupa mostram a região correspondente do ped; brincos usam
um enquadramento lateral aproximado da orelha. Mochilas usam a vista traseira e
chapéus podem usar a traseira quando a textura contém o detalhe principal nessa
face. O GTA é detetado pela configuração existente e a pasta de fotos é sempre
escolhida no diálogo de exportação.

As prévias de barbearia e tatuagens são geradas offline, sem capturar imagens no
jogo. Efeitos faciais usam as coordenadas da textura da cabeça; tatuagens usam o
segundo conjunto de UVs e o tile da zona definido no modelo. A aplicação respeita
posição, rotação, escala com sinal e continuidade horizontal. A câmera aproxima a
cabeça ou a região do corpo e escolhe o lado onde está a tatuagem.
As máscaras de cor de pelos recebem uma cor de prévia, preservando o alfa.
Mapas normais e especulares não são exportados como variantes visuais.
O catálogo inclui as definições de aparência e tatuagens do GTA instalado, e
preserva os UUIDs das substituições do projeto. Texturas sem metadados de posição
permanecem disponíveis no projeto, mas não são apresentadas como fotos aplicadas.
O teste padrão cobre o tile de cabeça, posições negativas, escala espelhada,
preservação de alfa e enquadramento da opção sem cabelo.

Materiais `ped_emissive` e `ped_palette` não usam o alfa difuso como transparência:
referências dos mantenedores do Sollumz em
https://github.com/Sollumz/Sollumz/issues/1121 e
https://github.com/Sollumz/Sollumz/issues/1122.
Decals com o marcador de decoração dinâmica não contêm o desenho final nos
arquivos do jogo; a prévia mostra o corpo sem inventar um logótipo.

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
