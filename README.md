<img src="docs/logo.png" width="96" height="96" alt="Logo do MyIPTV">

# Jellyfin Plugin: MyIPTV

🇧🇷 Português | [🇺🇸 English](README.en.md)

Plugin para Jellyfin que sincroniza um provedor **Xtream Codes** (IPTV) com sua biblioteca:
gera automaticamente uma playlist M3U de TV ao vivo (com categorias e EPG) e arquivos `.strm`
para Filmes e Séries, prontos para o Jellyfin escanear como bibliotecas normais.

## Aviso legal

Este projeto é uma ferramenta técnica de sincronização: ele lê o catálogo de um provedor
Xtream Codes **ao qual você já tem acesso** e organiza esses dados dentro do seu próprio
Jellyfin (playlist M3U, guia EPG, arquivos `.strm`). Não fornecemos, hospedamos, revendemos
nem temos qualquer vínculo com provedores de IPTV.

O uso de serviços IPTV às vezes está associado à distribuição não autorizada de conteúdo
protegido por direitos autorais. A responsabilidade pela legalidade do provedor escolhido e
do conteúdo acessado é inteiramente do usuário. Use por sua conta e risco, apenas com
serviços aos quais você tenha direito legal de acesso.

## Por que assim, e não um "canal" dentro do Jellyfin?

Versões antigas de plugins IPTV para Jellyfin costumam implementar a interface `IChannel`
(a antiga aba "Canais"). A partir do Jellyfin 10.9+, esse ponto de extensão para plugins de
terceiros deixou de ser usado pelo core — o `IChannel` carrega normalmente, mas nunca é
instanciado pelo `ApplicationHost`, então nenhum conteúdo aparece. Testado e confirmado em
10.11.8.

Este plugin usa **`IScheduledTask`** (que continua funcionando normalmente) para gerar os
arquivos que o Jellyfin já sabe consumir nativamente: M3U Tuner + XMLTV para Live TV, e
bibliotecas de mídia comuns para Filmes/Séries via `.strm`.

## Requisitos

- Jellyfin **10.11.x** (net9.0). Não testado em outras versões major.
- Um provedor Xtream Codes (host, usuário, senha).
- Volumes/pastas graváveis pelo usuário que roda o Jellyfin (ex: PUID 1000 nas imagens
  `linuxserver/jellyfin`) para onde os `.strm` serão escritos.

## Instalação

### Via repositório de plugins (recomendado)

1. No Jellyfin: **Painel → Plugins → Repositórios → Adicionar Repositório**.
2. URL: `https://raw.githubusercontent.com/rhuancampos/MyIPTV-Jellyfin/main/manifest.json`
3. Vá em **Catálogo**, encontre "Meu IPTV Custom" e instale.
4. Reinicie o Jellyfin.

### Manual

1. Baixe o `.dll` da [última release](https://github.com/rhuancampos/MyIPTV-Jellyfin/releases).
2. Copie para `<config>/data/plugins/Jellyfin.Plugin.MyIPTV_<versão>/`.
3. Reinicie o Jellyfin.

## Configuração

Painel → Plugins → **Meu IPTV Custom**:

![Tela de configuração do plugin](docs/screenshot-config.jpg)

| Campo | Descrição |
|---|---|
| Link da playlist M3U | *Opcional.* Link `get.php?...&type=m3u_plus&output=ts` do provedor. Se preenchido, o sync usa só ele (veja abaixo) |
| Host | URL do painel Xtream, com `http://` e porta |
| Usuário / Senha | Credenciais da sua assinatura Xtream (não são necessários se usar o link da playlist) |
| Pasta dos Filmes | Onde os `.strm` de filmes são gravados (padrão `/data/movies`) |
| Pasta das Séries | Onde os `.strm` de séries são gravados (padrão `/data/tvshows`) |
| Arquivo M3U | Onde o `live-tv.m3u` é gravado (padrão `/config/live-tv.m3u`) |

**Importante:** as pastas de Filmes/Séries precisam ser graváveis pelo usuário do processo
Jellyfin. Em imagens `linuxserver/jellyfin`, isso geralmente significa rodar uma vez:
```bash
docker exec -u root <container> chown -R abc:abc /data/movies /data/tvshows
```

## Rodando a sincronização

Painel → **Tarefas Agendadas** → categoria "Meu IPTV Custom" → **Sincronizar MyIPTV** →
rodar manualmente a primeira vez. Depois disso, roda sozinha todo dia às 04:00.

Há duas fontes de dados; escolha uma:

- **API Xtream** (Host + Usuário + Senha): busca categorias e canais, casa com o guia XMLTV,
  busca o catálogo de filmes e, para cada série, uma chamada `get_series_info` (lenta em
  catálogos grandes, e alguns provedores limitam com 503).
- **Link da playlist M3U** (`type=m3u_plus`): baixa tudo em **uma única requisição**, já com
  categorias, `tvg-id` do guia e todos os episódios. É bem mais rápido e não sofre com limite
  de requisições. O parâmetro `output` do link (`ts` ou `m3u8`) define o formato dos canais ao vivo.
  Episódios são reconhecidos pelo `SxxExx` no nome (ex: `Os Flintstones S01E09`).

Nas duas, o resultado é o mesmo: o M3U de Live TV e um `.strm` por filme e por episódio.
Se uma etapa falha (ex: provedor fora do ar), os arquivos que já existiam são mantidos e a
tarefa termina como falha.

## Como os arquivos são organizados

```
/config/live-tv.m3u                                     ← um único M3U com todos os canais
/data/movies/Drama/Capitã Marvel (2019)/Capitã Marvel (2019).strm
/data/tvshows/Netflix/Os Flintstones/Season 01/Os Flintstones - S01E09.strm
```

- **Categoria vira pasta**, sem o prefixo antes do `|` (`Filmes | Drama` → `Drama`,
  `Series | Netflix` → `Netflix`). No M3U, o `group-title` do canal segue a mesma regra
  (`Canais | Globo` → `Globo`), e o prefixo repetido no nome do canal é removido
  (categoria `A Fazenda 18` + canal `A Fazenda 18 CAM 01 (A)` → `CAM 01 (A)`).
- **Nomes são limpos para o Jellyfin achar os metadados**: tags como `[L]` e `[4K]` saem,
  `Nome - 2009` vira `Nome (2009)`, e `:` vira `-` (`Alabama: Presos` → `Alabama - Presos`).
- **Sem duplicatas**: um filme ou série que aparece em mais de uma categoria (ou em mais de
  uma versão, como `[L]` e `[4K]`) é escrito uma só vez, na primeira categoria em que aparece.
  Versões legendadas (`[L]`) só entram se não houver outra.
- **Guia (EPG)**: usa o `tvg-id`/`epg_channel_id` que o provedor informa; sem ele, casa pelo
  nome do canal ignorando `HD`, `FHD`, `4K`, `H265` etc.
- Arquivos só são regravados quando o conteúdo muda. O plugin **nunca apaga** nada: se um
  título sair do provedor, o `.strm` antigo continua lá até você removê-lo.

## Depois de sincronizar

- **Live TV**: Painel → Live TV → Tuner Devices → adicionar M3U Tuner apontando pro arquivo
  configurado. Depois, TV Guide Data Providers → XMLTV → URL do guia do seu provedor.
- **Filmes/Séries**: crie bibliotecas normais apontando para as pastas configuradas, com o
  tipo de conteúdo certo (Filmes / Séries), e rode um scan.

## Licença

GPL-3.0 — veja [LICENSE](LICENSE).
