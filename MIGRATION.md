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

Tamamlananların kaydı ve alınan kararlar aşağıda. Kalan fazların ayrıntılı iş planı:
[docs/plan.md](docs/plan.md)

### Faz 0 — İskelet ✅
6 proje, katman referansları, NuGet paketleri, `Directory.Build.props`, `docker-compose.yml` (Postgres 17 + healthcheck).

### Faz 1 — Domain + EF Core şema ✅
- Entity'ler: `SavedMedia`, `WatchLogEntry`, `EpisodeProgress`, `RecommendationFeedback`,
  `MediaList`, `ListMember`, `ListItem`, `ListPoll`, `ListPollCandidate`, `ListPollVote`.
- `auth.users` + `public.profiles` **tek tabloda birleşti**: `display_name`, `avatar_variant`,
  `avatar_seed`, `watch_region` alanları `ApplicationUser : IdentityUser<Guid>` üzerine taşındı.
  Supabase'de bu ikilik PostgREST'in `auth` şemasını görememesinden kaynaklanıyordu
  (`0002_profiles.sql`'deki trigger + backfill yalnızca bunu senkron tutmak içindi); .NET'te
  o kısıt yok, dolayısıyla trigger da senkronizasyon da gereksiz.
- `genres text[]` → `string[]` (Npgsql native), `timestamptz` → `DateTime` (UTC).
- Identity rolsüz kuruldu (`IdentityUserContext`): uygulamada global rol kavramı yok,
  `MemberRole` liste bazlı. Identity tabloları `users`, `user_claims`, `user_logins`,
  `user_tokens` olarak yeniden adlandırıldı. Toplam 14 tablo.
- **`MediaSnapshot` owned type denendi ve geri alındı.** EF Core 10, entity ve complex type
  kolonlarını birlikte kapsayan indeks tanımlayamıyor; `saved_media` ve `list_items`
  tekillik kısıtları tam olarak buna ihtiyaç duyuyor. Yedi TMDB alanı üç entity'ye
  düzleştirildi, böylece her kısıt EF modelinin içinde kaldı.
- Migration uygulandı ve kısıtlar canlı veritabanında test edildi (tekillik, rating aralığı,
  liste adı uzunluğu, cascade).

### Faz 2 — Kimlik doğrulama ✅
Uçları elle geçmek için sıralı senaryo: [docs/auth-test-protokolu.md](docs/auth-test-protokolu.md)

11 uç: `register`, `verify-email`, `resend-verification`, `login`, `refresh`, `logout`,
`forgot-password`, `reset-password`, ve `GET`/`PUT`/`DELETE /me`.

Plandan üç sapma oldu, üçü de ölçüme dayanıyor:

- **6 haneli kodlar Identity'nin TOTP sağlayıcısından değil, kendi
  `verification_codes` tablomuzdan geliyor.** Identity'nin penceresi ölçüldü: 3
  dakikalık timestep ve ±2 doğrulama aralığı, yani kod üretim anına göre 6–9 dakika
  yaşıyor. İkisi de belgelenmemiş iç detay. Kendi tablomuz kesin 1 saat süre, tek
  kullanımlık olma ve 5 deneme sınırı veriyor — sonuncusu TOTP'de imkânsızdı ve altı
  hanenin tek gerçek koruması o.
- **"Remember me" sunucuya hiç dokunmuyor.** `authStorage.ts` incelendiğinde bunun
  tamamen istemci tarafı bir saklama kararı olduğu görüldü: token diske mi belleğe mi
  yazılacak. Supabase her iki durumda da aynı token'ı üretiyordu.
- **Access token 60 değil 15 dakika.** Token iptal edilemiyor, dolayısıyla çıkış
  yenilemeyi durduruyor ama kullanımı durdurmuyor — elle testte çıkış sonrası
  `PUT /me`'nin çalıştığı görüldü. Ömür, o pencerenin genişliği demek.

Refresh token'lar rotasyonlu: her kullanımda değişiyor, harcanmış bir token'ın
yeniden ortaya çıkması hırsızlık sayılıp o kullanıcının tüm oturumlarını düşürüyor.
Çıkışla iptal edilen token bundan ayrı tutuluyor — o sadece bayat.

Bilinen ve belgelenmiş sınır: access token iptal edilemez, çıkış/şifre sıfırlama
sonrası en fazla 15 dakika çalışmaya devam eder. Hesap silme istisna, çünkü `/me`
claim'lere değil satıra bakıyor.

### Faz 3 — RLS → Authorization ✅
Veritabanı seviyesindeki korumanın tamamı uygulama katmanına taşındı. Üç ayrı mekanizma,
çünkü RLS'in yaptığı iş tek türden değildi.

**Kişisel tablolar → EF global query filter.** `saved_media`, `watch_log`,
`episode_progress`, `recommendation_feedback` üzerinde `UserId == CurrentUserId`.
Handler'larda filtre yazılmıyor, EF ekliyor. Kimse giriş yapmamışsa hiçbir satır
eşleşmiyor — sessizce kapanmak açık kalmaktan iyi. `verification_codes` ve
`refresh_tokens` bilerek filtresiz: giriş sırasında, ortada kullanıcı yokken
okunuyorlar. Tek meşru istisna paylaşımlı listedeki izleme sayacı, `IgnoreQueryFilters()`
ile çağrı yerinde görünür kılınıyor.

**Paylaşımlı listeler → `IListAccess` boğaz noktası.** Handler'lar `lists` tablosunu
hiç sorgulamıyor; ihtiyaç duydukları erişimi isteyip yetkisizse `null` alıyorlar
(`ForMemberAsync` / `ForOwnerAsync` / `ForViewerAsync` / `PollForMemberAsync`).
Query filter kullanılamadı çünkü kurallar tabloya değil **işleme** göre değişiyor —
içerik okumak kabul edilmiş üyelik ister, listenin adını görmek bekleyen davetliye de
açıktır, silmek yalnızca kurucunundur — ve query filter'lar yazmayı hiç kapsamıyor.

**Trigger'lar → `SaveChanges` içinde değişmezlik kontrolleri.** `lists.created_by`
değişmez; `list_members` üzerinde liste/kullanıcı/rol değişmez ve durum yalnızca
sabit bir yol izler: davet yanıtlanabilir, reddedilen davet yeniden gönderilebilir,
katılmak nihaidir. Geçiş kuralı `ListMember` üzerinde, veritabanı olmadan test edilebilir.

Supabase'deki trigger reddettiği için ulaşılamaz kalan **"reddedilmiş daveti yeniden
gönderme"** yolu burada bilerek açıldı.

⚠️ **Faz 4'te korunacak, regresyon riski yüksek davranışlar:**
- `invite_to_list`: "hesap yok" ile "zaten üye" **ayırt edilemez** olmalı — tek `invite_failed`
  hatası (`0021_invite_enumeration_fix.sql`). Aksi halde e-posta enumeration açığı geri gelir.
- `find_user_id_by_email` dışarıya **açılmamalı** (`0022`).
- `get_list_watch_summary` yalnızca **agregat sayı** döner, tekil watch kaydı değil (`0017`).

### Faz 4 — Endpoint'ler
Feature bazlı dikey dilimler. Alt adımlara bölünüşü ve kalan işin ayrıntısı
[docs/plan.md](docs/plan.md) dosyasında.

#### Faz 4a — Kişisel içerik ✅
16 uç: `saved-media`, `watch-log`, `episode-progress`, `recommendation-feedback`.
Faz 3'ün query filter'ları sahipliği zaten hallettiği için handler'lar filtre yazmıyor.

**Store'lar kullanıcı kimliği almıyor.** `ISavedMediaStore`, `IWatchLogStore`,
`IEpisodeProgressStore`, `IRecommendationFeedbackStore` — `IListAccess` ile aynı gerekçe:
çağıranı kendileri çözüyorlar, dolayısıyla bir handler'ın başkası adına satır yazması
mümkün değil. `Movie.Application` EF Core'a bağlı kalmıyor.

**Çakışma bir hata değil.** Zaten kayıtlı bir başlığı kaydetmek, zaten gizlenmiş bir
başlığı gizlemek, zaten işaretli bir bölümü işaretlemek — hepsi başarıyla dönüyor,
çünkü çağıranın istediği sonuç hâlihazırda geçerli. Kararı veritabanına bırakmak
(önce kontrol etmek yerine) yarışa kapalı olmasını sağlıyor. Yanıttaki sayı, istemcinin
yerel durumunun geride kaldığını yine de öğrenmesini sağlıyor.

**Supabase'e göre bilinçli farklar:**
- Tekil `POST /saved-media` çakışmada 409 değil 200 dönüyor. Uç "bu kayıtlı olsun"
  demek; iki cihazdan aynı anda favorilere eklemek hata üretmemeli.
- Saat dilimi taşımayan zaman damgası **reddediliyor** (400). `timestamptz` bir an
  saklıyor; `2024-01-01T20:00:00` bir an belirtmiyor. UTC varsaymak, UTC'de olmayan
  herkesin günlüğünü saatlerce kaydırırdı.
- Enum'lar route/query'de büyük-küçük harf duyarsız okunuyor. Gövdedeki JSON zaten
  öyleydi; minimal API'nin route binding'i değildi ve `favorite` yazımını reddediyordu.

**Toplu uçlarda üst sınır var**: 500 başlık, 2000 bölüm. Aşan istek kırpılmıyor,
reddediliyor — yarım uygulanmış bir içe aktarma, başarısız olandan kötüdür.

#### Faz 4b — Listeler ve üyelik ✅
10 uç. Yetkilendirme `IListAccess`'te kalıyor; veri işini yapan yeni `IListStore`'un
tek liste işleyen her metodu **`MediaList`'in kendisini alıyor, id değil** — o nesneyi
elde etmenin tek yolu `IListAccess`'ten geçtiği için, onu geçirmek kontrolün yapıldığının
kanıtı oluyor. Id alsalardı, handler görünürlüğünü hiç saptamadığı bir listeye uzanabilirdi.

**Yetkisiz her durumda 404, 403 değil.** Ayrı bir "yasak" yanıtı, listenin var olduğunu
onunla hiç ilgisi olmayan birine doğrulardı.

**Supabase'e göre bilinçli farklar:**
- **Katılım kodu yalnızca kabul etmiş üyeye dönüyor.** `lists` üzerindeki satır politikası
  hangi satırın okunabileceğini söyleyebiliyordu ama hangi *kolonun* değil, bu yüzden kod
  bekleyen davetliye de gidiyordu. Kodu bilmek daveti tamamen atlayıp anında üye olmak
  demek (Faz 1'de bu yüzden kriptografik üretece geçmişti); davet kartının ise yalnızca
  isme ihtiyacı var.
- **Kurucu kendi listesinden ayrılamaz** (409). Sahiplik `lists.created_by`'dan okunuyor,
  üyelik satırından değil; ayrılan kurucu listeyi silebilen tek kişi olmaya devam ederken
  içeriğini okuyamaz hale gelirdi. Çıkış yolu listeyi silmek.
- **Zaten ekli bir başlığı eklemek hata değil**, mevcut satır dönüyor — 4a'daki ile aynı
  gerekçe, artı çağıranın soracağı bir sonraki soruyu (kim ekledi) da yanıtlıyor.

**Yol boyunca bulunan hata:** `list_members.role` kolonunda `HasDefaultValue(Member)` vardı
ve `MemberRole.Owner` enum'da ilk sırada, yani CLR varsayılanı. EF, özellik CLR varsayılanını
tuttuğunda kolonu INSERT'ten çıkarıyor; sonuç olarak **kurucunun üyelik satırı `member`
olarak yazılıyordu**. `ValueGeneratedNever()` eklendi; şema değişmedi, migration gerekmedi.
Faz 3 testleri bunu yakalamamıştı çünkü sahiplik zaten rol satırından okunmuyor.

#### Faz 4c — Davetler ve kodla katılma ✅
5 uç. Faz 3'te ⚠️ ile işaretlenen üç davranışın hepsi korundu:

- **`invite_failed` tek yanıt.** "Bu adresin hesabı yok", "zaten davetli" ve "zaten üye"
  aynı durum kodu ve **bayt bayt aynı gövde** ile dönüyor. Testler bunu ayrı ayrı iki kez
  doğruluyor: farklı olan tek şey bile adresleri kayıtlı/kayıtsız diye ayırmaya yeter.
  `cannot_invite_self` ayrı kaldı — yalnızca çağıranın kendi adresi için tetikleniyor.
- **E-postadan kullanıcı bulma dışarı açılmadı.** Arama `IInvitationStore.InviteAsync`'in
  içinde kalıyor ve cevabı metottan çıkmıyor; o soruyu yanıtlayan bir metot yok.
- **Rate limit**: davet ve kodla katılma için 10 dakikada 20 deneme.

**Rate limit hesap bazlı, IP bazlı değil.** Supabase de `auth.uid()` sayıyordu. IP saymak
hem saldırganı az kısıtlar (adres değiştirmek ucuz) hem masumu çok kısıtlar (NAT arkasındaki
herkes tek bütçeyi paylaşır). **Başarısız denemeler de sayılıyor** — başarısız bir yoklama
bedava olsaydı sınır, var olma sebebi olan taramayı sınırlamazdı.

**Kodla katılma reddedilmiş üyeliği siliyor, güncellemiyor.** `join_list_by_code` orijinalinde
`declined`'ı doğrudan `accepted` yapıyordu; Faz 3'ün geçiş kuralı ise bunu reddediyor ve
haklı — kendi reddini üyeliğe çevirmek tam da o kuralın engellediği şey. Ama kodla katılmada
yetki **kodun kendisi**, cevaplanmamış bir davet değil. Bu yüzden satır silinip yenisi
yazılıyor (tek transaction, iki `SaveChanges`; aynı `(list, user)` çiftinin silme ve eklemesi
tek batch'te tekillik indeksiyle yarışırdı). Kural olduğu gibi sıkı kalıyor, kayıt da dürüst:
kimse davet etmedi, `invited_by` boş.

**Yol boyunca bulunan hata:** `UseRateLimiter()` `UseAuthentication()`'dan **önce**
çalışıyordu, dolayısıyla `context.User` boştu ve hesap bazlı sayım sessizce IP'ye düşüyordu.
Sıra düzeltildi. Token'ı burada okumak aynı zamanda claim'in doğrulanmış olmasını sağlıyor —
aksi halde çağıran `sub`'ı değiştirip kendi bütçesini tazeleyebilirdi.

#### Faz 4d — Anketler ✅
3 uç. Anketin kapalı olduğu hiçbir yerde saklanmıyor — `Deadline` geçtiyse kapalı; bu yüzden
kapatan bir arka plan işi yok, kontrol yalnızca oy verilirken yapılıyor.

**Yol boyunca bulunan hata:** `start_list_poll`, aday olarak verilen `list_item_id`'lerin
**o listeye ait olduğunu hiç doğrulamıyordu** — FK yalnızca satırın bir yerde var olduğunu
garanti ediyor. İki listeye birden üye olan biri, B listesinin bir içeriğini A'nın anketine
aday koyabilirdi. `.NET` tarafı adayları listenin kendi `list_items`'ı ile kesişim alarak
doğruluyor; uymayan istek `invalid_candidate` ile reddediliyor.

Ayrıca: aynı öğeyi iki kez aday göstermek iki aday saymıyor (`Distinct()` sonra sayılıyor),
oy `candidate_id`'nin gerçekten bu ankete ait olduğu kontrol edilerek kabul ediliyor —
başka bir anketin aday id'si burada geçmiyor.

#### Faz 4e — İzleme özeti ✅
1 uç, `GET /lists/{id}/watch-summary`. Faz 3'te tanımlanan tek meşru çapraz kullanıcı okuması;
`IgnoreQueryFilters()` tam olarak bu tek çağrı noktasında kullanılıyor.

**Yalnızca içerik başına sayı dönüyor**, tekil kayıt asla — `0017`'nin yorumunun söylediği
gibi. Sorgu `watch_log`'dan yalnızca `(media_id, media_type, user_id)` üçlüsünü seçiyor;
tarih, puan, not asla sorgunun dışına çıkmıyor. Sayı **kişi başına bir**, kayıt başına değil
— aynı filmi dört kez izleyen biri grup için bir kişi sayılıyor (`Distinct()` + `GroupBy`).
Üyelikten ayrılan birinin geçmiş izlemesi artık sayılmıyor, çünkü üyelik okuma anında
kontrol ediliyor, satıra o yazıldığında damgalanmıyor.

RPC → endpoint eşlemesi (kalan alt adımlar):

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
| `delete_account` | `DELETE /me` — *Faz 2'de yapıldı* |

Rate limit (`0012`, `0014`: 10 dakikada 20 deneme) → ASP.NET Core rate limiting middleware.

### Faz 5 — SignalR ✅
`ListHub`, grup adı `list:{listId}`. Handler'lar mutasyondan sonra gruba yayın yapar.
Supabase Realtime'da DELETE olayları için gereken `REPLICA IDENTITY FULL` hilesi (`0005`)
burada gereksiz — sunucu zaten silinen satırın tamamını biliyor.

**`IListEventPublisher`, `IListAccess`'in yayın karşılığı.** Handler'lar `IHubContext`'e hiç
dokunmuyor; ihtiyaç duydukları olayı isterler, uygulaması (`SignalRListEventPublisher`)
`Movie.Infrastructure`'da. Aynı `Movie.Application`'ın EF Core'a değil soyutlamalara bağlı
kalması gerekçesiyle — SignalR de bir altyapı detayı.

**`ListHub` `Movie.Infrastructure/Realtime` altında, `Movie.Api` altında değil.**
`HttpContextCurrentUser`'la aynı desen: ASP.NET Core'a özgü bir sınıf, soyutlamanın yanına
değil somutlaştığı katmana yazıldı. Bunun bedeli `Movie.Infrastructure`'ın (düz bir sınıf
kütüphanesi) `Hub`/`IHubContext` görebilmesi için `FrameworkReference`
(`Microsoft.AspNetCore.App`) alması — SignalR'ın sunucu tarafı NuGet paketi olarak değil
paylaşılan framework olarak dağıtıldığı için.

**Gruba katılmak `IListAccess.ForMemberAsync`'ten geçiyor**, REST uçlarıyla birebir aynı
kontrol. Üyesi olmadığı bir listeye katılmaya çalışan `HubException("not_a_member")` alıyor —
listenin hiç var olmamasıyla aynı yanıt, 404'lerin arkasındaki gerekçe burada da geçerli.

**Olay yükleri karışık: bazıları veri taşıyor, bazıları çıplak sinyal.** İçerik
eklendi/çıkarıldı olayları handler'ın zaten oluşturduğu DTO'yu taşıyor (bedavaya geliyor).
Üye değişti ve anket güncellendi ise yalnızca "git yeniden oku" diyor — bu ikisinin arkasında
birden fazla farklı sebep var (davet, kabul, çıkarma / anket başlatma, oy), her birine ayrı
bir yük şekli tanımlamak REST uçlarının DTO'larını burada tekrar etmek olurdu.

**Liste silindi planın yazılı listesinde yoktu, eklendi.** O anda listeye bakan bir istemci
"içerik/üye/anket" olaylarından hiçbirini almadan sessizce takılı kalırdı; silme de bir
mutasyon, atlanacak bir sebep yoktu.

**JWT sorgu dizesinden yalnızca `/hubs` altında okunuyor.** Tarayıcının WebSocket el
sıkışması `Authorization` başlığı taşıyamıyor, SignalR istemcisi bu yüzden token'ı
`access_token` sorgu parametresine koyuyor. Başka hiçbir yolda okunmuyor — orada token URL'e
girmenin (loglara, tarayıcı geçmişine) hiçbir karşılığı yok, header zaten kullanılabiliyor.

**Testler `Microsoft.AspNetCore.SignalR.Client`'la `WebApplicationFactory`'nin
`TestServer`'ına bağlanıyor**, `HttpTransportType.LongPolling`'e zorlanarak — `TestServer`'ın
altında gerçek bir soket yok, SignalR'ın önce denediği WebSocket'in üzerinde çalışacağı bir
şey yok.

**Yol boyunca bulunan hata: `IHttpContextAccessor` bir hub metodu içinde `null` dönüyordu.**
Bağlantı `[Authorize]`'ı geçiyor (JWT doğrulanmış, `Context.User` dolu) ama
`HttpContextCurrentUser`'ın okuduğu `IHttpContextAccessor.HttpContext` boş kalıyordu —
sonuç, giriş yapmış her kullanıcının `ForMemberAsync`'ten `null` alması, yani kendi listesine
bile katılamamasıydı. SignalR, ASP.NET Core'un normal istek hattının aksine, bir hub metodu
çağrısına isteğin `HttpContext`'ini otomatik akıtmıyor — bu `IListAccess`'in üzerine kurulu
her şeyi sessizce kırıyordu. Çözüm `HttpContextPropagationHubFilter` (`IHubFilter`): her hub
metodu çağrısından önce `accessor.HttpContext`'i `Context.GetHttpContext()`'ten (bu her zaman
doğru döner) tazeliyor. Tek bir yerde düzeltildi, böylece `ListHub`'ın hiçbir metodu bunu
bilmek zorunda kalmıyor ve `IListAccess` hiç değişmedi.

### Faz 6 — E-posta ✅
İki Edge Function → tek `IEmailSender` (Brevo API). Şablonlar `send-auth-email/emailTemplates.ts`
(Faz 2'de taşınmıştı) ve `send-list-invite-email/index.ts`'den taşındı.

**`BrevoEmailSender` tek bir yerde Brevo'yu biliyor.** `IVerificationEmailSender` ve
yeni `IListInviteEmailSender`, `IEmailSender`'ın üzerine kurulu — ikisi de şablon
oluşturur, gönderimi devreder; hangi sağlayıcı olduğunu bilmezler. `AddInfrastructure`
artık production'da bilerek hata fırlatmıyor, bunun yerine `Brevo:ApiKey` ve
`Brevo:SenderEmail` yapılandırılmamışsa açılışta hata veriyor — `Jwt:SigningKey`
kontrolüyle aynı gerekçe.

**Davet e-postası gönder-ve-unut, doğrulama kodu değil.** `ListInviteEmailSender`
`IEmailSender.SendAsync`'i kendi içinde yakalayıp logluyor; bir teslimat hatası zaten
var olan bir daveti geçersiz kılmamalı, tıpkı Supabase edge function'ının davrandığı gibi.
`BrevoEmailSender`'ın kendisi hata fırlatmaya devam ediyor — doğrulama kodu hiç
gitmezse kayıt/parola sıfırlama akışının bunu bilmesi gerekiyor.

**İnviter etiketi ve liste adı artık HTML-encode ediliyor.** Edge function ikisini de
çiğ interpolasyonla e-posta gövdesine yazıyordu; görüntü adı ve liste adı kullanıcı
girdisi olduğu için burada `WebUtility.HtmlEncode`'dan geçiriliyor — Faz 4d'de anket
adayına yapılan doğrulamayla aynı gerekçe.

**`InvitationStore.WithProfileAsync` artık `InvitedBy`'ı da yüklüyor.** E-postanın
"filanca seni davet etti" satırı için gerekiyordu; önceden yalnızca davet edilenin
profili yükleniyordu.

### Faz 7 — Mobil istemci geçişi
`lib/supabase/*` → `lib/api/*` (token yenileme interceptor'lı fetch istemcisi).
`stores/*.ts` güncellenir; mevcut jest testleri sözleşme kontrolü olarak kullanılır.

### Faz 8 — Dağıtım
Dockerfile, hosting, CI.

## Yerel geliştirme

Her şeyi Docker'da çalıştırmak:

```bash
docker compose up -d --build
```

| Adres | Ne |
|---|---|
| http://localhost:5080/scalar/v1 | API dokümantasyonu (Scalar) |
| http://localhost:5080/openapi/v1.json | Ham OpenAPI belgesi |
| http://localhost:8090 | Veritabanı arayüzü (Adminer) |
| localhost:5435 | Postgres |

Adminer'ın giriş formu varsayılan olarak MySQL'e ayarlı geliyor. Doğrudan
PostgreSQL'e ayarlı ve alanları dolu açmak için:

```
http://localhost:8090/?pgsql=db&username=movie&db=movie
```

Şifre: `movie_dev_password`. Adminer'ın kendi hesabı yok, doğrudan Postgres
kimlik bilgileriyle giriliyor — bu yüzden **yalnızca geliştirme makinesinde**
çalıştırılmalı.

Migration'lar geliştirmede uygulama açılışında uygulanıyor, ayrı bir adım gerekmiyor.

Sadece veritabanını Docker'da, API'yi yerelde çalıştırmak:

```bash
docker compose up -d db
dotnet run --project src/Movie.Api          # http://localhost:5294
docker exec -it movie-db psql -U movie -d movie
```

Postgres host tarafında 5432 değil **5435**: bu makinede başka projeler 5432/5433/5434'ü
zaten kullanıyor. Konteyner içinde 5432 dinlemeye devam ediyor, dolayısıyla `api` servisi
`db:5432`'ye bağlanıyor. `dotnet ef` komutları için bağlantı dizesi `MOVIE_DB_CONNECTION`
ortam değişkeniyle geçersiz kılınabilir.

Compose dosyasındaki şifre ve JWT anahtarı yalnızca yerel geliştirme içindir. Production'da
`Jwt__SigningKey` ortamdan gelmeli; eksik veya 32 bayttan kısaysa uygulama açılmaz.
