# CAE Kokpit — Aşama 2 (güncelleme): sayfa iskeleti, sekmeler, yetki

`/kokpit` adresinde tek sayfa: üst bar + 6 sekme (DxTabs) + plan yılını gösteren alt bilgi.
**Program.cs ve appsettings.json'a dokunmak gerekmez.** Tüm ayarlar bu klasörde.

## Dosyalar

| Dosya | İçerik |
|---|---|
| `Kokpit.razor` | `/kokpit` sayfası: kullanıcı yetkisi, 6 sekme, son açılan sekmeyi hatırlama |
| `KokpitSetup.cs` | Yetkili kullanıcı listeleri, plan yılı, logo yolu, kullanıcıyı çözen `KokpitAccess` |
| `KokpitLayout.razor` (+ `.razor.css`) | Üst bar (başlık, logo, AY-09 ve AY-07 alanları), içerik, alt bilgi; tüm stiller ve yerel font |
| `KokpitOverview.razor` … `KokpitCapacityQuality.razor` | 6 sekmenin içeriği (şimdilik yer tutucu) |
| `KokpitAccessDenied.razor` (+ `.razor.css`) | `/kokpit/access-denied` |
| `_Imports.razor` | Bu klasörün using'leri |

Klasör dışındaki dosyalar (statik dosyalar wwwroot'ta olmak zorunda):
- `wwwroot\img\kokpit\d3-white.png` — logo
- `wwwroot\fonts\kokpit\*.woff2` — Source Sans 3 (6 dosya, internet gerekmez; SIL Open Font License, `OFL.txt`)

## Yetki nasıl çalışıyor

1. Windows kullanıcısı alınır (`KUVEYTTURK\` öneki atılır) — EmptyLayout ile aynı kural.
2. `WEB.SimuleEdilenKullanicilar_SP` ile simüle edilen kullanıcı varsa o kullanılır.
3. `_global.KullaniciSatiri()` çağrılır (ad soyad, sektör vb. dolar).
4. Kullanıcı kodu `KokpitSettings.ViewerUsers` listesinde değilse sayfa hiçbir şey göstermez ve
   `/kokpit/access-denied` sayfasına yönlendirir. `HrUsers` listesinde değilse İK sekmesi oluşturulmaz.

Listeler şimdilik `KokpitSetup.cs` içinde. Veri aşamasında bir tabloya (örn. `WEB.KokpitYetki`) taşınacak;
o zaman sadece `KokpitAccess.ResolveAsync` içindeki iki satır değişir.

## Kendi projenizde kontrol edilecekler

- `KokpitSetup.cs` başındaki `using Container.Components.Shared.Services;` — `GlobalDegiskenler` ve `IDataAccess`
  başka bir namespace'teyse düzeltin.
- `IDataAccess.GetSingleRow<T, dynamic>(sp, parametre, CommandType.StoredProcedure)` imzası EmptyLayout'taki kullanımla aynı varsayıldı.
- Routes.razor'daki varsayılan layout EmptyLayout değilse `Kokpit.razor` başındaki `@layout EmptyLayout` satırını açın.
- Uygulama genelinde interaktif değilse `@rendermode InteractiveServer` satırını açın.
- `DxTabs` parametreleri: `ActiveTabIndex`, `ActiveTabIndexChanged`, `RenderMode="TabsRenderMode.OnDemand"`, `DxTabPage Text`.
