# ADR-0003: gRPC — kod üretimi olmadan, çalışma anında proto

- **Durum:** Kabul edildi
- **Tarih:** 2026-11-08

## Bağlam

Ekip gRPC servislerini `.proto` dosyalarıyla tanımlıyor. Server reflection şimdilik kullanılmıyor (ileride eklenecek).
Bir test aracı, proto dosyası değiştiğinde yeniden derleme gerektirmeden çalışabilmeli. .NET'in resmi protobuf
kütüphanesi (`Google.Protobuf`) dinamik mesaj (`DynamicMessage`) desteği sunmuyor.

## Değerlendirilen seçenekler

| Seçenek | Artı | Eksi |
|---|---|---|
| `protoc` çalıştırıp C# üretmek, çalışma anında derlemek | Resmi kod | Her platform için `protoc` dağıtmak, Roslyn ile derleme; yavaş ve kırılgan |
| `protoc --descriptor_set_out` + kendi codec'imiz | Resmi parser | Platform başına native ikili dağıtımı |
| **Yönetilen (managed) proto parser (protobuf-net.Reflection) + kendi JSON↔protobuf codec'imiz** | Saf .NET, her platformda aynı; well-known type'lar gömülü; hatalar satır/kolon bilgisiyle | Codec'in doğruluğu bizim sorumluluğumuzda |

## Karar

`protobuf-net.Reflection` ile `.proto` dosyaları çalışma anında `FileDescriptorProto`'ya çevrilir ve kendi şema modelimize
(`ProtoSchema`) dönüştürülür. Kütüphaneye olan bağımlılık tek bir dosyada izole edilmiştir. İleride server reflection'dan
gelen descriptor'lar da aynı modele dönüştürülecek.

JSON ↔ protobuf dönüşümü `ProtoJsonCodec` ile, resmi
[proto3 JSON eşlemesine](https://protobuf.dev/programming-guides/json/) uygun yapılır. Düşük seviye kodlamada
`Google.Protobuf`'ın `CodedInputStream`/`CodedOutputStream` sınıfları kullanılır. Çağrılar `Grpc.Net.Client` ile, mesajlar
ham bayt olarak taşınarak yapılır.

İki kütüphane de `Google.Protobuf.Reflection` isim alanında tip tanımladığı için `protobuf-net.Reflection` bir
`extern alias` (`pbnet`) ile referans edilir.

## Doğrulama

Codec, testlerde Google'ın resmi kod üretimi (`Grpc.Tools`) ve resmi `JsonParser`/`JsonFormatter`'ı ile çapraz
doğrulanır. Tüm skaler tipler, enum, iç içe/özyinelemeli mesajlar, `repeated` (packed/unpacked), `map` (string/int64/bool
anahtar), `oneof`, `optional` ve tüm well-known type'lar (`Timestamp`, `Duration`, wrapper'lar, `Struct`, `Value`,
`ListValue`, `FieldMask`, `Any`, `Empty`) kapsanır.

## Sonuçlar

- Proto değiştiğinde yalnızca yeniden yüklemek yeterlidir; derleme gerekmez.
- Kullanıcı hataları alan yolu ile raporlanır (`$.address.zip: Unknown field 'zip' in users.v1.Address. Known fields: street, city.`).
- Şemadan farklı bir sunucu yanıtı açık bir hata ile bildirilir. Bilinmeyen alanlar (yeni sunucu sürümü) sessizce atlanır.
- proto2 `group` alanları desteklenmez (açık hata mesajı verilir). Proto editions desteği ileride değerlendirilecek.
