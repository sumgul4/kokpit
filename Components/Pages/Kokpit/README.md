# CAE Kokpit — mevcut projeye ekleme

Bu klasör (`Container\Components\Pages\Kokpit\`) kokpitin tamamını içerir. Klasör dışında tek dosya logo:
`wwwroot\img\kokpit\d3-white.png`.

## Dosyalar

| Dosya | İçerik |
|---|---|
| `KokpitModels.cs` | Tüm tipler: kod sabitleri, 9 veri seti sınıfı, grafik satırları, Türkçe etiketler, ayarlar |
| `KokpitDataService.cs` | Tüm veritabanı erişimi: tek seferde veri çekimi, paylaşılan bellek kopyası, arka plan yenileme, `AddKokpit()` |
| `KokpitFilterState.cs` | Çapraz filtreleme durumu ve filtrelenmiş görünüm |
| `KokpitMetrics.cs` | Sayfa hesaplamaları, renk paleti, biçimlendirme |
| `KokpitLayout.razor` (+ `.razor.css`) | Başlık, sekmeler, filtre çubuğu ve tüm stiller |
| `KokpitPageBase.razor` | Sayfaların ortak tabanı: veri/filtre bağlantısı, grafik tıklama işleyicileri, özel bileşenler (KPI, rapor tüneli, ısı haritası, aşama kutuları, uyarı listesi) |
| `Kokpit*.razor` (6 adet) | Sekme sayfaları: `/kokpit`, `/kokpit/audit-plan`, `/kokpit/findings`, `/kokpit/report-tracking`, `/kokpit/human-resources`, `/kokpit/capacity-quality` |
| `_Imports.razor` | Bu klasördeki razor dosyalarının using'leri |
| `KokpitViews.sql` | Veri ekibi için 9 view'ın kolon sözleşmesi (derlenmez) |

## Kurulum

1. **NuGet** (DevExpress.Blazor zaten varsa): `Dapper`, `Microsoft.Data.SqlClient`.
2. **Program.cs** — `builder.Build()` satırından önce:
   ```csharp
   builder.Services.AddKokpit(builder.Configuration);
   ```
   (`using Container.Components.Pages.Kokpit;`)
3. **appsettings.json**:
   ```json
   "ConnectionStrings": { "KokpitReporting": "Server=...;Database=...;Integrated Security=true;TrustServerCertificate=true" },
   "Kokpit": {
     "RefreshIntervalSeconds": 120,
     "StaleAfterMinutes": 15,
     "CycleTimeTargetDays": 60,
     "TrainingHoursTarget": 40,
     "MinGroupSize": 5,
     "SlaWarningRatio": 1.25,
     "ViewerRoles": [ "DOMAIN\\TK-Baskanlik", "DOMAIN\\TK-Yonetisim" ],
     "HrRoles": [ "DOMAIN\\TK-Baskanlik" ]
   }
   ```
   Rol listesi boş bırakılırsa oturum açmış her kullanıcı erişir.
4. **Render mode** — uygulama genelinde `InteractiveServer` değilse her Kokpit sayfasındaki
   başındaki `@* @rendermode InteractiveServer *@` satırını açın.
5. **Stil** — `App.razor` içinde `<link rel="stylesheet" href="Container.styles.css" />` olmalı
   (Blazor şablonunda varsayılan olarak bulunur; `KokpitLayout.razor.css` bu dosyaya derlenir).
   `App.razor`'da `<HeadOutlet />` olmalı (font bağlantısı için).
6. **Yetki** — `Routes.razor` `AuthorizeRouteView` kullanmalı; sayfalar `[Authorize(Policy = ...)]` taşır.
7. **Menü** — ana menünüze `kokpit` bağlantısını ekleyin.

## İlk derlemede kontrol edilecekler

`DX-API` yorumlu yerler ve şu adlar DevExpress sürümünüzle doğrulanmalı:
`ChartSeriesPoint.DataItems`, `ChartSeriesClickEventArgs.Point`, `ChartSeriesPointCustomizationSettings.PointAppearance`,
`DxChartRangeBarSeries.StartValueField/EndValueField`, `DxSankey` alan adları ve `NodeClick`.
Grafik noktası okuma kodu tek yerde: `KokpitMetrics.cs` → `ChartInterop`.
