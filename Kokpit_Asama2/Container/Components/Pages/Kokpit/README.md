# CAE Kokpit — Aşama 2: sayfa iskeleti, sekmeler, yetki

`/kokpit` adresinde tek sayfa: üst bar + 6 sekme (DxTabs) + plan yılını gösteren alt bilgi.
Veri, model, filtre ve kart dosyaları sonraki aşamalarda bu klasöre eklenecek.

## Dosyalar

| Dosya | İçerik |
|---|---|
| `Kokpit.razor` | `/kokpit` sayfası: yetki kontrolü, 6 sekme, son açılan sekmeyi hatırlama |
| `KokpitLayout.razor` (+ `.razor.css`) | Üst bar (başlık, logo, AY-09 ve AY-07 için ayrılmış alanlar), içerik, alt bilgi; tüm stiller |
| `KokpitSetup.cs` | Ayarlar (`KokpitOptions`), yetki politikaları, `AddKokpit()` kaydı |
| `KokpitOverview.razor` … `KokpitCapacityQuality.razor` | 6 sekmenin içeriği (şimdilik yer tutucu, `@page` yok) |
| `KokpitAccessDenied.razor` (+ `.razor.css`) | `/kokpit/access-denied` yetkisiz sayfası |
| `_Imports.razor` | Bu klasörün using'leri |

Klasör dışında tek dosya: `wwwroot\img\kokpit\d3-white.png` (logo).

## Yetki kuralları

| Kullanıcı | Sonuç |
|---|---|
| `ViewerRoles` içinde rolü yok | Menüde link görünmez; `/kokpit` adresine gelirse `/kokpit/access-denied` sayfasına yönlenir (log'a uyarı düşer) |
| `ViewerRoles` var, `HrRoles` yok | 5 sekme görür; İnsan Kaynakları sekmesi hiç oluşturulmaz |
| `ViewerRoles` + `HrRoles` | 6 sekme |

Rol listesi boş bırakılırsa kimse erişemez (güvenli varsayılan).

## Kurulum

1. **Program.cs** — `builder.Build()` satırından önce:
   ```csharp
   builder.Services.AddKokpit(builder.Configuration);   // using Container.Components.Pages.Kokpit;
   ```
   Projede yoksa şunlar da olmalı: `builder.Services.AddCascadingAuthenticationState();`,
   `app.UseAuthentication(); app.UseAuthorization();`

2. **appsettings.json**:
   ```json
   "Kokpit": {
     "ViewerRoles": [ "DOMAIN\\TK-Baskanlik", "DOMAIN\\TK-Yonetisim" ],
     "HrRoles": [ "DOMAIN\\TK-Baskanlik" ],
     "PlanYear": 2026
   }
   ```
   `PlanYear` verilmezse içinde bulunulan yıl gösterilir (sonraki aşamada veriden gelecek).

3. **Menü** — `NavMenu.razor` içine (rolü olmayan link görmez):
   ```razor
   <AuthorizeView Policy="@Container.Components.Pages.Kokpit.KokpitPolicies.Viewer">
       <div class="nav-item px-3">
           <NavLink class="nav-link" href="kokpit">CAE Kokpit</NavLink>
       </div>
   </AuthorizeView>
   ```

4. **Render mode** — uygulama genelinde `InteractiveServer` değilse `Kokpit.razor` başındaki
   `@* @rendermode InteractiveServer *@` satırını açın.

5. **App.razor** — `<HeadOutlet />` ve `<link rel="stylesheet" href="Container.styles.css" />` bulunmalı
   (Blazor şablonunda varsayılan olarak vardır).

6. **Birden fazla sunucu (web farm)** — son sekme `ProtectedLocalStorage` ile şifreli saklanır.
   Sunucular ortak Data Protection anahtarı kullanmıyorsa diğer sunucu kaydı okuyamaz ve ilk sekme açılır (hata vermez).

## Önceki paketten kaldırılacaklar

Aşama 1 paketindeki şu dosyalar bu aşamada kullanılmıyor; veri yapınıza göre yeniden yazılacaklar:
`KokpitModels.cs`, `KokpitDataService.cs`, `KokpitFilterState.cs`, `KokpitMetrics.cs`, `KokpitPageBase.razor`, `KokpitViews.sql`.
Eski 6 sayfa dosyası (`@page` içerenler) bu paketteki sekme dosyalarıyla değiştirilmelidir.

## DevExpress kontrolü

`DxTabs` parametreleri: `ActiveTabIndex`, `ActiveTabIndexChanged`, `RenderMode="TabsRenderMode.OnDemand"`, `DxTabPage Text`.
Sürümünüzde ad farkı varsa yalnızca `Kokpit.razor` içinde düzeltilir.
