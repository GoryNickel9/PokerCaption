# PokerCaption

PokerCaption è un’applicazione desktop Windows per disporre le finestre dei tavoli, mostrare indicatori durante una sessione e compilare il campo dell’importo con formule definite dall’utente. Il repository contiene l’app WinForms, il componente nativo caricato nei processi del client e gli strumenti di compilazione e diagnostica. Questa pagina descrive il comportamento del sorgente per una revisione tecnica e di sicurezza.

## Funzioni e interazione con il client

| Funzione | Dati e comportamento |
| --- | --- |
| **Table Layout** | Rileva le finestre dei tavoli e, quando è abilitato, assegna ai nuovi tavoli il primo slot libero nell’ordine del profilo. Spostare una finestra su uno slot occupato scambia le assegnazioni. Una finestra rilasciata fuori dagli slot rimane dove l’utente l’ha posizionata. Gli slot attivi sono usati solo nei client che li supportano. |
| **HUD** | Legge i dati riconosciuti nell’interfaccia del tavolo, tra cui piatto, stack visibili, nomi o titoli esposti e azioni. Usa finestre overlay proprie. I tempi visualizzati derivano dai timestamp degli eventi osservati e possono mancare quando la sequenza è incompleta. Il General HUD mostra statistiche di sessione. |
| **BetScript** | Applica la prima formula utilizzabile per la street corrente. Le formule sono espressioni numeriche del profilo e usano valori disponibili come piatto, buio, stack e importo da chiamare. Il motore compila il campo una volta per turno riconosciuto. La conferma della puntata resta al giocatore. |
| **Rotellina e dialoghi** | La rotellina può variare l’importo. Le regole abilitate per i dialoghi possono chiudere finestre selezionate o attivare pulsanti specifici. Queste funzioni hanno percorsi distinti da BetScript. |

BetScript resta disponibile con Table Manager acceso. Sul client renderizzato usa lo stato delle azioni acquisito dalla scansione, richiede dati sufficienti dello stesso turno e rimanda la compilazione finché la finestra si sposta. Ogni tavolo ha un turno di precompilazione indipendente. Sul tavolo in primo piano e sotto il cursore il motore può usare l’input di Windows, ripristinando la posizione del mouse. Negli altri casi invia l’importo alla singola finestra, senza portarla in primo piano o spostare il cursore. Se la formula richiede dati assenti o non applicabili, il motore prova la regola successiva o lascia il campo com’è. La modalità layout del client renderizzato sospende `PrintWindow`, la rotellina e i dialoghi automatici. Restano attivi la scansione nativa, l’HUD, il campionamento di pixel visibili per le statistiche e BetScript.

## Architettura e flusso dei dati

| Livello | Responsabilità |
| --- | --- |
| **Interfaccia WinForms** | `Program` carica il profilo e la configurazione, inizializza una sola istanza dell’app e avvia `MainUiController`. `DesktopWindow` costruisce i menu. Il controller collega i controlli ai valori INI, aggiorna gli overlay e gestisce la chiusura. |
| **Servizio tavoli** | `TableService` enumera le finestre del client, associa una finestra a ogni tavolo e richiede scansioni. Ricava tipo di gioco e bui dal titolo. Collega le osservazioni più recenti a `TableInfo` e mantiene contatori di turno e di mano. Il timer WinForms aggiorna l’interfaccia. Un thread separato campiona i pixel per rilevare se Hero è in mano. |
| **Modulo nativo** | `captionHook.dll` osserva gli aggiornamenti di testo e visibilità e legge etichette e posizioni dei nodi visibili nel client renderizzato. Associa le azioni ai posti e conserva una coda circolare numerata con timestamp. Le scansioni pubblicano anche piatto, stack, numero di carte sul board, il posto identificato dal nome di Hero e la presenza di un controllo per impostare un importo. |
| **Motori** | `SeatActionHistory` combina gli eventi acquisiti con le etichette ancora visibili, evitando i duplicati. `HudTags` risolve i formati e `HudEngine` e `GenHud` disegnano overlay separati. `SessionStats` conserva tempo attivo e mani. `TableManagerEngine` gestisce le coordinate delle finestre. `BetScriptEngine` valuta le formule. `AutoDialogEngine` e `HotkeyEngine` gestiscono rispettivamente dialoghi e rotellina. |
| **Persistenza e input** | `IniFile` legge e salva le configurazioni. `TableManagerConfig` legge e scrive i profili degli slot. `Input` e `CoordsEngine` traducono gli aggiornamenti del campo importo in operazioni sui controlli Windows o in input sintetico sul tavolo renderizzato. |

Ogni aggiornamento dell’HUD usa i dati già acquisiti. I pannelli HUD sono finestre associate al rispettivo tavolo. Restano visibili sulle parti scoperte anche quando un’altra finestra ha il primo piano. Windows oscura i pannelli nelle zone coperte. Il parser di BetScript lavora in chip e converte il risultato per i campi visualizzati in BB. Il caso verificato sul client è Hold’em 100/200 Play Money con Table Manager acceso. La copertura delle altre varianti e dei diversi ridimensionamenti va verificata separatamente.

## Menu e controlli

### HUD

| Controllo | Comportamento |
| --- | --- |
| **Table HUD** | Abilita o disabilita gli overlay dei pannelli tavolo e giocatore. **Lock HUD** blocca il trascinamento dei pannelli giocatore. I pannelli tavolo sono già disposti automaticamente in colonna. |
| **General HUD** | Mostra o nasconde l’overlay di sessione. **Lock General HUD** blocca il trascinamento dell’intero overlay. Quando è sbloccato, l’ultima posizione viene salvata. |
| **Table panels** | Mostra dati del tavolo in una colonna centrata nella parte alta. **Format** combina testo e tag. **Insert tag** propone importo da chiamare in BB, pot odds in percentuale e generatore casuale. **Visible** controlla la visibilità del singolo pannello e **Text colour** il colore. |
| **Player panels** | Mostra i formati vicino ai posti. **Insert tag** propone `[actions]`, che mostra le azioni avversarie per street. Hero è escluso dallo storico visualizzato. **Visible** e **Text colour** agiscono sul pannello selezionato. Un pannello giocatore sbloccato può essere spostato per posto. |
| **General HUD panels** | **Format** e **Insert tag** permettono di inserire testo, numero di mani, tempo di sessione e mani per ora. **Text colour**, **Font** e **Background** controllano l’aspetto. Quando l’overlay generale è attivo, tutte le righe sono visibili, centrate e con sfondo opaco. |
| **Azioni sulle liste** | **Add** crea un pannello, **Delete** rimuove il selezionato e **Up / Down** cambia l’ordine. **Duplicate** duplica un pannello tavolo o giocatore. Nel selettore di tag, **Filter tags** restringe la lista, **Insert** inserisce il tag al cursore e **Cancel** chiude il selettore. |
| **Action symbols** | La colonna **Action** identifica l’azione. **Symbol** cambia il testo mostrato, compreso il separatore fra street. **Add timings** aggiunge i secondi accanto alle azioni con timestamp osservabile. **Apply** salva e lascia aperto il dialogo, **Ok** salva e chiude, **Cancel** scarta le modifiche successive all’ultimo salvataggio. |

Un’azione può avere un simbolo senza durata se è stata recuperata da un’etichetta visibile. Lo storico parte dalle osservazioni disponibili: le azioni già scomparse prima della lettura non possono essere ricostruite. Il menu contestuale del General HUD permette di azzerare il tempo, le mani della sessione o tutti i tag di sessione. `[handsPerHour]` si aggiorna a ogni nuova mano registrata usando il tempo attivo accumulato e resta fermo tra le mani. Fino a quando il tempo attivo è zero mostra `0`. I reset di tempo o mani azzerano anche il valore derivato.

### Table Layout

| Controllo | Comportamento |
| --- | --- |
| **Enable Table Manager** | Abilita l’assegnazione e il ridimensionamento delle finestre. I nuovi tavoli prendono il primo slot libero. Quando tutti gli slot sono occupati, il nuovo tavolo resta nella posizione scelta dal client finché si libera uno slot. Un trascinamento su uno slot occupato scambia le assegnazioni. Un tavolo rilasciato fuori dagli slot resta libero. Nei client compatibili i nuovi tavoli possono usare gli slot attivi già definiti nel profilo. |
| **Layout**, **New**, **Duplicate**, **Delete** | Selezionano il profilo da modificare, ne creano uno vuoto, duplicano quello selezionato o lo eliminano dopo conferma. La scelta nella lista prepara la modifica. **Save and activate** salva il profilo e lo rende quello in uso. |
| **Add slot**, **Up**, **Down**, **Remove** | Creano uno slot base, cambiano la priorità di assegnazione o rimuovono lo slot selezionato. |
| **Slot geometry** | **Left** e **Top** indicano la posizione sul desktop. **Width** imposta la larghezza in pixel. L’altezza segue le proporzioni del tavolo. La geometria degli slot già assegnati nel profilo attivo si può provare prima del salvataggio. |

### BetScript

**Add rule** crea una formula, **Duplicate** ne copia i campi, **Delete** elimina la selezione e **Move up / Move down** cambia l’ordine di valutazione. **Formula** contiene l’espressione numerica, il pulsante **?** apre la guida e **Streets** sceglie su quali fasi può essere applicata. **Round to (% of BB)** arrotonda al passo indicato in percentuale del buio. **Shove above (% of stack)** usa lo stack completo oltre la soglia, quando il totale è determinabile. La precompilazione può essere regolata lasciando vuota la lista delle regole o rimuovendo una street dalle regole. Il pulsante di conferma della puntata resta sotto il controllo del giocatore. Se, di fronte a una puntata, il risultato della formula è pari o inferiore a quanto serve per chiamare, BetScript usa il totale di rilancio mostrato dal client quando è disponibile. Con un piatto di 4,25 BB, `pot * 0.33` produce 1,40 BB. Se chiamare costa 1,42 BB e il client mostra **Raise To 2.83 BB**, BetScript compila 2,83 BB.

Le formule supportano operatori aritmetici, parentesi e il suffisso `bb` per esprimere un valore in grandi bui. `pot` è il piatto corrente, `spot` il piatto a inizio street, `ante` l’ante per giocatore e `antes` il totale degli ante. `bet` indica l’importo da pareggiare, `raise` l’ultima puntata o rilancio osservato, `hbet` il contributo di Hero nella street e `call` quanto serve per chiamare. `stack`, `effStack`, `betbox`, `sb` e `bb` indicano stack, stack effettivo, campo importo e bui. Alcuni valori dipendono dal client e dallo stato della mano. Per esempio `pot * 0.5` calcola metà piatto e `3.5bb` indica 3,5 grandi bui. `@0`–`@15` imposta il numero di decimali, con predefinito `2`, e `@H` lascia invariato il testo nel campo.

### Options

| Gruppo | Controlli |
| --- | --- |
| **Application** | **Minimize to tray** nasconde la finestra principale nell’area di notifica quando viene minimizzata. **Close PokerCaption after the last client closes** chiude PokerCaption dopo che l’ultima lobby o finestra tavolo è sparita. La funzione si attiva dopo il primo client rilevato. |
| **Table preferences** | **Never sit out on cash tables**, **Maximum** con il valore numerico e **Never sit out on tournament tables** appaiono disabilitati in questa build. |
| **Table closure** | I tre **Auto close** chiudono rispettivamente il tavolo segnato come chiuso, il tavolo su cui il gioco è terminato e il tavolo quando il torneo è finito. **Close tournament lobby when its last table closes** chiude la lobby del torneo identificato quando il suo ultimo tavolo viene chiuso. |

In **Dialogs** ogni voce lavora sul dialogo riconosciuto. Le opzioni di chiusura hanno questi ambiti.

| Voci | Risultato |
| --- | --- |
| **table is closed** | Chiude il dialogo che segnala la chiusura del tavolo. |
| **Close Current Session Information**, **Keep playing in Current Session**, **Stay connected on inactivity timeout** | Attivano rispettivamente i pulsanti di chiusura della scheda informativa, prosecuzione della sessione e mantenimento della connessione. |

Le azioni sui dialoghi dipendono dal titolo, dal testo o dalla struttura del dialogo riconosciuto. Sul client renderizzato sono sospese mentre Table Manager è attivo. **Save settings**, nella barra laterale, salva le impostazioni e conserva una copia del file precedente. Nell’area di notifica, **Mostra** riapre la finestra, **Config INI…** apre l’editor locale delle impostazioni e **Esci** chiude l’applicazione.

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
