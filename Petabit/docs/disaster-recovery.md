# Petabit — oporavak i sigurnosne kopije

## Što treba sačuvati

- Git povijest i sve grane/oznake (`repository.bundle`). Nezamijenjeni lokalni rad nije u Git bundleu; alat zato odbija prljavi radni direktorij.
- Cijeli `/app/App_Data` s Railway volumena. NASA i Starlink JSON datoteke nisu baza korisnika, ali čuvaju posljednje provjereno stanje tijekom prekida izvora.
- Nazive i strukturu konfiguracije, Dockerfile i ovo uputstvo. Tajne vrijednosti i recovery kodovi idu zasebno u šifrirano spremište koje kontrolira vlasnik.
- Ključ za dešifriranje, izvan projekta i odvojeno od arhive. Gubitak ključa znači gubitak pristupa kopiji.

## Utvrđeno 9. listopada 2026.

Railway volumen: `petabit-volume`, montiran na `/app/App_Data`. U trenutku provjere nije bilo backupa ni rasporeda. Railway API odbio je uključivanje rasporeda porukom da ručni backupi i rasporedi zahtijevaju Pro workspace. Plan nije nadograđen. Preuzimanje volumena traži registrirani SSH ključ; bez njega kopija lokalnog `Petabit/App_Data` NIJE kopija produkcijskog volumena.

GitHub secret scanning i push protection već su uključeni. Dodatno skeniranje Git povijesti ne zamjenjuje zaštitu korisničkih računa. MFA i recovery kodove treba potvrditi vlasnik; nije potvrđeno da su MFA postavke uključene.

## Šifrirana neovisna kopija

Potreban je Node.js 24+ i Git. Alat koristi AES-256-GCM, slučajni 256-bitni ključ, novu 96-bitnu nonce vrijednost po arhivi i SHA-256 za svaku datoteku. Ključ se nikad ne ispisuje. Arhiva se provjerava prije spremanja. Vraćanje je dopušteno samo u novi direktorij; postojeći direktoriji, opasne putanje, duplikati i symlinkovi u ulaznim podacima se odbijaju.

Primjer (PowerShell; zamijeni putanje svojim odredištem):

```powershell
node security/backup/backup.mjs keygen --key C:/PetabitKeys/petabit.recovery-key
node security/backup/backup.mjs create --repository . --data C:/PetabitExport/App_Data --data-source railway-volume-export --key C:/PetabitKeys/petabit.recovery-key --output E:/PetabitBackups/petabit-2026-10-09.pbit
node security/backup/backup.mjs verify --archive E:/PetabitBackups/petabit-2026-10-09.pbit --key C:/PetabitKeys/petabit.recovery-key
node security/backup/backup.mjs restore --archive E:/PetabitBackups/petabit-2026-10-09.pbit --key C:/PetabitKeys/petabit.recovery-key --destination C:/PetabitRestore/new-restore
```

Roditeljski direktoriji ključa/arhive/oporavka moraju već postojati. Na Windowsu ograniči ACL direktorija na svoj račun i SYSTEM; Nodeov `mode: 0600` sam nije Windows ACL zaštita. Alat odbija ključ unutar repozitorija ili podatkovnog direktorija, odbija overwrite arhive i traži da arhiva bude izvan direktorija podataka. Ukupni ulaz ograničen je na 192 MiB zbog memorijskog formata; za veće volumene treba alat sa streamingom i standardnim vanjskim spremištem.

U šifriranom manifestu je `dataSource`. Kopija s oznakom `local-preview` sadrži lokalne spremljene podatke, a ne stanje produkcije. Dok se ne preuzme stvarni volumen, ne smije se tvrditi da postoji potpun produkcijski backup.

Za produkcijski izvoz koristi postojeći ovlašteni Railway CLI/SSH pristup, bez unošenja ključeva u Git:

```powershell
railway volume files --volume d11b1d77-4679-4df3-b0f5-d00411a76ddb download / C:/PetabitExport/App_Data --json
```

Provjeri sadržaj rezultirajućeg direktorija: alat za preuzimanje može smjestiti podatke u dodatni poddirektorij. Za aktivne baze ovaj file-level izvoz nije dovoljan: potreban je konzistentni dump/snapshot. Petabit trenutačno sprema provjerene JSON snapshotove atomarnim preimenovanjem; prije arhiviranja provjeri JSON i izostavi nedovršene `.tmp` datoteke iz probnog oporavka.

## Obvezna proba oporavka

1. Dešifriraj u novi direktorij. Provjera autentikacije i svih hash vrijednosti mora proći prije pisanja.
2. `git clone <restore>/repository.bundle <restore>/source` i `git -C <restore>/source fsck --full`. Provjeri da postoji očekivani commit iz manifesta; Git clone može odabrati drugu zadanu granu pa eksplicitno checkoutaj manifestov commit.
3. Iz vraćenog izvornog koda izgradi Docker sliku. Pokreni je na privatnoj testnoj mreži s vraćenim `App_Data`, bez javne domene i bez izlaza na internet. Za probu učitavanja ISS snapshota ostavi `StationSync:Enabled=true`; vrijednost `false` isključuje i učitavanje spremljenog ISS stanja. Nikad prvo ne vraćaj preko produkcijskog volumena.
4. Provjeri `/health/live`, prikaz stranica, ISS/Starlink snapshotove i njihova stvarna vremena dohvaćanja. Pri provjeri bez interneta sačuvani podaci moraju ostati dostupni i biti označeni starima kada je potrebno.
5. Zabilježi vrijeme, commit, porijeklo podataka i rezultat. Ciljevi su gubitak najviše 24 sata spremljenih podataka i oporavak unutar dva sata; nisu jamstvo dok cijeli produkcijski postupak nije proban.

## Raspored i odvajanje pristupa

Predloženi raspored: dnevne kopije podataka, kopija pri svakom izdanju, mjesečna proba vraćanja. Zadrži 7 dnevnih, 4 tjedne i 3 mjesečne neovisne kopije, prema kapacitetu odabranog spremišta. Spremi barem jednu kopiju izvan računala i izvan GitHub/Railway računa. Lokalna arhiva i ključ na istom računalu ne štite od krađe računala ni ransomwarea.

Vanjsko spremište treba imati verzioniranje i vremensku zaštitu od brisanja. Identitet koji piše backup ne bi smio imati ovlast brisanja zaključanih kopija. Novi račun, naplatni plan i vanjsko odredište odabire vlasnik. Automatski vanjski backup nije uključen dok to odredište i pristup nisu definirani.

## Postupak kod incidenta

- Zabilježi simptome, vrijeme i verziju; sačuvaj logove. Ne objavljuj tokene ni osobne podatke u izvještaju.
- Ako je kompromitiran račun/token, opozovi pristup i promijeni ga kroz službene postavke računa. Ponovno pokretanje aplikacije nije dovoljna sanacija.
- Za napad botovima provjeri `railway waf under-attack status --json`. Vlasnik može privremeno aktivirati način zaštite; preglednički challenge može blokirati API klijente i nadzorne provjere. Nije uključen trajno.
- Oporavak radi iz čiste provjerene verzije, u odvojenoj okolini, pa tek nakon provjere prebaci promet. Sačuvaj kompromitiranu kopiju za analizu.

Izvori: https://docs.railway.com/volumes/backups ; https://www.cisa.gov/stopransomware/ransomware-guide
