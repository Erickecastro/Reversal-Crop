# Reversal Crop

Aplicativo desktop para enquadrar imagens e exportar ícones PNG com o perfil Reversal. A interface é feita com Avalonia UI e o processamento de imagem usa SkiaSharp.

## Recursos

- Abertura de imagens PNG, JPEG, WebP e BMP.
- Prévia assíncrona com ajuste de zoom e posição por sliders ou arraste.
- Exportação PNG em 512 × 512 e 2508 × 2508.
- Máscara Reversal com transparência e bordas arredondadas.

## Requisitos

- .NET 10 SDK.
- Linux com ambiente gráfico suportado pelo Avalonia (por exemplo, Fedora Workstation com GNOME).

## Executar

```bash
dotnet run --project src/ReversalCrop.App/ReversalCrop.App.csproj
```

## Testes

```bash
dotnet test tests/ReversalCrop.Core.Tests/ReversalCrop.Core.Tests.csproj
```

## Estrutura

- `src/ReversalCrop.App`: interface Avalonia e fluxo do aplicativo.
- `src/ReversalCrop.Core`: renderização e processamento de imagens.
- `tests/ReversalCrop.Core.Tests`: testes automatizados do renderizador.

## Perfil de recorte

A geometria usada é margem `4.5 / 64`, dimensão `55 / 64` e raio `15 / 64`. A exportação mantém supersampling e transparência PNG.
