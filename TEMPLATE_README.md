# Serhat Forge — Unity oyun başlangıç şablonu

Serhat Forge, proje bağımsız bir Unity başlangıç altyapısıdır. Üretim oyun mekaniği veya zorunlu domain içeriği taşımaz; ortak runtime servisleri ve güvenli başlangıç varsayımları sağlar.

- Unity: `6000.3.14f1` (`d68c3f99a318`)
- Render pipeline: URP `17.3.0`
- Varsayılan durum: reklam kapalı, remote catalog build/kontrolü kapalı, Addressables grupları boş; local content player ile deterministik build edilir
- Namespace: `Serhat.Forge.*`

## Yeni proje oluşturma

1. Klasörü kopyala; `Library/`, `Temp/`, `Logs/`, `obj/`, `.vs/`, `*.csproj` ve `*.sln` dosyalarını taşıma.
2. Projeyi Unity `6000.3.14f1` ile aç ve UPM çözümlemesinin tamamlanmasını bekle.
3. Player Settings'te company name, product name, bundle identifier, sürüm ve build numarasını değiştir.
4. Build Settings'e kendi boot ve gameplay sahnelerini ekle.
5. Platform ikonları, splash, Android signing ve iOS signing/capability ayarlarını proje bazında doldur.
6. Remote Addressables kullanacaksan profile URL'lerini değiştir; aksi halde remote catalog kapalı kalsın.
7. Yalnızca kullandığın üçüncü parti SDK'ları kur ve ilgili define symbol'ları aç.

## Genel sistemler

### Başlangıç ve DI

- `GameBootstrapper`: canonical tekil boot sahibi; Addressables init/catalog/preload, prefab preload, startup step çalıştırma ve async ilk sahne yükleme.
- `ContentBootstrapper`: yalnız content-init gereken projeler için standalone/legacy alternatiftir; varsayılan auto-init ve scene load kapalıdır. Aynı boot sahnesinde `GameBootstrapper` ile birlikte kullanma.
- `StartupStep` / `StartupPipeline`: sıralı required/optional adımlar, cancellation, timeout ve güvenli retry.
- Zenject `ProjectContext` + `ForgeProjectInstaller`: application-lifetime servis kayıtları ve tek composition root.
- `IPrefabLoader`: Addressables handle sahipliği, preload ve toplu release.

Timeout alan startup step tekrar çalıştırılmaz. Token iptalinden sonra step, kendi `Cancellation Grace Seconds` süresi kadar beklenir; token'ı yok sayarsa pipeline fail olur ve task arka planda kalabileceği için step implementasyonları cancellation'ı kooperatif sonlandırmalıdır.

### Persistence

Restore varsayılan olarak fail-closed ve transactionaldır: participant'lar `ITransactionalSaveParticipant<T>` ile mutation öncesi snapshot sağlamalıdır; hata halinde ters sırada rollback yapılır. Legacy geçişi için `SaveCoordinator(..., requireTransactionalRestore: false)` açık opt-out'unu yalnızca geçici kullan.

- `VersionedJsonSaveRepository<T>`: schema/data version, migration zinciri, SHA-256 checksum, generation seçimi ve `.tmp` / `.bak` recovery.
- `SaveCoordinator<T>`: sıralı participant capture/restore ve tek-writer save akışı.
- `ITransactionalSaveParticipant<T>`: mutation öncesi snapshot ve restore hatasında rollback kontratı.
- `SaveLifecycleRelay`: pause, focus loss ve quit save tetikleri.

Save DTO'ları `[Serializable]` olmalı ve `JsonUtility` kısıtlarına uymalı; dictionary kullanma. Checksum şifreleme veya anti-cheat değildir. Büyük save'lerde capture/serialize maliyetini profille ve gerekirse proje bazlı background serializer kullan.

### Feature gates

- Progress threshold, görünürlük, external condition/entitlement ve runtime override desteği.
- Sparse `FeatureId` saklama; enum bitmask varsayımı yok.
- Seen/notification state kalıcıdır.
- Bulunamayan external condition fail-closed davranır.

`LevelUnlockCatalog` sınıf adı eski asset GUID/serialization uyumluluğu için korunmuştur; runtime anlamı generic progress catalog'dur.

### İçerik, pooling ve presentation

- Boş ve temiz Addressables grupları: `Local_Core`, `Remote_Default`.
- Generic label'lar: `core`, `gameplay`, `ui`, `audio`.
- `ComponentPool<T>`: prefab-backed pool, prewarm, active lease takibi ve güvenli dispose.
- Audio, localization, tutorial/scenario, UI navigation, camera helpers, haptics ve analytics abstraction'ları.
- Analytics delivery serialize edilir; offline/provider hatalarında event'ler persistent outbox'a veya memory queue'ya geri alınır, dispose aktif gönderimlerin drain olmasını bekler.
- Provider yoksa `DebugAndRemote` güvenli biçimde `DebugOnly` olur; `RemoteOnly` konfigürasyonu fail-fast davranır.
- UIEffect ve commit'e pinli Particle Effect for UGUI authoring paketleri.
- Ads/IAP adapter yüzeyi; provider yokken null/disabled davranış.

## Dahil edilen güvenli config'ler

`Assets/Resources` altında:

- `ContentConfiguration.asset`: catalog update kapalı, preload boş, offline boot açık.
- `AdRuntimeSettings.asset`: reklam kapalı.
- `AppRuntimeSettings.asset`: frame policy kapalı; Unity'nin vSync ve target frame-rate ayarlarına dokunmaz.
- `LocalizationSettings.asset`: `en` / `tr`, sistem dili açık.

Bunlar başlangıç değerleridir; oyuna göre kopyala veya düzenle.

`Assets/StreamingAssets/Localization/Locales` altındaki `en`, `tr` ve CSV dosyaları yalnızca genel UI, ayar, mesaj ve hata anahtarları içeren başlangıç kataloğudur. Bunlar runtime kontratı değildir; yeni oyunun domain sözlüğünü ayrı anahtarlarla ekle ve üç katalogdaki anahtarları eş zamanlı tut.

## Opsiyonel entegrasyonlar

Aşağıdaki symbol'ları SDK kurulmadan açma:

| Symbol | Gereken entegrasyon | Etki |
|---|---|---|
| `DOTWEEN` | DOTween | Tween tabanlı camera/UI/navigation kodu |
| `SRDEBUGGER` | SRDebugger | `DebugActivator` çoklu-tap tetikleyicisinden debug panelini açar; SDK yokken UnityEvent çalışır |
| `GOOGLE_MOBILE_ADS` | Google Mobile Ads | Google ad provider ve inspector |
| `FIREBASE_ANALYTICS_AVAILABLE` | Firebase Analytics | Firebase analytics provider assembly |
| `NICE_VIBRATIONS` | Lofelt Nice Vibrations | Native preset haptics; yoksa mobilde `Handheld.Vibrate` fallback'i |
| `PLAYFAB_SDK` | PlayFab Unity SDK | Backend PlayFab adapter |
| `UNITY_PURCHASING` | Unity IAP | Satın alma client/adaptör kodunu açar; setup wizard üzerinden açıkça opt-in edilmelidir |
| `SERHAT_FORGE_LOCAL_MONETIZATION` | Unity IAP + `UNITY_PURCHASING` | Yalnızca Editor/Development Build'de derlenen, açıkça opt-in yerel purchase stub'ı; release player'a girmez |
| `SERHAT_FORGE_AUTH` + `PLAYFAB_SDK` | PlayFab Unity SDK | PlayFab tabanlı auth orchestrator; iOS Keychain/Game Center bridge dahil |
| `GOOGLE_PLAY_GAMES` | Google Play Games plugin | Android auth provider assembly; yukarıdaki auth symbol'larına eklenir |
| `SERHAT_FORGE_GAME_API_SAMPLE` | Backend sample contracts | Package Manager'dan import edilen Game API Reference örnek kontratlarını açar |
| `SERHAT_FORGE_IOS_GAME_SERVICES` | iOS build | Xcode Game Center ve Push Notifications capability postprocess'i |


Android Google Play Games provider ayrı asmdef'tedir; iOS auth Google Play Games assembly'sine bağlı değildir.

## Backend sınırı

`Packages/com.serhat.backend-sdk/Runtime/Core` içindeki resilience, outbox, coalescing ve transport abstraction'ları geneldir.

Aşağıdaki parçalar reusable core değildir:

- `Packages/com.serhat.backend-sdk/Samples~/GameApi`: oyun-domain örnek kontratlarıdır. Package Manager üzerinden import edilir ve yalnızca `SERHAT_FORGE_GAME_API_SAMPLE` ile derlenir.
- `Samples~/GameApiBackend`: progression ve economy içeren ayrı bir Game API Reference backend sample'ıdır. Generic runtime'ın parçası değildir; kendi domain kontratlarınla değiştir veya kaldır.
- `cloudscript-azure-functions-monetization`: ayrı bir monetization referansıdır. Production öncesinde Apple root CA/JWS ve Google Pub/Sub OIDC ayarlarını kendi bundle/package/audience/service-account değerlerinle yapılandırıp negatif güvenlik testlerini çalıştır.

## Addressables

- Şablonda oyun içeriği entry'si yoktur.
- `Remote_Default` URL'leri placeholder'dır.
- CDN yapılandırılmadan remote catalog veya remote preload açma.
- Runtime'da yüklenen asset handle'larının sahibi loader/service olmalı ve lifecycle sonunda release edilmelidir.

## iOS auth

`Assets/Plugins/iOS` altında generic bridge'ler bulunur:

- Keychain read/write/delete; native string belleği explicit free edilir.
- Game Center auth ve identity verification signature callback'i.

Bundle identifier değiştirildiğinde Keychain service adı otomatik olarak `<bundle-id>.auth` olur. Xcode Game Center capability için gerekirse `SERHAT_FORGE_IOS_GAME_SERVICES` aç.

## Template kabul checklist'i

Komut satırı mobil smoke build'leri için `SERHAT_FORGE_BUILD_PATH` tanımlayıp hedefe göre `Serhat.Forge.Editor.SerhatForgeBatchBuild.BuildAndroidDevelopment` veya `BuildIosDevelopment` metodunu `-executeMethod` ile çalıştır. Her iki giriş noktası da IL2CPP ve geçerli bir Build Settings sahnesini zorunlu tutar; Android doğrulaması ayrıca ARM64 ister ve development APK üretir.

- Unity Console: compile error yok.
- Package Manager: manifest/lock çözümlemesi başarılı.
- Addressables Analyze ve clean build başarılı.
- EditMode/PlayMode testleri başarılı.
- Android IL2CPP development build ve gerçek cihaz smoke test başarılı.
- iOS Xcode build, Keychain ve Game Center gerçek cihaz testi başarılı.
- Ads, IAP, analytics ve backend her environment için ayrı config ile doğrulanmış.
- Apple/Google server webhook signature validation production öncesi tamamlanmış.
