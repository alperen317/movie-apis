# Auth Test Protokolü

Kimlik doğrulama uçlarını elle geçmek için sıralı senaryo. Adımlar durum taşıyor —
her biri bir öncekinin ürettiği kodu ya da token'ı kullanıyor, bu yüzden sırayla
ilerlemek gerekiyor.

Taban adres: `http://localhost:5080`
Scalar arayüzü: `http://localhost:5080/scalar/v1`

Bu dosya protokolün tek kaynağıdır; uçlar değiştikçe burası güncellenir.

---

## Başlamadan

### 1. Yığını kontrol et

```bash
docker compose ps
```

`movie-api`, `movie-db` ve `movie-adminer` üçü de *Up* olmalı. Değilse:

```bash
docker compose up -d
```

### 2. Doğrulama kodunu okumayı öğren

E-posta gerçekten gönderilmiyor; geliştirme göndericisi (`LoggingEmailSender`)
mesajı API loguna yazıyor. Kodu oradan çekeceksin.

**PowerShell**

```powershell
(docker compose logs api --tail 200 |
  Select-String 'id="verification-code"[^>]*>(\d{6})<').Matches |
  Select-Object -Last 1 | ForEach-Object { $_.Groups[1].Value }
```

**bash**

```bash
docker compose logs api --tail 200 \
  | grep -o 'id="verification-code"[^>]*>[0-9]\{6\}<' \
  | tail -1 | grep -o '[0-9]\{6\}'
```

Altı haneli bir sayı dönmeli. Dönmüyorsa henüz kayıt isteği atmamışsındır.

### ⚠️ İstek bütçesi

**Kayıt ve yeniden gönderme uçları 10 dakikada 5 istekle sınırlı** (IP başına).
Bu protokolde ikisini toplam 4 kez kullanıyorsun. Baştan sona birkaç kez
koşacaksan altıncı denemede `429` alırsın — bu bir hata değil, sınırın
çalıştığının kanıtı. On dakika bekle ya da:

```bash
docker compose restart api
```

---

## A · Mutlu yol

Sıfırdan hesap açıp oturum açana ve oturumu kapatana kadar. Her adım bir
öncekinin çıktısını kullanıyor.

### 01 · Kayıt ol

`POST /auth/register`

```json
{
  "email": "test1@example.com",
  "password": "correct horse battery"
}
```

**Beklenen: `202`** — gövde boş. Hesap oluştu ama henüz doğrulanmadı; log'a bir
kod düştü.

> Şimdi elinde: **doğrulanmamış hesap**

### 02 · Doğrulamadan giriş dene

`POST /auth/login`

```json
{
  "email": "test1@example.com",
  "password": "correct horse battery"
}
```

**Beklenen: `401`** — `title: "email_not_confirmed"`. Şifre doğru ama adres
doğrulanmamış.

> **Neden bu mesaj güvenli:** bu cevabı ancak şifreyi zaten bilen biri alabilir.
> Yanlış şifreyle denersen `invalid_credentials` alırsın — bunu B1'de göreceksin.

### 03 · Kodu logdan al

"Başlamadan · 2" komutunu çalıştır. Kodu bir yere not et.

> Şimdi elinde: **doğrulama kodu** · 1 saat geçerli · 5 yanlış deneme hakkı

### 04 · E-postayı doğrula

`POST /auth/verify-email`

```json
{
  "email": "test1@example.com",
  "code": "<03'te aldığın kod>"
}
```

**Beklenen: `200`** — `accessToken`, `expiresAt`, `refreshToken` döner.

> **Doğrulama doğrudan oturum açıyor.** Supabase de böyle yapıyordu — aksi halde
> kullanıcıdan iki ekran önce belirlediği şifreyi tekrar istemek gerekirdi.

> Şimdi elinde: **accessToken** (1 saat) · **refreshToken** (60 gün)

### 05 · Token'ı kullan

`GET /me` · `Authorization: Bearer <accessToken>`

**Beklenen: `200`** — `{ "id": "...", "email": "test1@example.com" }`

> Scalar kullanıyorsan sağ üstteki **Bearer Token** alanına yapıştırman yeterli,
> her istekte otomatik gönderilir.

### 06 · Token'ı yenile

`POST /auth/refresh`

```json
{ "refreshToken": "<04'teki refreshToken>" }
```

**Beklenen: `200`** — yeni bir çift döner. `refreshToken` eskisinden farklı
olmalı, karşılaştır.

> **Rotasyon:** her kullanımda token değişiyor. Bu, çalınmış bir kopyanın fark
> edilmesini sağlayan mekanizma — bir sonraki adımda göreceksin.

> Şimdi elinde: **yeni token çifti** · eski refreshToken artık ölü

### 07 · Harcanmış token'ı tekrar dene

`POST /auth/refresh`

```json
{ "refreshToken": "<04'teki ESKİ refreshToken>" }
```

**Beklenen: `401`** — `title: "invalid_refresh_token"`

> **Bu adım sessiz bir yan etki bırakıyor:** rotasyonla harcanmış bir token'ın
> yeniden ortaya çıkması, aynı sırrın iki kopyası olduğu anlamına gelir. Hangisinin
> hırsız olduğu bilinemediği için **o kullanıcının tüm oturumları düşürülür.**
> Yani 06'da aldığın yeni token da artık ölü.

### 08 · Yeni token'ın da öldüğünü gör

`POST /auth/refresh`

```json
{ "refreshToken": "<06'da aldığın yeni refreshToken>" }
```

**Beklenen: `401`** — 07'deki replay bunu da iptal etti. Devam etmek için tekrar
giriş yapman gerekiyor.

### 09 · Normal giriş yap

`POST /auth/login`

```json
{
  "email": "test1@example.com",
  "password": "correct horse battery"
}
```

**Beklenen: `200`** — artık doğrulanmış hesap; token çifti döner.

> Şimdi elinde: **temiz bir oturum**

### 10 · Çıkış yap

`POST /auth/logout`

```json
{ "refreshToken": "<09'daki refreshToken>" }
```

**Beklenen: `204`** — gövde yok. Ardından aynı token'la `/auth/refresh` dene → `401`

> **07'den farkı:** çıkışla iptal edilen token tekrar sunulursa yalnızca reddedilir,
> diğer cihazlar etkilenmez. Sadece rotasyonla harcanmış token'ın replay'i hırsızlık
> sayılır.

---

## B · Güvenlik senaryoları

Bunlar sıraya bağlı değil, ama her biri **yeni bir e-posta** ister
(`test2@…`, `test3@…`). Asıl mesele dönen cevabın kendisi değil, **iki farklı
durumun aynı cevabı vermesi**.

### B1 · Yanlış şifre ile bilinmeyen adres aynı cevabı verir

`POST /auth/login` · iki kez

```json
{ "email": "test1@example.com",  "password": "yanlis" }
{ "email": "hicyok@example.com", "password": "correct horse battery" }
```

**Beklenen: `401`** — ikisi de `invalid_credentials`. Ayırt edilemez olmaları şart.

> Farklı cevap verselerdi bu uç, "bu adresin hesabı var mı" sorusunu herkese
> yanıtlayan bir araca dönüşürdü.

### B2 · Zayıf şifre, adres dolu olsa da reddedilir

`POST /auth/register`

```json
{ "email": "test1@example.com", "password": "kisa" }
```

**Beklenen: `400`** — şifre politikası hatası. Kayıtlı olmayan bir adresle de
**aynı** cevabı verir.

> **Sıra önemli:** şifre, hesap aramasından *önce* doğrulanıyor. Ters sırada
> olsaydı, bilerek zayıf bir şifre gönderip cevaba bakarak adresin kayıtlı olup
> olmadığı anlaşılırdı.

### B3 · Doğrulanmış adrese kayıt: aynı cevap, sıfır e-posta

`POST /auth/register`

```json
{
  "email": "test1@example.com",
  "password": "correct horse battery"
}
```

**Beklenen: `202`** — yeni bir adresle aynı cevap. Ama **loga yeni kod düşmez**,
kontrol et.

> E-posta gönderilseydi bu uç, istenen kişiye istendiği kadar mesaj yollamanın
> yolu olurdu.

### B4 · Yeniden gönderme öncekini öldürür

Önce `test2@example.com` ile kayıt ol ve ilk kodu not et.

`POST /auth/resend-verification`

```json
{ "email": "test2@example.com" }
```

**Beklenen: `202`** — yeni kod gelir. Eski kodla doğrulama dene → `400`
`invalid_code`

> Aksi halde her "tekrar gönder" ortalıkta çalışan bir kod daha bırakırdı.

### B5 · Beş yanlış denemeden sonra doğru kod da ölür

Önce `test3@example.com` ile kayıt ol ve doğru kodu not et.

`POST /auth/verify-email` · 5 kez yanlış kodla

```json
{ "email": "test3@example.com", "code": "000000" }
```

**Beklenen: `400`** — ilk 4'ü `invalid_code`, 5.'si `too_many_attempts`. Sonra
**doğru kodu dene** → yine `too_many_attempts`.

> **Asıl korumanın olduğu yer burası.** Altı hane bir milyon ihtimal demek; deneme
> sayısı sınırlı olmasa tahmin edilebilir bir sır olurdu. Doğru kodun da ölmesi
> kritik — yoksa saldırgan tutturana kadar denerdi.

### B6 · Bir cihazdan çıkış diğerini düşürmez

İki kez giriş yap (iki ayrı `refreshToken`), birinciyle çıkış yap, ikinciyle
refresh dene.

**Beklenen: `200`** — ikinci oturum ayakta kalır.

> Telefondan çıkmak tabletteki oturumu kapatmamalı. Bu davranış `refresh_tokens`
> tablosunun kullanıcı başına tek satır tutmamasının sebebi.

### B7 · Şifre sıfırlama tüm oturumları kapatır

Önce `test1@example.com` ile **iki kez** giriş yap, iki `refreshToken`'ı da not et.

`POST /auth/forgot-password`

```json
{ "email": "test1@example.com" }
```

**Beklenen: `202`** — loga bir sıfırlama kodu düşer.

Sonra `POST /auth/reset-password`

```json
{
  "email": "test1@example.com",
  "code": "<sıfırlama kodu>",
  "newPassword": "a different long one"
}
```

**Beklenen: `204`** — ardından:

- Eski şifreyle giriş → `401`
- Yeni şifreyle giriş → `200`
- **Not ettiğin iki refreshToken ile refresh** → ikisi de `401`

> İnsanlar şifresini genellikle **başkası bildiği için** sıfırlar. O kişinin
> oturumu ayakta kalsaydı işlemin anlamı kalmazdı.

### B8 · Sıfırlama kodu e-posta doğrulamaya yaramaz

Doğrulanmış bir hesap için `forgot-password` çağır, gelen kodu al ve
`verify-email` ile kullanmayı dene.

**Beklenen: `400`** — `invalid_code`

> Kodlar amaca bağlı. Bir akış için gönderilen kod diğerinde harcanamaz.

### B9 · Zayıf yeni şifre kodu yakmaz

Sıfırlama kodunu al, `reset-password`'ü **kısa** bir şifreyle çağır → `400`.
Sonra **aynı kodla** düzgün bir şifre gönder.

**Beklenen: `204`** — kod hâlâ geçerli.

> Şifre politikası, kod harcanmadan önce kontrol ediliyor. Ters sırada olsaydı
> bir yazım hatası kullanıcıyı yeni kod istemek için posta kutusuna geri gönderirdi.

---

## C · Sınırlar

Bu senaryoyu en sona bırak — istek bütçesini tüketiyor ve diğer adımları
engelleyebilir.

### C1 · Altıncı kayıt isteği reddedilir

`POST /auth/register` · her seferinde **farklı** e-posta ile 6 kez

**Beklenen:** ilk 5 istek `202`, altıncı `429`.

> **E-postayı her seferinde değiştirmen önemli:** sınır çağıranın adresine göre
> tutuluyor, gönderdiğin e-postaya göre değil. Aksi halde e-postayı değiştirerek
> sınırı aşmak mümkün olurdu.

---

## D · Hesap

Bu bölüm `Authorization: Bearer <accessToken>` başlığı ister. Token'ı A bölümünün
04 veya 09. adımından alabilirsin. Scalar kullanıyorsan sağ üstteki **Bearer
Token** alanına bir kez yapıştırman yeterli.

### D1 · Profili oku

`GET /me`

**Beklenen: `200`**

```json
{
  "id": "0198…",
  "email": "test1@example.com",
  "displayName": null,
  "avatarVariant": "beam",
  "avatarSeed": null,
  "watchRegion": null
}
```

> Yeni hesapta `displayName`, `avatarSeed` ve `watchRegion` boş; `avatarVariant`
> varsayılan olarak `beam`.

### D2 · Profili güncelle

`PUT /me`

```json
{
  "displayName": "Alperen",
  "avatarVariant": "bauhaus",
  "avatarSeed": "seed-42",
  "watchRegion": "tr"
}
```

**Beklenen: `200`** — güncellenmiş profil döner. `GET /me` ile tekrar oku,
kalıcı olduğunu gör. `watchRegion` **`TR`** olarak dönmeli — büyük harfe
çevriliyor.

### D3 · Eksik alan silinir

`PUT /me` — bu sefer yalnızca `avatarVariant` gönder:

```json
{ "avatarVariant": "ring" }
```

**Beklenen: `200`** — ardından `GET /me`:

- `displayName` → `null`
- `avatarSeed` → `null`
- `watchRegion` → `null`

> **Bu uç `PATCH` değil `PUT`.** Gönderdiğin gövde profilin düzenlenebilir
> kısmının tamamının yerine geçiyor; bir alanı yazmamak onu **siliyor**.
> Supabase istemcisi yalnızca değişen alanları gönderiyordu, burada davranış
> farklı — Faz 7'de istemci buna göre yazılacak.

### D4 · Geçersiz değerler reddedilir

`PUT /me` ile iki ayrı deneme:

```json
{ "avatarVariant": "beam", "displayName": "<61 karakter>" }
{ "avatarVariant": "beam", "watchRegion": "TUR" }
```

**Beklenen: `400`** — biri ad uzunluğu, diğeri bölge kodu iki harf olmadığı için.

### D5 · Hesabı sil

`DELETE /me`

**Beklenen: `204`** — sonra **aynı token'la**:

- `GET /me` → `401`
- `POST /auth/login` (aynı e-posta ve şifre) → `401`

> **Burası önemli:** token hâlâ geçerli imzalı ve süresi dolmamış. Ama `/me`
> artık veritabanından okuyor, satır olmadığı için 401 dönüyor. Bu, hesap
> silmenin anında etkili olmasını sağlayan şey.

Silmenin ne kadarını götürdüğünü görmek istersen Adminer'dan bak — kullanıcının
refresh token'ları, doğrulama kodları, kaydettiği içerikler, liste üyelikleri,
hepsi cascade ile gider.

---

## Referans

| Kural | Değer | Nerede |
|---|---|---|
| Şifre en az | 8 karakter | Başka şart yok — büyük harf, rakam, sembol aranmıyor |
| Doğrulama kodu | 6 hane · 1 saat | Tek kullanımlık, 5 yanlış deneme hakkı |
| Access token | 1 saat | `expiresAt` alanında döner |
| Refresh token | 60 gün | Her kullanımda değişir |
| Hesap kilidi | 10 hata · 15 dk | Kilitliyken şifre doğru olsa da giriş yok |
| Kayıt / yeniden gönderme | 5 istek / 10 dk | IP başına |
| Giriş / kod doğrulama | 20 istek / 10 dk | IP başına |
| Refresh / çıkış | sınırsız | Taşıdıkları sır 256 bit, tahmin edilemez |

### Hata başlıkları

| `title` | Uç | Anlamı |
|---|---|---|
| `invalid_credentials` | `/auth/login` | Yanlış şifre **veya** var olmayan hesap — ayırt edilemez |
| `email_not_confirmed` | `/auth/login` | Şifre doğru, adres doğrulanmamış |
| `locked_out` | `/auth/login` | 10 başarısız denemeden sonra 15 dakika |
| `invalid_code` | `/auth/verify-email` | Yanlış kod, bilinmeyen adres veya zaten doğrulanmış hesap |
| `code_expired` | `/auth/verify-email` | Kod 1 saatlik ömrünü doldurdu |
| `too_many_attempts` | `/auth/verify-email` | 5 yanlış deneme — kod tamamen öldü |
| `invalid_code` | `/auth/reset-password` | Yanlış kod, bilinmeyen adres veya doğrulanmamış hesap |
| `code_expired` | `/auth/reset-password` | Kod 1 saatlik ömrünü doldurdu |
| `too_many_attempts` | `/auth/reset-password` | 5 yanlış deneme |
| `invalid_refresh_token` | `/auth/refresh` | Bilinmeyen, süresi dolmuş, iptal edilmiş veya harcanmış token |

---

## Hata değil: access token iptal edilemez

Test ederken şuna denk geleceksin ve bozukmuş gibi görünecek:

- İki kez giriş yaparsın; **ilk access token hâlâ `200` döner**
- Çıkış yaparsın; **aynı access token hâlâ `200` döner**

İkisi de beklenen davranış. İki token temelden farklı çalışıyor:

| | Access token | Refresh token |
|---|---|---|
| Nerede saklanıyor | Hiçbir yerde — kendi kendini taşıyor | `refresh_tokens` tablosunda satır |
| Nasıl doğrulanıyor | İmza + son kullanma tarihi | Veritabanı sorgusu |
| İptal edilebilir mi | **Hayır** | Evet |

Access token bir JWT: sunucu onu doğrularken veritabanına hiç bakmıyor, imza
tutuyor ve süresi dolmamışsa geçerli sayıyor. Onu geçersiz kılacak bir mekanizma
yok. Aynı anda birden çok geçerli access token bulunması zaten istenen davranış —
telefon ve tablet aynı anda açık olabilsin diye.

Supabase'de de aynıydı: `signOut()` refresh token'ı iptal ediyor, JWT süresi
dolana kadar çalışmaya devam ediyordu.

**Pratikte ne anlama geliyor:** çıkış yapmak kalıcı erişimi keser (refresh token
ölür), ama eldeki access token en fazla bir saat daha çalışır. Şifre sıfırlama
kullanıcının tüm refresh token'larını iptal eder, yani aynı pencere orada da
geçerli.

**Tek istisna: hesap silme.** `GET /me` token'ın claim'lerine değil veritabanına
bakıyor, dolayısıyla satır silindiğinde token anında işe yaramaz hale geliyor
(D5). Bu, token'ın iptal edildiği anlamına gelmiyor — imzası hâlâ geçerli — ama
arkasında kullanıcı kalmadığı için servis edilmiyor.

Bu pencereyi tamamen kapatmak isteseydik iki yol vardı: access token ömrünü
kısaltmak, ya da **her** istekte token'ı veritabanından doğrulamak. İkincisi
JWT'nin stateless olma avantajını tümden götürdüğü için tercih edilmedi.

---

## Temizlik

Protokol bitince oluşturduğun hesapları silmek istersen:

```bash
docker exec movie-db psql -U movie -d movie -c "delete from users;"
```

Kullanıcıyı silmek token'larını, kodlarını ve tüm içeriğini de siler — cascade
tanımlı.
