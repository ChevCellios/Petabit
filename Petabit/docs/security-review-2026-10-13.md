# Petabit: sigurnosni pregled 13. listopada 2026.

## Opseg

Pregledani su HTTP sloj i kontroleri, vanjski izvori i spremljeni podaci, frontend i orbitalni worker, ovisnosti, GitHub Actions i zaštita mastera, Docker deployment te šifrirani backup i oporavak. Produkcijske tajne nisu čitane, prikazane ni rotirane. Aktivni testovi zagušenja i neispravnih izvora koriste lokalne testne pomoćnike; javna stranica provjerava se samo kratkim funkcionalnim zahtjevima.

## Provedena poboljšanja

| Područje | Promjena | Provjera |
| --- | --- | --- |
| ISS dostupnost | Rok od osam sekundi obuhvaća i čitanje JSON tijela, ne samo dolazak HTTP zaglavlja. | Simulirani stream koji nikad ne dovrši tijelo završava kontroliranim 504 odgovorom. |
| NASA video | Jedan zajednički dohvat, rok 15 s i pauza 30 s nakon greške; prekid jednog posjetitelja ne prekida dohvat za ostale. | Paralelni posjetitelji, prekid čekanja, nepodržan embed, greška izvora i zaglavljeno tijelo odgovora. |
| Provjera spremnosti | Najviše četiri istodobne readiness provjere bez reda čekanja, uz globalne limite. Liveness ostaje izuzet. | Pet paralelnih blokiranih provjera: četiri prihvaćene, peta 429; liveness vraća 200. |
| Starlink integritet i worker | Nepoznati statusi ne postaju operativni; nepodržane deep-space orbite ne ulaze u propagaciju. | Neispravni statusi i stara deep-space orbita; svih 11.105 zapisa u pregledanim stvarnim snapshotovima ostaje podržano. |
| Starlink oporavak | Oštećen bootstrap gzip ne sprječava korištenje valjanog cachea ili izvora. | Test oštećene gzip datoteke i uspješnog nastavka do valjanih podataka. |
| Backup putanje | Rezervirana imena, manifest, preduge/preduboke putanje i kolizije datoteka/direktorija odbijaju se prije pisanja. | Testovi potvrđuju da odredište nije ni stvoreno nakon odbijanja; valjana arhiva i neovisni Git oporavak i dalje rade. |
| Backup verzija | Manifest koristi commit stvarno aktivnog Railway deploymenta, provjeren prije i poslije izvoza. | Master noviji od produkcije, promjena deploymenta tijekom izvoza i nepotpuni metapodaci; nepouzdan izvoz ne dolazi do uploada. |
| Ovisnosti | NuGet audit odbija i nedostupan/nepotpun advisory izvor. Docker koristi istu konfiguraciju. | Stvarni negativni testovi izvora završavaju s NU1900/NU1905 i izlaznim kodom 1; normalan restore/audit prolazi. |
| Tajne u izgradnji | Git/Docker isključuju lokalne konfiguracije vjerodajnica i privatne ključeve; Docker i lokalne agent podatke. | Provjereni ignore uzorci i odsutnost takvih praćenih datoteka pri pregledu. |
| GitHub Actions | Uključena obvezna puna SHA referenca; Dependabot automatsko spajanje veže se na točan commit događaja. | API readback potvrđuje `sha_pinning_required=true`; svih 26 postojećih Actions referenci kompatibilno. |

## Provjereno postojeće stanje

- Secret Scanning i Push Protection uključeni su; pri pregledu nema otvorenih Secret Scanning, Dependabot ni CodeQL upozorenja. Privatna prijava ranjivosti je uključena.
- Master zahtijeva osam obveznih CI/sigurnosnih provjera, ažurnu granu, linearnu povijest i riješene rasprave. Zaštita vrijedi i za administratore; force push i brisanje su zabranjeni. Pravilo bez obveznog drugog ljudskog reviewera ostaje raniji izbor projekta s jednim vlasnikom.
- Actions token zadano ima read ovlasti i ne može odobravati PR-ove. Privilegirani Dependabot workflow ne checkouta PR kod i prihvaća samo dopuštene NuGet patch promjene.
- Backup environment dopušta samo granu master. [Raspoređeni backup 38385599840](https://github.com/ChevCellios/Petabit/actions/runs/38385599840), 13. listopada, završio je uspješno. Šifriranje, ponovno preuzimanje i provjera bajtova, Git oporavak i opoziv privremenog SSH ključa dio su tog postupka; puna aplikacijska proba je mjesečna ili posebno zatražena.
- Postojeći CSP, antiforgery, HTTPS/HSTS, sigurne postavke kolačića, provjera hosta, zabrana automatskih upstream preusmjeravanja, limiti veličine, XML/regex zaštite i neprivilegirani aplikacijski proces ostaju aktivni.

## Dokazi i granice

Lokalno je prošlo 116 .NET i 37 JavaScript regresijskih testova, audit ovisnosti i negativni testovi advisory izvora. Tri Linux integracijska testa lokalno su preskočena. Linux backup integracijski testovi i privatni Docker/ZAP/Trivy postupak izvršavaju se na GitHub runneru; lokalni Docker servis nije bio pokrenut. Završni PR, CI i produkcijska provjera priloženi su u razgovoru.

[CI kontejnerski izvještaj](https://github.com/ChevCellios/Petabit/actions/runs/38392275461/job/115278725222) bilježi 14 nalaza u Ubuntu baznoj slici: devet MEDIUM i pet LOW, bez HIGH/CRITICAL nalaza. Za te zapise izvještaj nema `FixedVersion`; nije opravdano tvrditi da je slika bez ranjivosti. Cjelovit izvještaj zadržan je kao artifact. Dnevno skeniranje i provjera novih baznih slika ostaju važni; build nove slike sam ne ažurira već pokrenut deployment.

ZAP je zabilježio jednu informativnu poruku `Session Management Response Identified`, povezanu s detekcijom cookie/token zaglavlja, bez srednjih/visokih nalaza. Izvještaj je izričito klasificira kao informaciju koja ne traži popravak.

Ovo je pregled i hardening, bez tvrdnje o potpunom neovisnom penetracijskom testu. Nisu pronađeni potvrđeni izravni XSS/SSRF sinkovi u pregledanom kodu, što nije jamstvo da ne postoji drugi put napada.

Vlasnik još treba neovisno potvrditi MFA za GitHub, Railway i Microsoft, fizičku offline kopiju recovery ključa i dostavu failure obavijesti. OneDrive nije nepromjenjiva kopija zaštićena od brisanja kompromitiranim računom. Ranije odobren Railway token obuhvaća pet projekata, a Microsoft veza šire datoteke; izolirani identitet/spremište traži zaseban odabir vlasnika. Povijest Git e-mail metapodataka nije prepisivana.

Distribuirani DDoS ostaje odgovornost hosting ingressa. Aplikacijski limiti nisu zamjena za tu zaštitu. Nisu uključivani naplatni servisi niti mijenjani odobreni opsezi računa.

## Primarni izvori

- [Microsoft: čitanje HTTP tijela i rokovi](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpcompletionoption?view=net-10.0)
- [Microsoft: NuGet sigurnosni audit](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
- [Microsoft: rezervirana Windows imena](https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file)
- [GitHub: Actions policy API](https://docs.github.com/en/rest/actions/permissions)
