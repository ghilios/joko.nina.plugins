# Hocus Focus — a NINA plugin

Improved **star detection**, **star annotation**, **autofocus**, and **tilt / aberration inspection**
for [NINA](https://nighttime-imaging.eu/) (Nighttime Imaging 'N' Astronomy).

📖 **Documentation:** **<https://ghilios.github.io/hocus-focus/>**

The documentation covers:

- An overview of the plugin and its key features.
- A complete reference for **every star-detection setting** — what it does, when it helps, and when it
  doesn't, with illustrative figures.
- A technical deep-dive on the **star-detection optimization** approach (the objective function, the
  search algorithm, and how each setting factors in).
- An analysis of which settings could be derived **heuristically** rather than tuned empirically.

## Installation

Hocus Focus is published through NINA's in-app plugin manager (Plugins → Available). See the
documentation for details.

## Building from source

This is a .NET 8 (Windows) class library. Open `Joko.NINA.Plugins/Joko.NINA.Plugins.sln` in Visual
Studio, or build from the command line:

```
dotnet build Joko.NINA.Plugins/Joko.NINA.Plugins.sln -c Debug
```

## License

Licensed under the [Mozilla Public License 2.0](LICENSE.txt).

— George Hilios (jokogeo)
