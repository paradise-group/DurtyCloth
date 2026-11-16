# Fotografias das roupas do GTA V

Em **Options → Export photos from installed GTA V**, escolha a pasta que contém
`x64v.rpf`. O programa procura roupas, cabelos e acessórios freemode masculinos e
femininos nos arquivos base e nos DLCs instalados. A pasta `mods` é excluída.
Esta opção funciona sem abrir um projeto e não altera o projeto carregado.

Na janela seguinte, escolha categorias, géneros e opções da imagem. Pode usar a
mesma pasta das fotos do pack: **Ignorar fotografias com o mesmo UUID** é a opção
por defeito. **Substituir** troca apenas as fotografias selecionadas que já existem.
Também é possível cancelar e voltar a executar a exportação, aproveitando as fotos
guardadas. Os arquivos do GTA são apenas lidos; os assets necessários são extraídos
para uma cache separada.

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
