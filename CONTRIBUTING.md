# Doprinos projektu Petabit

Hvala što želiš pomoći projektu. Povratne informacije, prijave grešaka i dobro obrazloženi prijedlozi poboljšanja su dobrodošli.

## Prije otvaranja prijave

- provjeri postoji li već sličan GitHub Issue
- za sigurnosni problem nemoj javno objavljivati osjetljive detalje ni tajne
- opiši konkretan problem ili cilj, bez uključivanja osobnih podataka

## Prijava greške

Koristi predložak **Prijava greške** i navedi:

- jasne korake za reprodukciju
- očekivano i stvarno ponašanje
- preglednik, operacijski sustav i relevantne snimke zaslona

## Prijedlog poboljšanja

Koristi predložak **Prijedlog funkcionalnosti**. Objasni komu bi promjena koristila, koji problem rješava i kako bi se uklopila u postojeći projekt.

## Lokalni razvoj

Potrebni su .NET SDK 10 i Git.

```bash
git clone https://github.com/ChevCellios/Petabit.git
cd Petabit
dotnet restore Petabit.sln
dotnet build Petabit.sln --configuration Release --no-restore
dotnet test Petabit.sln --configuration Release --no-build
```

Pokretanje aplikacije:

```bash
dotnet run --project Petabit/Petabit.csproj
```

## Pull requestovi

- ograniči PR na jednu povezanu promjenu
- opiši razlog i korisnički učinak promjene
- dodaj ili prilagodi testove kada se mijenja ponašanje aplikacije
- provjeri da build, testovi i sigurnosne provjere prolaze
- ne uključuj ključeve, lozinke, privatne adrese ni druge osjetljive podatke

Slanjem doprinosa prihvaćaš da se on objavi pod [MIT licencom](LICENSE).
