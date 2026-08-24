# Kalan iş planı

Faz 0–3 bitti. Bu belge kalan dört fazın ayrıntısı; tamamlanan fazların kaydı ve
alınan kararlar [MIGRATION.md](../MIGRATION.md) dosyasında.

| Faz | Konu | Durum |
|---|---|---|
| 0 | İskelet | ✅ |
| 1 | Domain + EF Core şema | ✅ |
| 2 | Kimlik doğrulama | ✅ |
| 3 | RLS → Authorization | ✅ |
| 4 | Endpoint'ler | ✅ |
| 5 | SignalR | ✅ |
| 6 | E-posta (Brevo) | ✅ |
| 7 | Mobil istemci geçişi | ✅ |
| **8** | **Dağıtım** | sırada |

---

## Faz 4 — Endpoint'ler

Mobil istemcinin `lib/supabase/*` altındaki her çağrısının bir karşılığı olacak.
Beş alt adıma bölünüyor; sıralama bilinçli: kolay ve izole olanlar önce, güvenlik
açısından hassas olanlar sonra.

### 4a · Kişisel içerik ✅

Faz 3'ün query filter'ları sahipliği zaten hallettiği için bu uçlar en basit olanlar.
Handler'lar filtre yazmıyor. Uygulanırken alınan kararlar
[MIGRATION.md](../MIGRATION.md#faz-4a--kişisel-içerik-) altında.

| Kaynak (`lib/supabase/`) | Endpoint |
|---|---|
| `lists.ts: fetchSavedMedia` | `GET /saved-media?listType=favorite\|watchlist` |
| `lists.ts: addSavedMedia` | `POST /saved-media` |
| `lists.ts: addSavedMediaBatch` | `POST /saved-media/batch` |
| `lists.ts: removeSavedMedia` | `DELETE /saved-media/{mediaType}/{mediaId}?listType=` |
| `watchLog.ts: fetchWatchLog` | `GET /watch-log` |
| `watchLog.ts: addWatchLogEntry` | `POST /watch-log` |
| `watchLog.ts: addWatchLogEntriesBatch` | `POST /watch-log/batch` |
| `watchLog.ts: updateWatchLogEntry` | `PUT /watch-log/{id}` |
| `watchLog.ts: deleteWatchLogEntries` | `DELETE /watch-log` (gövdede id listesi) |
| `episodeProgress.ts: fetchAllEpisodeProgress` | `GET /episode-progress` |
| `episodeProgress.ts: markEpisodeWatched` | `PUT /episode-progress/{showId}/{season}/{episode}` |
| `episodeProgress.ts: unmarkEpisodeWatched` | `DELETE /episode-progress/{showId}/{season}/{episode}` |
| `episodeProgress.ts: markEpisodesWatchedBatch` | `POST /episode-progress/batch` |
| `episodeProgress.ts: unmarkSeasonWatched` | `DELETE /episode-progress/{showId}/{season}` |
| `recommendationFeedback.ts: fetchDismissedKeys` | `GET /recommendation-feedback` |
| `recommendationFeedback.ts: addDismissed` | `POST /recommendation-feedback` |

Dikkat edilecekler:

- **Toplu ekleme uçları importer içindir** (TV Time / Letterboxd). İstemci 500'lük
  parçalara bölüyordu; sunucuda bir üst sınır olmalı, yoksa tek istekle bellek
  tüketilebilir.
- **`saved_media` toplu ekleme çakışmayı yok sayar.** Mevcut tekillik kısıtı sayesinde
  yeniden çalıştırılan bir içe aktarma hata vermiyor, sessizce atlıyor.
- **`watch_log` toplu ekleme düz insert.** Tekillik kısıtı yok, yeniden izlemeler
  bilinçli olarak çoğaltılıyor.
- **"İzledim" işaretini kaldırmak o başlığın tüm satırlarını siler**, yalnızca sonuncusunu
  değil. Arayüzdeki işaret "hiç kaydı var mı" demek; bir yeniden izleme geride kalırsa
  başlık izlenmiş görünmeye devam eder.
- **Bölüm ilerlemesi upsert.** "Buraya kadar izledim" çok satırlı bir upsert olarak iniyor.

### 4b · Listeler ve üyelik ✅

`IListAccess` üzerinden. Handler'lar `lists` tablosunu doğrudan sorgulamıyor.
Uygulanırken alınan kararlar [MIGRATION.md](../MIGRATION.md#faz-4b--listeler-ve-üyelik-)
altında.

| RPC / çağrı | Endpoint | Erişim |
|---|---|---|
| `fetchMyLists` | `GET /lists` | kabul etmiş üyelikler |
| `fetchListById` | `GET /lists/{id}` | `ForViewerAsync` |
| `create_shared_list` | `POST /lists` | herkes |
| `renameSharedList` | `PUT /lists/{id}` | `ForMemberAsync` |
| `deleteSharedList` | `DELETE /lists/{id}` | `ForOwnerAsync` |
| `fetchListMembers` | `GET /lists/{id}/members` | `ForMemberAsync` |
| `removeMember` | `DELETE /members/{id}` | sahip **veya** kendisi |
| `fetchListItems` | `GET /lists/{id}/items` | `ForMemberAsync` |
| `addListItem` | `POST /lists/{id}/items` | `ForMemberAsync` |
| `removeListItem` | `DELETE /lists/{id}/items/{mediaType}/{mediaId}` | `ForMemberAsync` |

Dikkat edilecekler:

- **`create_shared_list` iki insert'i tek işlemde yapıyordu**: liste ve sahibin üyelik
  satırı. `SaveChanges` bunu zaten tek transaction'da yapıyor.
- **Katılım kodu oluşturulurken çakışma ihtimaline karşı yeniden denenmeli.** 32 sembollük
  8 karakterde pratikte imkânsız ama kısıt ihlali yakalanmalı.
- **İçerik silme `added_by`'a bağlı değil.** Kabul etmiş her üye her içeriği kaldırabilir;
  bu bilinçli bir ürün kararı.
- **`fetchMyLists` `list_members` üzerinden sorguluyordu, `lists` üzerinden değil** — çünkü
  `lists` görünürlüğü bekleyen davetleri de kapsıyor ve ikisi karışmamalı.

### 4c · Davetler ve kodla katılma ✅

Faz 4'ün güvenlik açısından en hassas kısmı. Üç davranış migration yorumlarında açıkça
"bu bir güvenlik düzeltmesiydi" diye işaretlenmiş — üçü de korundu; ayrıntı
[MIGRATION.md](../MIGRATION.md#faz-4c--davetler-ve-kodla-katılma-) altında.

| RPC | Endpoint |
|---|---|
| `invite_to_list` | `POST /lists/{id}/invites` |
| `respondToInvite` | `POST /invites/{id}/response` |
| `fetchPendingInvites` | `GET /lists/invites` |
| `join_list_by_code` | `POST /lists/join` |
| `regenerate_list_join_code` | `POST /lists/{id}/join-code` |

**Korunması zorunlu davranışlar:**

- **`invite_to_list` tek hata döner.** "Bu e-postanın hesabı yok" ile "zaten üye/davetli"
  ayırt edilemez olmalı (`0021_invite_enumeration_fix.sql`). Ayrı hata kodları, liste
  sahibinin rastgele adresleri davet ederek kimin kayıtlı olduğunu öğrenmesini sağlıyordu.
  `cannot_invite_self` ayrı kalabilir — yalnızca çağıranın kendi adresi için tetikleniyor.
- **E-postadan kullanıcı bulma dışarı açılmamalı** (`0022`). Supabase'de bu RPC
  `authenticated` rolüne verilmişti ve yukarıdaki korumayı tamamen baypas eden,
  sınırsız bir "bu adres kayıtlı mı" oracle'ı yaratıyordu.
- **Rate limit**: davet ve kodla katılma için 10 dakikada 20 deneme (`0012`, `0014`).
  Faz 2'deki `RateLimiting` altyapısı kullanılacak; sınır çağıranın adresine göre,
  gönderilen e-postaya göre değil.

Ayrıca:

- **Kodla katılma bekleme adımı olmadan doğrudan üye yapıyor.** Kodu bilmek yetkinin
  kendisi; bu yüzden kod kriptografik üreteçle üretiliyor (Faz 1'de yapıldı).
- **Reddedilmiş davet yeniden gönderilebilir** (Faz 3'te açıldı, Supabase'de trigger
  yüzünden ulaşılamazdı).
- Davet e-postası Faz 6'ya kadar loglanacak.

### 4d · Anketler ✅

Uygulanırken alınan kararlar [MIGRATION.md](../MIGRATION.md#faz-4d--anketler-) altında —
orijinal SQL'de bir açık bulundu ve düzeltildi (aday, aday gösterildiği listeye ait
olmalı, hiç doğrulanmıyordu).

### 4e · İzleme özeti ✅

Faz 3'te tanımlanan tek meşru çapraz kullanıcı okuması. Uygulanırken alınan kararlar
[MIGRATION.md](../MIGRATION.md#faz-4e--i̇zleme-özeti-) altında.

---

## Faz 5 — SignalR ✅

Supabase Realtime'ın karşılığı. `ListHub`, grup adı `list:{listId}`. Uygulanırken alınan
kararlar [MIGRATION.md](../MIGRATION.md#faz-5--signalr-) altında.

---

## Faz 6 — E-posta ✅

İki Edge Function tek `IEmailSender` implementasyonuna indi. Arayüz ve doğrulama kodu
şablonları Faz 2'de yazılmıştı; eksik olan Brevo'ya gerçekten gönderen sınıftı.
Uygulanırken alınan kararlar [MIGRATION.md](../MIGRATION.md#faz-6--e-posta-) altında.

- `BrevoEmailSender` — `https://api.brevo.com/v3/smtp/email`, `IEmailSender`'ın tek
  production implementasyonu
- Yapılandırma: `Brevo:ApiKey`, `Brevo:SenderEmail`, `Brevo:SenderName` — eksikse
  açılışta hata
- Liste davet e-postası şablonu `send-list-invite-email/index.ts`'den `IListInviteEmailSender`
  + `ListInviteEmailTemplates`'e taşındı
- Gönderim hatası daveti geçersiz kılmıyor — `ListInviteEmailSender` hatayı yutup logluyor,
  Supabase'de de "gönder ve unut"tu

---

## Faz 7 — Mobil istemci geçişi ✅

`mobile-base`'te `lib/supabase/*` → `lib/api/*`, `@supabase/supabase-js` kaldırıldı. Dört
alt fazda yürütüldü (7a HTTP istemci + auth/profil, 7b kişisel içerik store'ları, 7c
paylaşımlı listeler + SignalR, 7d bağımlılık temizliği), her biri kendi commit'ini ve
canlı (Docker'daki previously-api + Expo web + iki hesap) doğrulamasını aldı. Uygulanırken
alınan kararlar — token yenileme tasarımı, `PUT /me` tam-değiştirme için istemci tarafı
birleştirme, SignalR bağlantı ömrü/reconnect, ve canlı testte bulunup düzeltilen bir
SignalR enum serileştirme hatası dahil — [MIGRATION.md](../MIGRATION.md#faz-7--mobil-istemci-geçişi-)
altında.

`supabase/` CLI proje klasörü (migrations, Edge Functions) mobil repoda kullanıcı kararıyla
tarihsel referans olarak kaldı, silinmedi.

---

## Faz 8 — Dağıtım

- Production `Dockerfile` (mevcut olan geliştirme için yazıldı, gözden geçirilecek)
- Ortam değişkenleri: `Jwt__SigningKey`, `ConnectionStrings__Database`, Brevo anahtarları
- Migration stratejisi: geliştirmede açılışta uygulanıyor, production'da **açıkça**
  çalıştırılmalı
- Sağlık kontrolü ucu
- CI: derleme + test (Testcontainers Docker istiyor)
- Sentry entegrasyonu (mobil tarafta zaten var)
