# 🎸 Plettro

**Plettro** is a native Windows app for electric guitarists: a precise **tuner**, a sample-accurate **metronome** and a few practice tools. It is designed for USB audio interfaces such as the **Focusrite Scarlett Solo (3rd gen)**.

The interface is in Italian.

![Windows](https://img.shields.io/badge/Windows-10%20%2F%2011-0078D6) ![.NET](https://img.shields.io/badge/.NET-10-512BD4) ![License](https://img.shields.io/badge/licenza-MIT-green)

## Funzioni

### Accordatore
- Rilevamento dell'intonazione con algoritmo **YIN**: precisione sotto il cent, da 28 Hz (basso a 5 corde) a 1400 Hz
- Lancetta da −50 a +50 cent, con indicazioni chiare: "tendi la corda" o "allenta la corda"
- **16 accordature**: Standard, Drop D, Eb, D standard, Drop C#, Drop C, Drop B, C standard, Open G, Open D, Open E, DADGAD, 7 corde (standard e Drop A), basso a 4 e 5 corde
- **Modalità automatica** (riconosce la corda da sola) oppure **corda bloccata**: clicchi una corda e l'accordatore mira a quella nota, comodo per scendere di accordatura
- **Note di riferimento** da suonare in cuffia, per accordare a orecchio
- **Diapason regolabile** (La4 da 415 a 466 Hz)
- Le corde accordate vengono segnate con ✓
- Nomi delle note in inglese e in italiano (E / Mi)

### Metronomo
- Da 20 a 300 BPM; il suono viene generato direttamente nel flusso audio, quindi il tempo è preciso al campione e senza jitter
- Da 1/4 a 12/4, suddivisioni (ottavi, terzine, sedicesimi, sestine)
- **Accenti per battito**: clicca sui pallini per scegliere accento → normale → muto
- **Suoni di batteria** generati in tempo reale: **batteria completa** (crash e cassa sull'1, rullante su 2 e 4, charleston sugli altri battiti e sulle suddivisioni), **charleston (hi-hat)** e **piatto ride** con campana. Restano anche beep, legno (woodblock) e click.
- **Tap tempo**, rotellina del mouse e scorciatoie da tastiera
- Nome del tempo (Largo, Andante, Allegro, Presto…)

### Strumenti di allenamento
- **Allenatore di velocità**: il metronomo accelera da solo (per esempio +5 BPM ogni 4 battute) fino al BPM obiettivo
- **Battute mute**: il metronomo suona per N battute e poi tace per M battute, per allenare il tempo interno
- **Timer della sessione di pratica**

### Pensato per la Scarlett Solo
- Seleziona automaticamente la scheda Focusrite se è collegata
- Scelta del canale: **Ingresso 2 (strumento)**, Ingresso 1 (mic), Mix, oppure **Automatico** (usa il canale su cui c'è segnale)
- **Misuratore di livello con spia CLIP**, che aiuta a regolare il gain
- Consigli di configurazione direttamente nell'app

## Installazione

1. Scarica `Plettro.exe` dall'ultima [Release](../../releases/latest)
2. Avvialo con un doppio clic. Non serve installare nulla: il runtime .NET è già incluso nell'eseguibile.

> Windows SmartScreen potrebbe avvisare che l'app non è firmata: clicca **Ulteriori informazioni → Esegui comunque**.

## Configurare la Scarlett Solo (3ª gen)

1. Collega la chitarra all'**ingresso 2** (jack frontale) e premi **INST**
2. Regola il **GAIN** suonando forte: l'anello deve restare verde, al massimo ambra
3. In Plettro, sotto *Impostazioni audio*, scegli come ingresso **Analogue 1 + 2 (Focusrite USB Audio)** e come uscita **Speakers (Focusrite USB Audio)**
4. Attiva **Direct Monitor** sulla scheda per sentire la chitarra in cuffia senza latenza

## Scorciatoie

| Tasto | Azione |
|---|---|
| `Spazio` | Avvia o ferma il metronomo |
| `↑` / `↓` | ±1 BPM (con `Maiusc`: ±5) |
| `T` | Tap tempo |
| `F1` / `F2` / `F3` | Accordatore / Metronomo / Impostazioni |

## Compilare dai sorgenti

Requisiti: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
cd Plettro
dotnet run
```

Per creare l'eseguibile singolo:

```bash
dotnet publish Plettro -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

## Tecnologia

- WPF (.NET 10) con il tema Fluent scuro
- [NAudio](https://github.com/naudio/NAudio) per l'audio WASAPI (ingresso e uscita in modalità condivisa)
- Rilevatore di intonazione YIN con differenza vettorizzata (SIMD) e interpolazione parabolica
- Le impostazioni vengono salvate in `%AppData%\Plettro\settings.json`

## Licenza

MIT
