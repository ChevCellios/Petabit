{ pkgs ? import <nixpkgs> {} }:

pkgs.mkShell {
  buildInputs = [
    pkgs.dotnet-sdk-6.0
  ];

  shellHook = ''
    echo "Development shell with .NET 6 SDK"
  '';
}
