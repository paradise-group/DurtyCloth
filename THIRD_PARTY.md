# Componentes externos

- `BCnEncoder.Net` 2.3.0: descodificação de texturas BC7 para prévias e fotos.
  Projeto: https://github.com/Nominom/BCnEncoder.NET
  Licença MIT: `licenses/BCnEncoder.Net-LICENSE-MIT.txt`.

- `CodeWalker.Core.dll`: usado para ler e compilar metadados binários YMT.
  Cópia local proveniente do Mapper instalado neste computador.
  Projeto: https://github.com/dexyfex/CodeWalker
  Avisos do projeto: https://github.com/dexyfex/CodeWalker/blob/master/Notice.txt
- `MessagePack` 3.1.12: usado para importar projetos `.dctproj` com blocos LZ4.
  Projeto: https://github.com/MessagePack-CSharp/MessagePack-CSharp

O código do Durty Cloth Tool não é incluído no Foox Cloth Studio.

Referências de formato usadas nas novas funções:
- Creature metadata: esquema público do YMTEditor,
  https://github.com/grzybeek/YMTEditor/blob/master/YMTEditor/XMLHandler.cs
- Primeira pessoa: estruturas de metadados e exemplos publicados no Cfx Forum,
  https://forum.cfx.re/t/how-to-stream-clothes-and-props-as-addons-for-mp-freemode-models/3345474/1000
- Registro de metadados: https://docs.fivem.net/docs/game-references/data-files/
- Recursos alt:V: https://docs.altv.mp/articles/resources.html e
  https://docs.altv.mp/articles/configs/stream.html
