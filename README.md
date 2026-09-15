# Jellyfin Plugin: MyIPTV

Plugin para Jellyfin que sincroniza um provedor **Xtream Codes** (IPTV) com sua biblioteca:
gera automaticamente uma playlist M3U de TV ao vivo (com categorias e EPG) e arquivos `.strm`
para Filmes e Séries, prontos para o Jellyfin escanear como bibliotecas normais.

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

| Campo | Descrição |
|---|---|
| Host | URL do painel Xtream, com `http://` e porta |
| Usuário / Senha | Credenciais da sua assinatura Xtream |
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

A tarefa:
1. Busca categorias e canais ao vivo, casa com o guia XMLTV do provedor, e escreve o M3U.
2. Busca o catálogo de filmes e gera um `.strm` por filme.
3. Busca a lista de séries e, para cada uma, busca os episódios e gera um `.strm` por episódio.

## Depois de sincronizar

- **Live TV**: Painel → Live TV → Tuner Devices → adicionar M3U Tuner apontando pro arquivo
  configurado. Depois, TV Guide Data Providers → XMLTV → URL do guia do seu provedor.
- **Filmes/Séries**: crie bibliotecas normais apontando para as pastas configuradas, com o
  tipo de conteúdo certo (Filmes / Séries), e rode um scan.

## Licença

GPL-3.0 — veja [LICENSE](LICENSE).
