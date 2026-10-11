# Petabit — sigurnosni pregled, 11. listopada 2026.

Pregledano 5/5 stavki. Nakon pregleda vlasnik je odobrio dodatna ograničenja Dependabot workflowa opisana niže. Git povijest i pravila zaštite grane nisu mijenjani.

- [x] GitHub Secret Scanning i Push Protection: obje zaštite uključene; nema otvorenih secret-scanning upozorenja. Non-provider patterns i validity checks nisu uključeni.
- [x] Zaštita mastera i spajanje PR-ova: osam obveznih provjera, ažurna grana, linearna povijest i riješene rasprave. Force push i brisanje zabranjeni; zaštita vrijedi za administratore, ruleset nema bypass identitete. Ljudsko odobrenje i potpisani commitovi nisu obvezni. Za projekt jednog vlasnika dodatni obvezni reviewer traži dogovor o tome tko pregledava promjene.
- [x] Gitleaks: posljednje dnevno izvršavanje Security workflowa, 10. listopada 2026., uspješno je završilo za produkcijski commit `7cbdc2548525232f0408bbc65203aa53218b315a`. Skeniranje pune dohvaćene Git povijesti koristi `--log-opts=--all` i redakciju izlaza. Ovo nije novo lokalno izvršavanje skenera: Docker servis trenutačno nije pokrenut.
- [x] Git e-mail metapodaci: u lokalno dostupnoj povijesti osobna Gmail adresa pojavljuje se kao autor u 102 commita i committer u 69 commita. Trenutačna lokalna konfiguracija koristi vlasnikovu GitHub noreply adresu; ostale adrese pripadaju GitHubu i Dependabot botu. Adresa je podatak o privatnosti, sama po sebi nije pristupni ključ. Povijest nije prepisivana; takav zahvat mijenja commit hash vrijednosti i ne uklanja postojeće vanjske kopije.
- [x] Dependabot auto-merge: pregledan postojeći workflow i otvoreni PR-ovi. PR #74 (NuGet patch) ima auto-merge, PR #75 (GitHub Actions major) nema. Workflow koristi pinanu metadata akciju, ne checkouta niti izvršava PR kod, a `gh pr merge --auto --squash` poštuje zaštitu grane.

## Odobrena dodatna ograničenja Dependabota

Prijašnji workflow provjeravao je `github.actor` i patch vrstu, bez zasebne provjere autora PR-a, ciljne grane, izvornog repozitorija i ekosustava.

Odobrena promjena:

1. Zahtijevati Dependabot kao autora i izvršitelja događaja, cilj `master` i isti izvorni repozitorij.
2. Automatski spajati samo dopuštene NuGet patch nadogradnje nakon svih osam provjera. GitHub Actions, Docker i orbitalni JavaScript paket ostaviti za pregled vlasnika.
3. Ograničiti write dozvole na posao koji uključuje auto-merge; zadržati odsutnost checkouta i izvršavanja PR koda na privilegiranom `pull_request_target` događaju.
4. Otvoreni PR-ovi provjereni su pri izmjeni: #74 je NuGet patch i zadržava auto-merge uz obvezne provjere; #75 je GitHub Actions major i nema auto-merge. Nije bilo prethodno uključenih opcija izvan nove politike koje bi trebalo opozvati. Pri budućem sužavanju politike ponoviti ovu provjeru jer izmjena workflowa sama nije opoziv već uključenog auto-mergea.

Patch oznaka nije jamstvo sigurnosti. Svaki neuspjeh obveznih provjera i dalje mora blokirati spajanje.

## Dokazi

- Security izvršavanje: https://github.com/ChevCellios/Petabit/actions/runs/38047861788
- Aktivni ruleset: https://github.com/ChevCellios/Petabit/rules/23294034
- Workflow: `.github/workflows/dependabot-automerge.yml`
- Lokalni Git metapodaci provjereni preko svih lokalno dostupnih referenci; brojevi se mogu razlikovati od povijesti samo grane master.
