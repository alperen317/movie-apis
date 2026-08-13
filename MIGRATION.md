# Supabase → .NET geçiş planı

Kaynak proje: `../mobile-base` (Expo/React Native, `lib/supabase/*` + `supabase/migrations/*`).

## Taşınacak yüzeyin envanteri

| Supabase tarafı | Adet | .NET karşılığı |
|---|---|---|
| Tablo (`public.*`) | 11 + 2 rate-limit | EF Core entity + migration |
| `auth.users` + `profiles` | 2 | Tek `ApplicationUser` (ASP.NET Identity) |
| RPC (SECURITY DEFINER/INVOKER) | 9 | Command/Query handler |
| RLS policy | ~25 | Authorization servisi + query filtresi |
| Tamper-guard trigger | 3 | Handler içi açık kontrol + CHECK constraint |
| Realtime publication | 6 tablo | SignalR hub (`list:{listId}` grubu) |
| Edge Function (Deno) | 2 | `IEmailSender` (Brevo) implementasyonu |

## Kararlar

| Konu | Seçim | Gerekçe |
|---|---|---|
| Veritabanı | PostgreSQL 17 | `uuid`, `timestamptz`, `text[]` (genres) native taşınır |
| SDK | .NET 10.0.400 LTS (x64) | EF Core 10 + Npgsql 10 kararlı paketleri var |
| Mimari | Clean Architecture, 4 proje | Domain / Application / Infrastructure / Api |
| Mediator | `Mediator` 3.0.2 (martinothamar) | MIT, source generator, MediatR API'sine denk |
| Assertion | Shouldly 4.3.0 | FluentAssertions 8+ ticari lisansa geçti |
| Realtime | SignalR | Supabase Realtime'ın birebir karşılığı |
| Veri taşıma | Yok, temiz başlangıç | Canlı kullanıcı yok; standart Identity PBKDF2 hash'i |

## Fazlar

### Faz 0 — İskelet ✅
6 proje, katman referansları, NuGet paketleri, `Directory.Build.props`, `docker-compose.yml` (Postgres 17 + healthcheck).

### Faz 1 — Domain + EF Core şema
- Entity'ler: `SavedMedia`, `WatchLogEntry`, `EpisodeProgress`, `RecommendationFeedback`,
  `MediaList`, `ListMember`, `ListItem`, `ListPoll`, `ListPollCandidate`, `ListPollVote`.
- `auth.users` + `public.profiles` **tek tabloda birleşiyor**: `display_name`, `avatar_variant`,
  `avatar_seed`, `watch_region` alanları `ApplicationUser : IdentityUser<Guid>` üzerine taşınır.
  Supabase'de bu ikilik PostgREST'in `auth` şemasını göremmesinden kaynaklanıyordu
  (`0002_profiles.sql`'deki trigger + backfill yalnızca bunu senkron tutmak içindi); .NET'te
  o kısıt yok, dolayısıyla trigger da senkronizasyon da gereksiz.
- `genres text[]` → `string[]` (Npgsql native), `timestamptz` → `DateTime` (UTC).
- İlk EF Core migration + `docker compose up -d db` üzerinde doğrulama.

### Faz 2 — Kimlik doğrulama
- ASP.NET Identity + JWT (access + refresh token).
- Supabase'in 6 haneli OTP akışı → Identity'nin `EmailTokenProvider`'ı (TOTP tabanlı, zaten 6 hane):
  kayıt doğrulama, kod yeniden gönderme, şifre sıfırlama.
- "Remember me" → refresh token ömrü; mobildeki `authStorage.ts` mantığı korunur.
- `delete_account()` RPC → `DELETE /me`, cascade ile.

### Faz 3 — RLS → Authorization  ⚠️ en kritik faz
RLS artık veritabanında değil. Her sorgu sahiplik filtresi, her komut yetki kontrolü almalı.

- `is_list_member` / `is_list_owner` / `can_view_list` → authorization servisi.
- Kişisel tablolarda (`saved_media`, `watch_log`, `episode_progress`,
  `recommendation_feedback`) EF global query filter ile `UserId == currentUser`.
- Trigger'ların karşılığı: `lists.created_by` değişmez; `list_members` üzerinde yalnızca
  kendi *pending* davetini bir kez accept/decline edebilirsin (`0003`'teki
  `prevent_list_member_tampering` mantığı).
- **Korunacak güvenlik davranışları** (regresyon riski yüksek):
  - `invite_to_list`: "hesap yok" ile "zaten üye" **ayırt edilemez** olmalı — tek `invite_failed`
    hatası (`0021_invite_enumeration_fix.sql`). Aksi halde e-posta enumeration açığı geri gelir.
  - `find_user_id_by_email` dışarıya **açılmamalı** (`0022`).
  - `get_list_watch_summary` yalnızca **agregat sayı** döner, tekil watch kaydı değil (`0017`).

### Faz 4 — Endpoint'ler
Feature bazlı dikey dilimler. RPC → endpoint eşlemesi:

| RPC | Endpoint |
|---|---|
| `create_shared_list` | `POST /lists` |
| `invite_to_list` | `POST /lists/{id}/invites` |
| `join_list_by_code` | `POST /lists/join` |
| `regenerate_list_join_code` | `POST /lists/{id}/join-code` |
| `start_list_poll` | `POST /lists/{id}/polls` |
| `cast_poll_vote` | `POST /polls/{id}/votes` |
| `get_list_poll` | `GET /lists/{id}/poll` |
| `get_list_watch_summary` | `GET /lists/{id}/watch-summary` |
| `delete_account` | `DELETE /me` |

Rate limit (`0012`, `0014`: 10 dakikada 20 deneme) → ASP.NET Core rate limiting middleware.

### Faz 5 — SignalR
`ListHub`, grup adı `list:{listId}`. Handler'lar mutasyondan sonra gruba yayın yapar.
Supabase Realtime'da DELETE olayları için gereken `REPLICA IDENTITY FULL` hilesi (`0005`)
burada gereksiz — sunucu zaten silinen satırın tamamını biliyor.

### Faz 6 — E-posta
İki Edge Function → tek `IEmailSender` (Brevo API). Şablonlar `send-auth-email/emailTemplates.ts`
ve `send-list-invite-email/index.ts`'den taşınır.

### Faz 7 — Mobil istemci geçişi
`lib/supabase/*` → `lib/api/*` (token yenileme interceptor'lı fetch istemcisi).
`stores/*.ts` güncellenir; mevcut jest testleri sözleşme kontrolü olarak kullanılır.

### Faz 8 — Dağıtım
Dockerfile, hosting, CI.

## Yerel geliştirme

```bash
docker compose up -d db      # Postgres 17, localhost:5432
dotnet build                 # tüm çözüm
dotnet run --project src/Movie.Api
```
