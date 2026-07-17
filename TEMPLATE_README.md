# Serhat Forge — Türkçe teknik rehber yönlendirmesi

Bu dosya, eski bağlantıları bozmamak için korunmaktadır. Güncel Türkçe tanıtım ve doğru ilk kurulum akışı için [README.tr.md](README.tr.md) dosyasını kullan.

Teknik belgelerin canonical sürümü İngilizcedir:

- [Getting Started](docs/GETTING_STARTED.md) — GitHub template akışı, Unity Hub, setup wizard ve ilk doğrulama
- [Features](docs/FEATURES.md) — yetenekler, varsayılan durum ve production hazırlık sınırları
- [Architecture](docs/ARCHITECTURE.md) — Zenject composition, startup akışı, sahiplik ve klasör yapısı
- [Core Systems](docs/CORE_SYSTEMS.md) — genel sistemlerin pratik kullanımı
- [Integrations](docs/INTEGRATIONS.md) — opsiyonel SDK ve scripting symbol kurulumu
- [CI and Release](docs/CI_AND_RELEASE.md) — test, build ve release gate'leri
- [Troubleshooting](docs/TROUBLESHOOTING.md) — yaygın sorunlar ve çözüm adımları
- [Upgrading](docs/UPGRADING.md) — template güncellemelerini oyun projesine güvenle taşıma

> Önemli: Önce boş bir Unity projesi oluşturup Serhat Forge dosyalarını üzerine kopyalama. GitHub'da **Use this template** ile yeni repository oluştur, onu clone et ve repository kökünü Unity Hub'da **Add project from disk** ile aç.

Serhat Forge `0.1.0-preview.1` durumundadır. Template test edilmiş genel bir altyapı sunar; açtığın platform, mağaza, backend ve üçüncü parti servisleri production öncesinde kendi oyunun için yapılandırıp doğrulaman gerekir.
