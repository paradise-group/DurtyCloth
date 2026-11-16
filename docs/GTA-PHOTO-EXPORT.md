# Fotografias das roupas do GTA V

Use **Options → Export preview photos**, com o FiveM ligado ao servidor. Não existe
uma opção separada para o GTA nem é necessário exportar um catálogo manualmente.
O programa lê automaticamente as peças e texturas disponíveis para os modelos
masculino e feminino, respeitando as regras das lojas configuradas. A leitura usa
peds temporários invisíveis; a aparência do jogador permanece intacta.

O recurso `skinshop` precisa da versão que inclui `client/photo-catalog.lua`.
Depois de atualizar os arquivos, reinicie esse recurso uma vez. O programa consulta
a NUI local do FiveM para obter o catálogo completo dos dois modelos.

A instalação do GTA é detetada automaticamente. Se não estiver configurada, escolha
a pasta que contém `x64v.rpf`. O programa procura os modelos nos arquivos base e
nos DLCs instalados, exclui a pasta `mods` e mantém apenas as peças e texturas que
existem no catálogo do servidor. Junta-as ao projeto carregado, com prioridade para
as peças do pack que já têm o mesmo UUID, sem alterar o projeto aberto.

Na janela seguinte, escolha categorias, géneros e opções da imagem. Pode usar a
mesma pasta das fotos do pack: **Ignorar fotografias com o mesmo UUID** é a opção
por defeito. **Substituir** troca apenas as fotografias selecionadas que já existem.
Também é possível cancelar e voltar a executar a exportação, aproveitando as fotos
guardadas. Os arquivos do GTA são apenas lidos; os assets necessários são extraídos
para uma cache separada.

Se o FiveM ou o catálogo atualizado não estiver disponível, o programa informa
o motivo e permite continuar apenas com o projeto carregado. Não exporta a biblioteca
inteira do GTA como alternativa. O total de texturas vem dos natives para cada peça,
sem um limite fixo de 256.

Os UUIDs seguem a mesma regra dos modelos originais importados no projeto.
O arquivo `gta-clothing-identities.json`, guardado junto às fotos, associa os UUIDs
ao modelo, categoria, coleção, índice local e textura. Não o apague: é necessário
para ligar as novas fotos às peças no servidor. `index.jsonl` continua a reunir
os resultados das exportações do pack e do GTA.

Para preparar um único lote com as fotos dos dois, mantendo as pastas do nations3:

```powershell
python tools/prepare_cdn_photos.py --source "C:/Fotos" --output "C:/Pacote-CDN" --identities "C:/Pack/clothing-identities.json"
```

O preparador junta automaticamente o mapa do pack com
`C:/Fotos/gta-clothing-identities.json`. Aceita também vários arquivos depois de
`--identities`. Numa exportação só do GTA, esse argumento pode ser omitido.
O pacote inclui `upload/`, um ZIP com as imagens e um mapa combinado
`clothing-identities.json`. UUIDs incompatíveis são rejeitados.

Depois de terminar e verificar as fotos, use o manifesto e o mapa combinado para
regenerar `vrp/config/AppearanceImages.json` com a ferramenta do repositório `high`.
As imagens e o manifesto do servidor devem ser publicados no mesmo lote de entrega.

Só é possível fotografar conteúdo presente na instalação escolhida. Substituições
customizadas do servidor pertencem ao pack. Os erros de leitura ou renderização
ficam no índice; modelos com texturas herdadas podem exigir a origem dessas texturas.
A posição e as cores finais dentro do jogo podem diferir da prévia offline.
