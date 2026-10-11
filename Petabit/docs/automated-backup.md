# Automatski produkcijski backup

## Stanje 11. listopada 2026.

Postupak je pripremljen u `.github/workflows/production-backup.yml`. Dnevno izvršavanje **nije uključeno**: nedostaju odobrena Microsoft OAuth veza i namjenski Railway API pristup u zaštićenim GitHub environment secrets. Postojeća OneDrive kopija od 10. listopada ostaje sačuvana. Nisu registrirani novi trajni SSH ključevi, kopirani lokalni pristupni tokeni niti mijenjane MFA postavke.

GitHub-hosted runner može raditi kada vlasnikovo računalo nije uključeno. Raspored je 03:20 UTC (05:20 u Zagrebu tijekom ljetnog računanja, 04:20 zimi). GitHub rasporedi nisu jamstvo točnog vremena izvršavanja.

## Postupak

1. Produkcijski posao dopušten je samo na masteru, preko rasporeda ili ručnog pokretanja. Na PR-ovima se provjeravaju samo testovi i instalacija alata, bez produkcijskih tajni. Git checkout nema spremljene GitHub vjerodajnice.
2. Railway i rclone preuzimaju se iz službenih izdanja uz pinanu verziju i SHA-256 provjeru. Backup kod i alati izuzeti su iz aplikacijske Docker slike.
3. Zaštićeni API token registrira nov privremeni SSH ključ. Preuzima se stvarni volumen `/app/App_Data`; ključ se odmah opoziva. Neuspjeh opoziva smatra se neuspjehom cijelog postupka.
4. Git bundle i volumen šifriraju se postojećim AES-256-GCM formatom. Arhiva dobiva datum i jedinstveni run ID. Kopira se u `OneDrive / Petabit Backups / Automated` bez overwritea i bez brisanja prethodnih kopija.
5. Ista arhiva ponovno se preuzima s OneDrivea. Provjeravaju se identičnost bajtova, autentikacija, sve hash vrijednosti, produkcijsko podrijetlo i JSON snapshotovi. Git repozitorij vraća se iz bundlea i provjerava kroz `git fsck`.
6. Prvog dana mjeseca po zagrebačkom vremenu, ili na zahtjev, iz vraćenog koda gradi se aplikacija. Pokreće se na privatnoj mreži bez interneta, s podacima samo za čitanje. Provjeravaju se zdravlje, sigurnosne granice i dohvat spremljenih ISS/Starlink podataka.
7. Izvještaj bez tajni ostaje GitHub artifact 90 dana. OAuth refresh konfiguracija čuva se kao zaseban **šifrirani** artifact 30 dana i učitava pri sljedećem izvršavanju. Time se čuvaju rotirani Microsoft tokeni bez PAT-a s ovlasti mijenjanja GitHub tajni. Nešifrirani podaci, OAuth konfiguracija, SSH privatni ključ i recovery ključ nikad se ne uploadaju kao artifacts.

## Povezivanje prije aktivacije

1. Kreiraj GitHub environment `production-backup`, s deployment branch pravilom samo za `master`. Ne dodavati PR grane. Environment ne smije zahtijevati ručno odobrenje svakog dnevnog izvršavanja ako se želi potpuno automatski rad. Zaštita mastera ostaje obvezna.
2. Namjenski Railway token spremi kao environment secret `PETABIT_RAILWAY_API_TOKEN`. CLI za registraciju ključa i izvoz volumena zahtijeva account/workspace API pristup; ovo **nije read-only token**. Workspace-owned ključevi mogu tražiti ADMIN pristup. Najprije provjeriti postoji li izolirani identitet ograničen na Petabit; ako nije dostupan, vlasnik mora odlučiti prihvaća li širi pristup ili odabrati drugi backend. Ne kopirati postojeći osobni CLI token bez tog dogovora. Ako opoziv ključa traži interaktivni MFA kod, postupak se ne može automatizirati tim identitetom; ne gasiti MFA radi backupa.
3. Rclone OneDrive konfiguraciju napravi kroz Microsoft prijavu na pouzdanom računalu. Remote nazovi `petabit`, tip `onedrive`. Microsoft prikazuje tražene OAuth ovlasti; prihvatiti ih tek nakon pregleda. Personal OneDrive ne treba tretirati kao spremište s pravom pisanja samo u jednu mapu ako dodijeljeni token ima širi pristup. Konfiguraciju spremi kao `PETABIT_ONEDRIVE_CONFIG`; nikad je ne slati u chat, Git ili logove. Inicijalna konfiguracija služi za prvo pokretanje i ponovnu autorizaciju nakon dugog prekida ili opoziva.
4. Postojeći 32-bajtni recovery ključ spremi base64-kodiran kao `PETABIT_BACKUP_KEY_BASE64`, bez ispisivanja vrijednosti. Kopija ključa u GitHub secretu potrebna je za automatsko šifriranje i provjeru; offline kopija i dalje mora biti odvojena od OneDrivea i GitHuba. Gubitak ili promjena ključa onemogućuje čitanje starih arhiva i OAuth checkpointova.
5. Pokreni workflow ručno na masteru s `restore_drill=true`. Tek nakon potvrđenog remote readbacka, oporavka i opoziva SSH ključa postavi repository variable `PETABIT_BACKUPS_ENABLED=true`. Bez varijable dnevni posao se preskače, a ne predstavlja kao uspješan backup.

Tajne postaviti kroz GitHub environment UI ili siguran stdin alata `gh secret set --env production-backup`. Ne stavljati vrijednosti u argumente naredbi, clipboard izvještaje ili snimke ekrana. Automatski posao izolira osjetljive varijable po alatu: Railway dobiva svoj token, gh svoj token, a ostali alati ne nasljeđuju recovery ključ ni OAuth konfiguraciju iz environmenta.

## Upozorenja, zadržavanje i provjere vlasnika

Neuspjelo izvršavanje otvara jedan GitHub issue `Petabit production backup needs attention` i označava vlasnika. Dok je taj issue otvoren, ponovljeni kvarovi ne stvaraju nove obavijesti. Vlasnik treba potvrditi da mu GitHub obavijesti dolaze; nije postavljen zaseban e-mail/Slack kanal. Otkazano izvršavanje ili izostanak GitHub rasporeda nisu pokriveni ovom failure obavijesti: za to je potreban neovisan nadzor starosti posljednje kopije.

U slučaju prekida provjeriti Railway SSH keys: procesni `finally` nije jamstvo pri prisilnom gašenju runnera. Repozitorij i environment secrets ostaju osjetljiva točka; kod spojen u master mogao bi koristiti tajne. MFA, pregled pristupa i odvojena offline kopija ostaju važni.

Kopije se zasad čuvaju sve; automatizacija nema naredbu za brisanje. Nakon mjesec dana izmjeriti potrošnju i dogovoriti rotaciju (npr. 7 dnevnih, 4 tjedne, 3 mjesečne). OneDrive sinkronizacija nije nepromjenjivo spremište zaštićeno od brisanja. Povremeno napraviti i kopiju na odspojenom disku.

Vlasnik još treba potvrditi MFA za Microsoft, GitHub i Railway i fizički spremiti offline recovery ključ. To nije moguće dokazati postojanjem datoteke na istom računalu. Cijeli cloud postupak nije produkcijski potvrđen dok ne prođe ručna proba s pravim odobrenim vezama.

Službeni izvori: [Railway volume](https://docs.railway.com/cli/volume), [Railway SSH](https://docs.railway.com/cli/ssh), [Rclone OneDrive](https://rclone.org/onedrive/), [Rclone remote setup](https://rclone.org/remote_setup/).
