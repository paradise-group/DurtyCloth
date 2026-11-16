# Collections publicadas (FiveM)

Para salvar roupas como `collection + localIndex`, mantenha os índices já usados no servidor.
Cada projeto contém uma collection por gênero (com componentes e props); para expandir além
de 128 drawables no mesmo slot, crie outro projeto com outro DLC name e publique outro recurso.

Atualizações de recursos pelo MCP acrescentam os componentes no fim da última
coleção `pcs` de cada gênero, sem mudar índices existentes. Uma coleção nova por
gênero só é aberta sem coleção `pcs` anterior, ao passar de 128 drawables em um
slot, ou para props e peças com metadados complementares. Consulte as regras e o checklist em [MCP.md](MCP.md#antes-de-aplicar-uma-atualização-preparada-ao-recurso-do-servidor).

1. Gere e confira o addon. Em **Project Settings > General**, marque **Collection publicada**
   e aplique, confirmando que a ordem atual corresponde à publicação. Salve o `.fcsproj`.
2. O snapshot grava DLC name, prefixo e, por gênero/slot/localIndex, o `Guid` persistido da peça.
   `.fcsbundle` também preserva o snapshot. Não altere esses dados manualmente.
3. Adicione peças no final. Reordenação e mudança de gênero/slot de peças publicadas ficam bloqueadas; DLC name,
   nome do projeto, prefixo e nomes de replacement ficam travados. Renomear o label de uma peça
   continua permitido e não muda seu índice. Importar peças ou copiar para outro gênero acrescenta no final.
   Corrigir gênero/slot de uma peça ainda não publicada também a posiciona no final do destino.
4. Para retirar uma peça publicada, use **Replace with Dummy**. **Remove** oferece essa opção;
   um dummy publicado não pode ser removido. Importação não preenche reservas de collections publicadas.
5. Ao publicar uma atualização com peças novas, marque **Registrar também as novas peças como
   publicadas** nas configurações, aplique e salve. Esse registro só acrescenta índices; não aceita
   divergências do snapshot anterior. Não há comando de desbloqueio: outra collection usa outro projeto.

**Error List** e **Build** mostram divergências como erro; o exportador também recusa o build antes
de criar a saída. Com 128 ou mais drawables em um slot aparece aviso para abrir outra collection;
o Build permite continuar após confirmação. O limite inclui reservas dummy.

Todo build de addon (FiveM, alt:V, single player e RageMP) inclui `collection-index.json` na raiz.
`entries` contém `gender`, `slot`, `collection`, `localIndex`, `itemId`, `name`, `label`, `textureCount`
e `isDummy`. `collection` é o DLC name realmente gravado nos metadados (inclui `mp_m_`/`mp_f_`
quando habilitado); `slot` usa o número do componente ou `p0`, `p1`, `p2`, `p3`, `p6`, `p7`.
Índices começam em zero. `textureCount` inclui posições de textura reservadas; drawable dummy tem zero.
Para imagens do servidor, use `clothes/<gender>/<slot>/<collection>/<localIndex>.png`, mapeando
`Masculino`/`Feminino` para as pastas de gênero usadas pelo servidor. A exportação simples de stream
preserva nomes nativos e não gera esse mapa nem metadados de addon.

## Verificação manual

1. Faça uma cópia de projeto com duas peças no mesmo slot; publique nas configurações, salve e reabra.
2. Tente mudar Position, gênero/slot, DLC name e prefixo: devem estar bloqueados ou voltar ao valor anterior.
3. Use Remove numa peça publicada, confirme o dummy e confira o mesmo índice; tente remover essa reserva.
4. Adicione uma peça com índice de origem baixo: deve entrar no final. Faça Build e compare o JSON com o snapshot.
5. Registre as novas peças, exporte/import por bundle e confira a proteção novamente.
6. Numa cópia do JSON do projeto, altere ordem ou DLC name sem alterar o snapshot: Error List deve acusar erro
   e Build deve recusar a saída. Com 128/129 reservas no mesmo slot, confira o aviso de capacidade.

Probe autossuficiente (sem assets do GTA ou dependências novas):

```powershell
dotnet run --project tools/CollectionIndicesProbe/CollectionIndicesProbe.csproj -c Release
```

Ele cobre snapshot/validação, IDs, append, dummy, importação, cópia entre gêneros, roundtrip JSON/bundle,
limite 128/129 e build real com placeholders, releitura de YMT/shop e conferência do mapa.
Não comprova carregamento no FXServer nem nomes de imagens do CDN em produção.
