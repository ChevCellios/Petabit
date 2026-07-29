# 🌐 Petabit

![.NET](https://img.shields.io/badge/.NET-8.0-blueviolet)
![Language](https://img.shields.io/badge/language-C%23-orange)
![Localization](https://img.shields.io/badge/i18n-3%20languages-green)

Petabit je responzivna ASP.NET Core MVC aplikacija s višejezičnim sučeljem i jednostavnim ISS trackerom. Projekt je zamišljen kao portfolio demonstracija rada s .NET-om, Razor pogledima, lokalizacijom i dohvatom podataka iz vanjskih API-ja.

## Značajke

- Lokalizacija na hrvatski, engleski i njemački jezik
- Light i dark način prikaza, uz spremanje korisničkog odabira
- ISS tracker s trenutačnom lokacijom, brzinom i brojem astronauta na ISS-u
- Obrada nedostupnosti vanjskih servisa i vremenskog ograničenja zahtjeva
- Responzivan prikaz za desktop i mobilne uređaje
- Stranice za knjige, aplikacije, blockchain i privatnost

## Tehnologije

- .NET 8 i ASP.NET Core MVC
- C# i Razor Views
- Bootstrap 5
- JavaScript Fetch API
- `.resx` lokalizacijske datoteke
- [Where the ISS at?](https://wheretheiss.at/) i [Open Notify](http://open-notify.org/) API-ji

## Pokretanje lokalno

### Preduvjeti

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Koraci

```bash
git clone https://github.com/ChevCellios/Petabit.git
cd Petabit
dotnet restore --configfile NuGet.Config
dotnet run --project Petabit
```

Nakon pokretanja otvorite URL koji `dotnet run` ispiše u terminalu. U razvojnom okruženju možete otvoriti i `Petabit.sln` u Visual Studio 2022.

## ISS tracker

Klik na **Ping ISS** dohvaća podatke preko aplikacijskog endpointa `GET /Home/Data`. Poslužitelj dohvaća lokaciju i brzinu ISS-a te broj članova posade, čime se izbjegava problem blokiranih HTTP zahtjeva iz HTTPS preglednika.

Za prikaz svježih podataka aplikaciji je potreban pristup internetu. Ako vanjski servis privremeno nije dostupan, tracker prikazuje poruku o grešci bez rušenja stranice.

## Struktura projekta

```text
Petabit/
├── Controllers/       # MVC kontroleri i ISS endpoint
├── Models/            # modeli odgovora vanjskih API-ja
├── Resources/         # lokalizacijske datoteke
├── Views/             # Razor pogledi
├── wwwroot/           # CSS, JavaScript, zvukovi i slike
├── Program.cs         # konfiguracija aplikacije
└── Petabit.csproj
```

## Screenshot

![Petabit dark mode](Petabit/docs/screenshotPetabit.png)

## Kontakt

- GitHub: [Chev Cellios](https://github.com/chevcellios)
- E-mail: [midom.croatia@yahoo.com](mailto:midom.croatia@yahoo.com)
