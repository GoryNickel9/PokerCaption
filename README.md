# PokerCaption

PokerCaption è un’applicazione desktop Windows per disporre le finestre dei tavoli, mostrare indicatori durante una sessione e compilare il campo dell’importo con formule definite dall’utente. Il repository contiene l’app WinForms, il componente nativo caricato nei processi del client e gli strumenti di compilazione e diagnostica. Questa pagina descrive il comportamento del sorgente per una revisione tecnica e di sicurezza.

## Funzioni e interazione con il client

| Funzione | Dati e comportamento |
| --- | --- |
| **Table Layout** | Rileva le finestre dei tavoli e, quando è abilitato, assegna ai nuovi tavoli il primo slot libero nell’ordine del profilo. Spostare una finestra su uno slot occupato scambia le assegnazioni. Una finestra rilasciata fuori dagli slot rimane dove l’utente l’ha posizionata. Gli slot attivi sono usati solo nei client che li supportano. |
| **HUD** | Legge i dati riconosciuti nell’interfaccia del tavolo, tra cui piatto, stack visibili, nomi o titoli esposti e azioni. Usa finestre overlay proprie. I tempi visualizzati derivano dai timestamp degli eventi osservati e possono mancare quando la sequenza è incompleta. Il General HUD mostra statistiche di sessione. |
| **BetScript** | Applica la prima formula utilizzabile per la street corrente. Le formule sono espressioni numeriche del profilo e usano valori disponibili come piatto, buio, stack e importo da chiamare. Il motore compila il campo una volta per turno riconosciuto. La conferma della puntata resta al giocatore. |
| **Rotellina e dialoghi** | La rotellina può variare l’importo. Le regole abilitate per i dialoghi possono chiudere finestre selezionate o attivare pulsanti specifici. Queste funzioni hanno percorsi distinti da BetScript. |

BetScript resta disponibile con Table Manager acceso. Sul client renderizzato usa lo stato delle azioni acquisito dalla scansione, richiede dati sufficienti dello stesso turno e rimanda la compilazione finché la finestra si sposta. La scrittura a coordinate richiede il tavolo in primo piano e verifica la finestra che riceverebbe l’input nel punto del campo. Se la formula richiede dati assenti o non applicabili, il motore prova la regola successiva o lascia il campo com’è. La modalità layout del client renderizzato sospende `PrintWindow`, la rotellina e i dialoghi automatici. Restano attivi la scansione nativa, l’HUD, il campionamento di pixel visibili per le statistiche e BetScript.

## Processi, privilegi e confini di fiducia

1. **Applicazione desktop.** L’eseguibile WinForms è x86. Enumera le finestre, identifica i client mediante nomi di processo, classi e titoli, e usa API Win32 per spostamento, overlay e interazione. I percorsi di cattura usano `PrintWindow` o pixel visibili a seconda della modalità. Alcune operazioni usano messaggi Windows, altre input sintetico a coordinate.
2. **Codice nel processo del client.** L’avvio normale installa hook di messaggistica Windows con `captionHook.dll`. Gli hook hanno portata desktop secondo l’installazione Win32. La selezione dei processi e dei tavoli usati dall’app avviene nei componenti che ricevono ed elaborano gli eventi. Il modulo nativo ispeziona oggetti dell’interfaccia del client renderizzato e acquisisce aggiornamenti selezionati. La build x86 collega MinHook. Il repository compila anche la DLL x64. L’architettura e la versione del client condizionano la compatibilità effettiva.
3. **Comunicazione locale.** I componenti scambiano comandi e osservazioni tramite una mappatura di memoria denominata nella sessione Windows e messaggi Windows. La mappatura è creata con gli attributi di sicurezza predefiniti del processo. Per la revisione, verificare chi può accedervi e inviare messaggi, la validazione di indici e lunghezze e il comportamento con dati incompleti o alterati. L’applicazione usa processi, finestre e IPC locali per queste funzioni.
4. **Impatto operativo.** Hook, lettura degli oggetti nella memoria del client, cattura della finestra e input sintetico sono superfici separate. Un aggiornamento del client può rendere incompatibili i controlli, le coordinate o gli oggetti interni attesi. I test locali coprono questi percorsi con fixture. La stabilità e la correttezza sul client reale richiedono una prova separata in un ambiente autorizzato.

## Dati su disco e diagnostica

I dati sono salvati accanto all’eseguibile. Il processo deve poter scrivere nella cartella di esecuzione.

| Percorso | Contenuto e visibilità |
| --- | --- |
| `PokerCaption.ini`, `PokerCaption.ini_bak`, `client_profile.ini` | Impostazioni, formule, profilo del client, coordinate ed eventuale copia precedente salvata dall’applicazione. |
| `TableManagerConfigs/` | Slot e geometrie dei profili locali. Gli scambi fatti trascinando i tavoli riguardano le assegnazioni correnti. Riattivare il manager o un profilo ricrea le assegnazioni. |
| `global_stats` | Statistiche cumulative di sessione. |
| `main_log.txt` | Log ricreato all’avvio. Può contenere argomenti di avvio, titoli delle finestre, testo campionato dai tavoli ed errori diagnostici. |
| `tablesnap/`, `snap_result.txt`, `scan_client.txt` e report di test | Output delle funzioni diagnostiche esplicite. Le acquisizioni possono mostrare informazioni visibili sul tavolo. I report dei test usano invece dati sintetici. |

Trattare log e acquisizioni come dati potenzialmente identificativi. Questa revisione riguarda il codice e la build indicata, con le impostazioni e il profilo effettivamente usati. Verificare la conformità delle singole funzioni alle regole dell’ambiente prima di collegare l’applicazione, incluse le funzioni di temporizzazione e di input automatico.

## Compilazione e verifica riproducibile

Su Windows servono .NET SDK 8, il runtime .NET Desktop 8 x86 per eseguire l’app e Zig disponibile in `PATH`. Dalla radice del repository, in una sessione desktop interattiva:

```powershell
.\build.cmd
Set-Location .\src\PokerCaption\bin\Release\net8.0-windows
.\PokerCaption.exe --ui-preview
.\PokerCaption.exe --selftest2
```

`--ui-preview` apre l’interfaccia senza collegarsi al client. `--selftest2` usa finestre di prova locali, verifica fra l’altro layout, rendering, formule e dialoghi, e scrive `selftest2.txt`. Per verificare l’esecuzione reale usare un profilo di test, registrare la versione del client e separare le prove di sola disposizione finestre da quelle di acquisizione e di input.

## Punti di ingresso per la revisione del codice

- [Avvio, profili e diagnostica](src/PokerCaption/Program.cs) e [servizio tavoli](src/PokerCaption/Engine/TableService.cs).
- [Hook e IPC Windows](src/PokerCaption/Native/NativeLayer.cs), [componente nativo](native/captionHook.c), [compilazione delle DLL](native/build.ps1) e [licenza MinHook](native/third_party/minhook/LICENSE.txt).
- [Assegnazione degli slot](src/PokerCaption/Engine/TableManagerEngine.cs), [HUD](src/PokerCaption/Engine/HudEngine.cs) e [storico azioni](src/PokerCaption/Engine/SeatActionHistory.cs).
- [Formule e precompilazione](src/PokerCaption/Engine/BetScriptEngine.cs), [input a coordinate](src/PokerCaption/Engine/CoordsEngine.cs), [rotellina](src/PokerCaption/Engine/HotkeyEngine.cs) e [dialoghi](src/PokerCaption/Engine/AutoDialogEngine.cs).
