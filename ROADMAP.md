# Yol haritası

Durum 29 Ekim 2026. İşaretli maddeler depodaki kodda vardır. İşaretsiz maddeler sözdür, kurulu programda yoktur.

## Şimdi

- [x] Yalnızca StevenBlack porn-only listesi. Doğrulama tutmazsa eski liste kalır.
- [x] Liste hosts dosyasına yazılmaz. Eski NTFT bloğu silinir, sizin satırlarınız kalır.
- [x] Yerel vekil alan adına bakar, sayfa şifresini çözmez. Vekil düşünce bağlantı doğrudan gider.
- [x] Parola kilidi, kurtarma anahtarı ve deneme sınırı.
- [x] Güç düğmeli pencere. Koruma ve kilit ayrı yazılır.
- [x] Engel bildirimi alan adı taşımaz.

## Sırada: TurkNet

- [ ] Koruma açılınca Cloudflare Family DNS (`1.1.1.3` / `1.0.0.3`) seçilebilir olsun. `microsoft.com` çözülmezse eski DNS geri gelsin.
- [ ] Tarayıcı DoH politikası ve bilinen DoH adreslerine 443 engeli kapalı kalsın. Tarayıcıda DoH kullanılmıyor. Bu ikisi tarayıcıyı bozabilir. Sertleştirme olarak sonra bakılır.

Family DNS, VPN kapalıyken Discord’u açar ve yetişkin alan adını çözmez. Proton açıkken Windows DNS’i tünele girmez.

## Sırada: tarayıcı kilidi

Cold Turkey’deki kaçış şuydu: eklenti bir tarayıcıda duruyor, eski bir tarayıcı eklentisiz açılıyor. NTFT bunu tarayıcı işlemiyle kapatacak. VPN’e karışmayacak. Yetişkin bir adrese arka planda soru atılmayacak.

- [ ] Chrome, Edge ve Brave için zorunlu eklenti. Firefox için ayrı ilke.
- [ ] Eklenti, açılan adresi tarayıcının içinde listeyle karşılaştırır. Adres servise ve günlüğe gitmez.
- [ ] Engel sayfasında yalnızca “Bu site engellenmiştir” yazar. Sayfanın şifresi çözülmez.
- [ ] Kurulum, gizli sekmeyi görme ve adresleri okuma izinlerini kontrol eder. İzin yoksa koruma açık sayılmaz.
- [ ] Gizli sekme kapatılmaz. Eklenti gizli sekmeyi göremiyorsa tarayıcı kapanır.
- [ ] Nabız kesilirse tarayıcı kapanır. Eklenti silinmişse, `--disable-extensions` ile açılmışsa veya Helium ilkeyi yok sayıyorsa işlem açık kalmaz.
- [ ] Internet Explorer, eski Edge, Pale Moon ve eklenti yükleyemeyen diğer bilinen tarayıcılar nabız veremediği için kapanır.
- [ ] VLC gibi ayrı bir oynatıcı eklenti yükleyemez. VPN kapalıyken alanı Family DNS keser. VPN açıkken ayrı oynatıcı bu kilidin dışındadır.
- [ ] Discord uygulaması tarayıcı değildir. Bu kurala girmez.

## VPN

- [x] Proton ve WARP’ın tüneli kesilmez.
- [ ] Proton günlük kullanılınca tarayıcı kilidi tünelin içindeki adresi de görür. Proton’un NetShield ayarı okunmaz.

## Sonra: malware ve ad

Porn tek başına dar bir kitle. Genel bir güvenlik duvarı veya çocuk filtresi gibi görünmesi de bu ürünün işi değil.

- [ ] Kurulumda iki kutu: “Porn engellensin” ve “Malware engellensin”.
- [ ] Yalnız porn seçilirse ad NTFT-Enhanced kalır.
- [ ] Malware de seçilirse ürün genel bir site engelleyicisine yaklaşır. Ad o zaman değişir. Aday ad KMSecurity’dir. Karar, kutu gerçekten çalışınca verilir.
- [ ] Malware listesi de indirilen bir liste olur. Hosts dosyasının tamamı yine yazılmaz.

## Bilerek yok

- Çekirdek sürücüsü, görev yöneticisini kapatmak, IFEO.
- HTTPS ortadaki adam ve kök sertifika.
- YouTube kısıtı.
- Arka planda yetişkin adrese ping.
- Ürünün içine VPN istemcisi veya NoFap sayacı.
