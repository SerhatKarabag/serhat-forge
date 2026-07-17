# Serhat Forge

**Aynı altyapıyı her oyunda yeniden kurmadan gerçek projelere başlamak için production odaklı Unity 6 temeli.**

[English](README.md) · [Türkçe](README.tr.md) · [Başlangıç rehberi](docs/GETTING_STARTED.md) · [Yetenekler](docs/FEATURES.md) · [Mimari](docs/ARCHITECTURE.md)

> **Sürüm durumu:** `0.1.0-preview.1`
>
> Serhat Forge değerlendirme ve yeni proje başlangıcı için hazırdır. Oyuna test edilmiş bir mühendislik temeli verir; o oyunu otomatik olarak production-ready yapmaz. Etkinleştirilen her platform, servis, mağaza ve backend proje özelinde yapılandırılmalı ve release öncesinde doğrulanmalıdır.

## Serhat Forge ne işe yarar?

Unity projelerinde gameplay başlamadan önce genellikle aynı işler tekrarlanır: dependency composition, kontrollü başlangıç akışı, içerik yükleme, kayıt sistemi, localization, analytics sınırları, CI ve üçüncü parti entegrasyonları. Serhat Forge bu genel ihtiyaçları tek projede bir araya getirir; oyuna özel mekanikleri ve gizli bilgileri template dışında bırakır.

- **Çalışan bir composition root ile başlarsın.** Zenject, proje ömürlü servisler, Addressables başlangıcı ve gözlemlenebilir startup pipeline hazır bağlıdır.
- **Hataları kontrollü yönetirsin.** Startup adımları required/optional davranış, timeout, cooperative cancellation, retry ve açık hata sonucu destekler.
- **Offline senaryoları baştan düşünülür.** Versioned save/recovery, analytics outbox ve dayanıklı backend yardımcıları bulunur.
- **Entegrasyonları bilinçli açarsın.** Üçüncü parti entegrasyonlar provider, ortam ayarları ve composition-root bağlantıları tamamlanana kadar davranışsal olarak pasiftir; paketle gelen Unity IAP kodu compile olur fakat satın alma akışı başlatmaz.
- **Oyun sana ait kalır.** Template gameplay loop, progression, economy, mağaza kataloğu, production credential veya canlı servis ortamı içermez.

Bu yapı özellikle mobil veya servis bağlantılı bir oyun geliştiren, genel altyapıyı tekrar kullanmak isteyen ve domain mimarisini kendisi yönetecek geliştirici ve ekipler içindir. Bitmiş oyun, no-code kit veya mevcut bir Unity projesinin üzerine topluca kopyalanacak bir SDK paketi değildir.

## Başlıca yetenekler

| Alan | Sağlanan temel |
|---|---|
| Unity | Unity `6000.3.14f1`, URP `17.3.0`, Input System, Addressables `2.9.1` ve Test Framework |
| Composition | Zenject/Extenject `ProjectContext`, proje ve bootstrap installer'ları, test edilebilir servis sınırları |
| Startup | Async içerik başlangıcı, opsiyonel katalog kontrolü, preload, sıralı startup adımları, retry, cancellation ve hata durumları |
| Persistence | Versioned JSON, migration, SHA-256 bütünlük kontrolü, `.tmp`/`.bak` recovery ve transactional restore kontratları |
| Genel runtime | Prefab loader, pooling, feature gates, audio, localization, tutorial/scenario, UI/navigation, haptics ve kamera yardımcıları |
| Servis temelleri | Persistent outbox destekli analytics; transport-independent backend resilience, coalescing, circuit breaker ve outbox |
| Opsiyonel entegrasyonlar | Compile-gated auth, ads, Firebase Analytics, PlayFab, Google Play Games, DOTween, SRDebugger ve native mobil köprüler; varsayılan olarak bağlanmamış ayrı Unity IAP assembly'si |
| Kalite | EditMode/PlayMode testleri, repository doğrulayıcı, cloud .NET testleri, GitHub Actions ve mobil build giriş noktaları |

Hangi özelliğin varsayılan olarak çalıştığını, hangisinin oyuna bağlanması gerektiğini ve hangisinin yalnızca opt-in olduğunu [yetenek ve hazırlık matrisinden](docs/FEATURES.md) kontrol et.

## Yeni oyuna başlama

Önce boş Unity projesi oluşturma. Serhat Forge; `Assets`, `Packages` ve `ProjectSettings` dahil olmak üzere zaten tam bir Unity projesidir.

1. GitHub'da **Use this template → Create a new repository** seçeneğini kullan.
2. Yeni repository'ye oyunun adını ver ve oluşan repository'yi clone et:

   ```bash
   git clone https://github.com/<hesap>/<yeni-oyun>.git
   ```

3. Unity Hub'da **Add → Add project from disk** seç ve `Assets`, `Packages`, `ProjectSettings` klasörlerini içeren repository kökünü göster.
4. Projeyi Unity `6000.3.14f1` ile aç; Package Manager çözümlemesi ve ilk import tamamlanana kadar bekle.
5. **Tools → Serhat Forge → Setup → Project Settings** menüsünü çalıştır. Company/product adı, bundle identifier, sürüm/build numaraları, sample sahnenin ilk sırada olup olmayacağı ve mobil IL2CPP varsayımlarını ayarla.
6. **Tools → Serhat Forge → Setup → Repair Zenject Composition** komutunu çalıştır. Komut tekrar çalıştırılabilir ve sonradan eklediğin installer'ları korur.
7. `Assets/Scenes/SampleScene.unity` sahnesini aç ve Play Mode'a gir.

Başarılı ilk çalıştırmada **Serhat Forge** smoke panelinde `Boot state: Ready` ve `Content initialized: True` görünür. **Toggle audio mute** çalışır; rewarded ad butonu provider yapılandırılmadığı için pasif kalır. Console'da hata olmamalıdır.

Örnek sahneyi kendi oyununa dönüştürme, ilk sistemleri bağlama ve production checklist için [detaylı başlangıç rehberine](docs/GETTING_STARTED.md) devam et.

## Güvenli başlangıç durumu

- Reklamlar ve provider-specific runtime kodları kapalıdır.
- Unity IAP kodu Purchasing paket bağımlılığı nedeniyle compile edilir; oyun kendi catalog, store ve doğrulanmış backend bağlantılarını composition root'ta kurana kadar store client veya purchase service oluşturulmaz.
- UnityConnect, Purchasing ve Ads otomatik initialization ayarları kapalıdır.
- Remote Addressables catalog build/kontrolü kapalıdır; local içerik player ile build edilir.
- Addressables gruplarında oyun içeriği yoktur; remote URL çalışmayan placeholder'dır.
- UGS environment ID, signing verisi, console ID, servis credential ve production secret bulunmaz. Unity'nin serialize ettiği varsayılan PS4 passcode yalnızca public placeholder'dır; PS4 content/NP title kimliği boş kalır.
- Application identifier placeholder'dır ve değiştirilmelidir.
- Frame-rate policy kapalıdır; Unity proje ayarlarına müdahale etmez.

Bu varsayımlar yanlışlıkla servis çağrısı yapılmasını veya secret paylaşılmasını önler. Oyunun etkinleştirdiği servisleri yapılandırma ve test etme sorumluluğunu ortadan kaldırmaz.

## Dokümantasyon

İngilizce `docs/` belgeleri teknik detaylar için canonical kaynaktır; böylece iki dilde birbirinden kopan teknik talimatlar oluşmaz. Bu Türkçe sayfa template'i tanımak ve doğru başlangıç akışını izlemek için güncel tutulur.

- [Getting Started](docs/GETTING_STARTED.md): repository oluşturma, Unity'de açma, ilk doğrulama ve sample'dan oyuna geçiş
- [Features](docs/FEATURES.md): yetenek kapsamı, varsayılan durum ve shipping sorumluluğu
- [Architecture](docs/ARCHITECTURE.md): composition/startup akışı, sahiplik sınırları ve klasör yapısı
- [Core Systems](docs/CORE_SYSTEMS.md): startup, persistence, content, pooling, feature gate, analytics, audio, UI ve localization tarifleri
- [Integrations](docs/INTEGRATIONS.md): gerekli SDK'lar, scripting symbol'ları, kurulum sırası ve doğrulama
- [CI and Release](docs/CI_AND_RELEASE.md): repository gate'leri, Unity CI, cloud testleri ve release checklist
- [Troubleshooting](docs/TROUBLESHOOTING.md): import, Zenject, Addressables, entegrasyon ve build sorunları
- [Upgrading](docs/UPGRADING.md): template'ten üretilen oyunu güvenli biçimde güncelleme

Paket referansları: [Core](Packages/com.serhat.core-sdk/README.md) · [Analytics](Packages/com.serhat.analytics-sdk/README.md) · [Backend](Packages/com.serhat.backend-sdk/README.md) · [Localization](Packages/com.serhat.localization-sdk/README.md) · [Monetization](Packages/com.serhat.monetization-sdk/README.md)

Proje yönetimi: [Changelog](CHANGELOG.md) · [Katkı rehberi](CONTRIBUTING.md) · [Güvenlik](SECURITY.md) · [Davranış kuralları](CODE_OF_CONDUCT.md)

Güvenlik dışı yardım için başlangıç ve sorun giderme rehberlerini kontrol ettikten sonra repository'deki **Usage question** issue formunu kullan. Güvenlik açıklarını yalnızca `SECURITY.md` içindeki özel kanaldan bildir.

## Repository doğrulama

Proje kökünde çalıştır:

```powershell
pwsh -File ./Tools/Verify-Repository.ps1
```

Unity testleri, cloud testleri, Addressables ve platform build gate'leri [CI and Release](docs/CI_AND_RELEASE.md) belgesindedir. `Library`, `Temp`, `Logs`, `UserSettings`, `obj`, `.sln` ve Unity tarafından üretilen `.csproj` dosyalarını commit etme.

## Lisans

Serhat Forge'un first-party kodu [MIT License](LICENSE) ile sunulur. Üçüncü parti bileşenler ve import edilen Unity kaynakları kendi lisanslarını korur; dağıtımdan önce [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) dosyasını incele.
