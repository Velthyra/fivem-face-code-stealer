# FaceSt Studio

FaceSt, FiveM'de istemciye yüklenmiş freemode karakterlerin yüz ayarlarını JSON olarak dışa aktaran Windows uygulamasıdır. Bu depo arayüz ve uygulama entegrasyonunun kaynak kodunu içerir. Yüz okuyucu, yükleyici ve JSON dönüştürücü `Payload/` içinde yalnızca derlenmiş olarak bulunur.

## Derleme

Windows x64 ve .NET 9 SDK gerekir. Depoyu indirdikten sonra `build.bat` dosyasını çalıştırın. Çıktı `dist/FaceSt.exe` olur; EXE kendi .NET çalışma zamanını ve gerekli ikili bileşenleri içerir. Geliştirme sırasında `dotnet run --project FaceSt.csproj` komutunu kullanabilirsiniz.

`Payload/` içindeki üç dosya derleme için gereklidir. Depoyu ZIP olarak indirirken veya klonlarken bu klasörü de alın.

## Kullanım

1. FiveM'de karakteriniz oyundayken FaceSt'i açın.
2. `INJECT` ile okuyucuyu yükleyin.
3. `YAKINDAKİLERİ BUL` listesinden oyuncu seçin veya sunucu ID'si girin. ID boşsa kendi karakteriniz okunur.
4. `YÜZ KODUNU ÇIKAR` ile JSON'u üretin. Sonuç panoya kopyalanır ve `%LOCALAPPDATA%\FaceSt\Codes` altında saklanır.
5. İşiniz bittiğinde `UNINJECT` ile okuyucuyu kaldırın.

Oyuncu ve yüz verilerinin istemcide yüklenmiş olması gerekir. Freemode dışındaki karakter modelleri yapıştırılabilir JSON'a dönüştürülemez. `YAKINDAKİLERİ BUL` listesindeki cinsiyet bilgisi yalnızca freemode modelinden belirlenir; diğer modeller `BİLİNMİYOR` olarak görünür.

## Yapı

| Yol | İçerik |
| --- | --- |
| `App.xaml`, `MainWindow.xaml` | WPF görünümü ve stiller |
| `MainWindow.xaml.cs` | Arayüz akışı, dosya doğrulama ve okuyucu çağrıları |
| `Payload/FaceStReader.dll` | Kapalı kaynak oyun içi okuyucu |
| `Payload/FaceCodeInjector.exe` | Kapalı kaynak yükleyici |
| `Payload/FaceSt.Core.dll` | Kapalı kaynak JSON dönüştürücü |

Okuyucu ham veriyi `%LOCALAPPDATA%\FaceSt\Captures` klasörüne yazar. Arayüz, dosyanın beklenen dizinde olduğunu ve JSON şemasını kontrol ettikten sonra dönüştürücüye iletir. Bu proje sunucunun özel veya şifreli yüz kodunu çözmez; karaktere uygulanmış değerlerden JSON üretir.

## Lisans

Arayüz kaynakları [MIT](LICENSE) lisanslıdır. `Payload/` dosyaları MIT kapsamına girmez; koşulları [NOTICE.md](NOTICE.md) dosyasındadır. Bu nedenle depo, uygulamanın tamamı için açık kaynak iddiasında bulunmaz.

## Geliştirici sorumluluk reddi

Bu yazılım kendi sorumluluğunuzda kullanılır. Kullanımından doğabilecek hesap yasakları, hesap veya veri kaybı ve diğer olumsuz sonuçlardan geliştirici sorumlu tutulamaz. Yazılımı kullanarak bu riskleri bildiğinizi ve kabul ettiğinizi beyan edersiniz.
