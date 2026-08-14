# Kalan iş planı

Faz 0–3 bitti. Bu belge kalan dört fazın ayrıntısı; tamamlanan fazların kaydı ve
alınan kararlar [MIGRATION.md](../MIGRATION.md) dosyasında.

| Faz | Konu | Durum |
|---|---|---|
| 0 | İskelet | ✅ |
| 1 | Domain + EF Core şema | ✅ |
| 2 | Kimlik doğrulama | ✅ |
| 3 | RLS → Authorization | ✅ |
| **4** | **Endpoint'ler** | 4a ✅ 4b ✅ · sırada 4c |
| 5 | SignalR | |
| 6 | E-posta (Brevo) | |
| 7 | Mobil istemci geçişi | |
| 8 | Dağıtım | |

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

### 4c · Davetler ve kodla katılma ⚠️

Faz 4'ün güvenlik açısından en hassas kısmı. Üç davranış migration yorumlarında açıkça
"bu bir güvenlik düzeltmesiydi" diye işaretlenmiş.

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

### 4d · Anketler

| RPC | Endpoint |
|---|---|
| `get_list_poll` | `GET /lists/{id}/poll` |
| `start_list_poll` | `POST /lists/{id}/polls` |
| `cast_poll_vote` | `POST /polls/{id}/votes` |

Kurallar:

- En az 2 aday, bitiş zamanı gelecekte olmalı
- **Liste başına aynı anda tek aktif anket**
- Anketin kapalı olduğu saklanmıyor; `Deadline` geçtiyse kapalı. Oy verilirken kontrol
  ediliyor, arka plan işi yok
- Kişi başına anket başına tek oy; fikir değiştirmek yeni satır değil güncelleme
- `GET` en son anketi döner (aktif ya da yeni kapanmış), aday başına oy sayısı ve
  çağıranın oyuyla birlikte

### 4e · İzleme özeti ⚠️

`get_list_watch_summary` → `GET /lists/{id}/watch-summary`

Faz 3'te tanımlanan tek meşru çapraz kullanıcı okuması. `IgnoreQueryFilters()` burada
kullanılacak.

**Yalnızca içerik başına sayı dönmeli**, tekil kayıt asla. `0017`'nin yorumu bunu açıkça
söylüyor: üyenin kişisel izleme geçmişi, puanı ve notu co-member'lardan gizli kalmalı.

---

## Faz 5 — SignalR

Supabase Realtime'ın karşılığı. `ListHub`, grup adı `list:{listId}`.

- İstemci bir listeyi açtığında gruba katılır, kapattığında ayrılır
- Gruba katılmadan önce `IListAccess.ForMemberAsync` ile yetki kontrolü — aksi halde
  herhangi biri herhangi bir listenin değişikliklerini dinleyebilir
- Handler'lar mutasyondan sonra gruba yayın yapar: içerik eklendi/çıkarıldı, üye
  değişti, liste yeniden adlandırıldı, anket güncellendi
- JWT ile kimlik doğrulama: SignalR token'ı query string'de taşır, `OnMessageReceived`
  ile alınmalı

Supabase'deki iki hilenin karşılığı **gereksiz**:

- `REPLICA IDENTITY FULL` (`0005`) — DELETE olaylarında silinen satırın kolonlarının
  WAL'a yazılması içindi. Sunucu zaten neyi sildiğini biliyor.
- İstemci tarafı filtreleme — Supabase'de DELETE olayları sunucu tarafında
  filtrelenemediği için tüm tabloya abone olunup istemcide filtreleniyordu. Burada
  yayın zaten yalnızca ilgili gruba gidiyor.

---

## Faz 6 — E-posta

İki Edge Function tek `IEmailSender` implementasyonuna iniyor. Arayüz ve şablonlar
Faz 2'de yazıldı; eksik olan Brevo'ya gerçekten gönderen sınıf.

- `BrevoEmailSender` — `https://api.brevo.com/v3/smtp/email`
- Yapılandırma: `BREVO_API_KEY`, `BREVO_SENDER_EMAIL`, `BREVO_SENDER_NAME`
- **Production'da `AddInfrastructure` şu an bilerek hata fırlatıyor**; bu faz o kontrolü
  kaldıracak
- Liste davet e-postası şablonu `send-list-invite-email/index.ts`'den taşınacak
- Gönderim hatası daveti geçersiz kılmamalı — Supabase'de de "gönder ve unut"tu

---

## Faz 7 — Mobil istemci geçişi

`lib/supabase/*` → `lib/api/*`.

- Token yenileme interceptor'lı fetch istemcisi: 401 alınca bir kez `/auth/refresh`
  deneyip isteği tekrarlar, o da başarısızsa oturumu kapatır
- `authStorage.ts` olduğu gibi kalır — "beni hatırla" tamamen istemci tarafı
- Realtime aboneliği `@supabase/supabase-js`'ten SignalR istemcisine geçer
- `stores/*.ts` güncellenir; mevcut jest testleri sözleşme kontrolü olarak kullanılır
- `@supabase/supabase-js` bağımlılığı kaldırılır

Bilinen sözleşme farkları (Faz 2 ve 3'te alınan kararlar):

- **`PUT /me` kısmi güncelleme değil**, gönderilmeyen alan siliniyor. `updateOwnProfile`
  buna göre yazılacak
- **Access token 15 dakika** (60 değil) — yenileme daha sık, istemci mantığı aynı
- **`verify-email` token dönüyor**, yani doğrulamadan sonra ayrıca giriş gerekmiyor
- Enum'lar küçük harfli metin olarak gidip geliyor, mevcut string literal'lerle uyumlu

Ayrıca: kod geçerlilik süresi ekranında geri sayım düşünülebilir (kod 1 saat geçerli).

---

## Faz 8 — Dağıtım

- Production `Dockerfile` (mevcut olan geliştirme için yazıldı, gözden geçirilecek)
- Ortam değişkenleri: `Jwt__SigningKey`, `ConnectionStrings__Database`, Brevo anahtarları
- Migration stratejisi: geliştirmede açılışta uygulanıyor, production'da **açıkça**
  çalıştırılmalı
- Sağlık kontrolü ucu
- CI: derleme + test (Testcontainers Docker istiyor)
- Sentry entegrasyonu (mobil tarafta zaten var)

---

## Faz 4'e başlarken

Önerilen sıra 4a → 4b → 4c → 4d → 4e. Gerekçe: 4a en izole olan ve Faz 3'ün query
filter'ları sayesinde neredeyse kendiliğinden güvenli; 4c ise geri gelmesi en kolay
güvenlik açıklarını içeriyor ve ondan önce liste altyapısının oturmuş olması gerekiyor.
